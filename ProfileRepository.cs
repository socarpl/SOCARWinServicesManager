using Microsoft.Data.Sqlite;
using System.IO;

namespace Socar.WinServicesManager;

public sealed class ProfileRepository
{
    private readonly string _databasePath;

    public ProfileRepository()
        : this(SharedRuntimeConfig.ResolveDatabasePath())
    {
    }

    public ProfileRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    public string DatabasePath => _databasePath;

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath
    }.ToString();

    public void Initialize()
    {
        using var connection = OpenConnection();
        ExecuteNonQuery(connection, """
            create table if not exists profiles (
                id integer primary key autoincrement,
                name text not null unique,
                created_at text not null,
                updated_at text not null
            );
            """);
        ExecuteNonQuery(connection, """
            create table if not exists profile_service_actions (
                id integer primary key autoincrement,
                profile_id integer not null references profiles(id) on delete cascade,
                service_name text not null,
                display_name text null,
                desired_start_type integer null,
                desired_status text null,
                unique(profile_id, service_name)
            );
            """);
        ExecuteNonQuery(connection, """
            create table if not exists app_settings (
                key text primary key,
                value text not null
            );
            """);

        if (!SettingExists(connection, "dependency_stop_policy"))
        {
            SaveSetting(connection, "dependency_stop_policy", DependencyStopPolicy.AutoStopDependents.ToString());
        }
    }

    public IReadOnlyList<ServiceProfile> GetProfiles()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "select id, name, created_at, updated_at from profiles order by name";

        var profiles = new List<ServiceProfile>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            profiles.Add(new ServiceProfile
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                CreatedAt = DateTime.Parse(reader.GetString(2)),
                UpdatedAt = DateTime.Parse(reader.GetString(3))
            });
        }

        return profiles;
    }

    public ServiceProfile GetProfile(long id)
    {
        using var connection = OpenConnection();
        var profile = GetProfileHeader(connection, id) ?? throw new InvalidOperationException("Profile was not found.");
        profile.Actions = GetActions(connection, id).ToList();
        return profile;
    }

    public void SaveProfile(ServiceProfile profile)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");

        if (profile.Id == 0)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "insert into profiles (name, created_at, updated_at) values ($name, $created, $updated); select last_insert_rowid();";
            insert.Parameters.AddWithValue("$name", profile.Name);
            insert.Parameters.AddWithValue("$created", now);
            insert.Parameters.AddWithValue("$updated", now);
            profile.Id = (long)(insert.ExecuteScalar() ?? 0L);
        }
        else
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "update profiles set name = $name, updated_at = $updated where id = $id";
            update.Parameters.AddWithValue("$name", profile.Name);
            update.Parameters.AddWithValue("$updated", now);
            update.Parameters.AddWithValue("$id", profile.Id);
            update.ExecuteNonQuery();

            using var deleteActions = connection.CreateCommand();
            deleteActions.Transaction = transaction;
            deleteActions.CommandText = "delete from profile_service_actions where profile_id = $profileId";
            deleteActions.Parameters.AddWithValue("$profileId", profile.Id);
            deleteActions.ExecuteNonQuery();
        }

        foreach (var action in profile.Actions)
        {
            using var insertAction = connection.CreateCommand();
            insertAction.Transaction = transaction;
            insertAction.CommandText = """
                insert into profile_service_actions
                    (profile_id, service_name, display_name, desired_start_type, desired_status)
                values
                    ($profileId, $serviceName, $displayName, $startType, $status)
                """;
            insertAction.Parameters.AddWithValue("$profileId", profile.Id);
            insertAction.Parameters.AddWithValue("$serviceName", action.ServiceName);
            insertAction.Parameters.AddWithValue("$displayName", (object?)action.DisplayName ?? DBNull.Value);
            insertAction.Parameters.AddWithValue("$startType", action.DesiredStartType is null ? DBNull.Value : (int)action.DesiredStartType.Value);
            insertAction.Parameters.AddWithValue("$status", action.DesiredStatus?.ToString() ?? (object)DBNull.Value);
            insertAction.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void DeleteProfile(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "delete from profiles where id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public AppSettings GetSettings()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "select value from app_settings where key = 'dependency_stop_policy'";
        var value = command.ExecuteScalar()?.ToString();
        return new AppSettings
        {
            DependencyStopPolicy = Enum.TryParse<DependencyStopPolicy>(value, out var policy)
                ? policy
                : DependencyStopPolicy.AutoStopDependents
        };
    }

    public void SaveSettings(AppSettings settings)
    {
        using var connection = OpenConnection();
        SaveSetting(connection, "dependency_stop_policy", settings.DependencyStopPolicy.ToString());
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "pragma foreign_keys = on";
        command.ExecuteNonQuery();

        return connection;
    }

    private static ServiceProfile? GetProfileHeader(SqliteConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select id, name, created_at, updated_at from profiles where id = $id";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ServiceProfile
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            CreatedAt = DateTime.Parse(reader.GetString(2)),
            UpdatedAt = DateTime.Parse(reader.GetString(3))
        };
    }

    private static IReadOnlyList<ProfileServiceAction> GetActions(SqliteConnection connection, long profileId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select id, profile_id, service_name, display_name, desired_start_type, desired_status
            from profile_service_actions
            where profile_id = $profileId
            order by display_name, service_name
            """;
        command.Parameters.AddWithValue("$profileId", profileId);

        var actions = new List<ProfileServiceAction>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            actions.Add(new ProfileServiceAction
            {
                Id = reader.GetInt64(0),
                ProfileId = reader.GetInt64(1),
                ServiceName = reader.GetString(2),
                DisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
                DesiredStartType = reader.IsDBNull(4) ? null : (ServiceStartType)reader.GetInt32(4),
                DesiredStatus = reader.IsDBNull(5) ? null : Enum.Parse<DesiredServiceStatus>(reader.GetString(5))
            });
        }

        return actions;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static bool SettingExists(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from app_settings where key = $key";
        command.Parameters.AddWithValue("$key", key);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void SaveSetting(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            insert into app_settings (key, value)
            values ($key, $value)
            on conflict(key) do update set value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
