using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Socar.WinServicesManager;

public static class SharedRuntimeConfig
{
    public const string DatabasePathEnvironmentVariable = "WINDOWS_SERVICE_MANAGER_DB";
    public const string MainAppPathEnvironmentVariable = "WINDOWS_SERVICE_MANAGER_APP";
    public const string ConfigFileName = "service-manager.config.json";
    public const string PipeName = "Socar.WinServicesManager.ProfileRunner";

    public static string ResolveDatabasePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(DatabasePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var config = LoadOrCreate();
        return string.IsNullOrWhiteSpace(config.DatabasePath)
            ? Path.Combine(AppContext.BaseDirectory, "service-profiles.db")
            : ExpandPath(config.DatabasePath);
    }

    public static string ResolveMainAppPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(MainAppPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var config = LoadOrCreate();
        return string.IsNullOrWhiteSpace(config.MainAppPath)
            ? Path.Combine(AppContext.BaseDirectory, "Socar.WinServicesManager.exe")
            : ExpandPath(config.MainAppPath);
    }

    public static string ResolveConfigPath()
    {
        return Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }

    public static RuntimeConfig LoadOrCreate()
    {
        var path = ResolveConfigPath();
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<RuntimeConfig>(json, JsonOptions);
            return config ?? CreateDefault(path);
        }

        return CreateDefault(path);
    }

    public static void EnsureMainAppConfig()
    {
        var path = ResolveConfigPath();
        var config = LoadOrCreate();
        var changed = false;

        if (string.IsNullOrWhiteSpace(config.DatabasePath))
        {
            config.DatabasePath = Path.Combine(AppContext.BaseDirectory, "service-profiles.db");
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(config.MainAppPath))
        {
            config.MainAppPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Socar.WinServicesManager.exe");
            changed = true;
        }

        if (changed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        }
    }

    private static RuntimeConfig CreateDefault(string path)
    {
        var config = new RuntimeConfig
        {
            DatabasePath = string.Empty,
            MainAppPath = string.Empty
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        return config;
    }

    private static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.IsPathRooted(expanded)
            ? expanded
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expanded));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}

public sealed class RuntimeConfig
{
    [JsonPropertyName("databasePath")]
    public string? DatabasePath { get; set; }

    [JsonPropertyName("mainAppPath")]
    public string? MainAppPath { get; set; }
}
