namespace VRCVideoCacher;

public static class YtdlArgsHelper
{
    public static string GetYtdlArgs()
    {
        if (!string.IsNullOrEmpty(ConfigManager.Config.YtdlpArgsOverride))
            return ConfigManager.Config.YtdlpArgsOverride;

        return ConfigManager.Config.YtdlpAdditionalArgs;
    }
}
