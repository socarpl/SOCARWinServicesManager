namespace Socar.WinServicesManager;

public static class IpcMessages
{
    public const string RunProfilePrefix = "RUN_PROFILE_ID:";

    public static string RunProfile(long profileId)
    {
        return $"{RunProfilePrefix}{profileId}";
    }

    public static bool TryParseRunProfile(string message, out long profileId)
    {
        profileId = 0;
        if (!message.StartsWith(RunProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return long.TryParse(message[RunProfilePrefix.Length..], out profileId);
    }
}
