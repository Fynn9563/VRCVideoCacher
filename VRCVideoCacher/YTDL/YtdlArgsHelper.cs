namespace VRCVideoCacher.YTDL;

public static class YtdlArgsHelper
{
    /// <summary>
    /// Determines if AVPro should be used based on the config override and original request.
    /// </summary>
    /// <param name="requestedAvPro">The AVPro value requested by the caller.</param>
    /// <returns>The effective AVPro value after applying config override.</returns>
    public static bool GetEffectiveAvPro(bool requestedAvPro)
    {
        // If avproOverride is enabled, always use AVPro
        if (ConfigManager.Config.avproOverride)
            return true;

        return requestedAvPro;
    }

    /// <summary>
    /// Gets the additional yt-dlp arguments, preferring override if set.
    /// </summary>
    /// <returns>The effective additional arguments string.</returns>
    public static string GetEffectiveAdditionalArgs()
    {
        // If ytdlArgsOverride is set, use it instead of ytdlAdditionalArgs
        if (!string.IsNullOrEmpty(ConfigManager.Config.ytdlArgsOverride))
            return ConfigManager.Config.ytdlArgsOverride;

        return ConfigManager.Config.ytdlAdditionalArgs;
    }
}
