namespace VRCVideoCacher;

public static class YtdlArgsHelper
{
    public static string GetYtdlArgs()
    {
        if (!string.IsNullOrEmpty(ConfigManager.Config.ytdlArgsOverride))
            return ConfigManager.Config.ytdlArgsOverride;

        return ConfigManager.Config.ytdlAdditionalArgs;
    }

    public static bool ApplyAvproOverride(bool originalAvPro, out bool wasOverriddenToFalse)
    {
        wasOverriddenToFalse = false;

        switch (ConfigManager.Config.avproOverride.ToLower())
        {
            case "true":
                return true;
            case "false":
                wasOverriddenToFalse = true;
                return false;
            case "default":
            default:
                return originalAvPro;
        }
    }
}
