using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.YTDL;

// ReSharper disable FieldCanBeMadeReadOnly.Global

namespace VRCVideoCacher;

public class ConfigManager
{
    public static readonly ConfigModel Config;
    private static readonly ILogger Log = Program.Logger.ForContext<ConfigManager>();
    private static readonly string ConfigFilePath;
    public static readonly string UtilsPath;

    // Events for UI
    public static event Action? OnConfigChanged;

    static ConfigManager()
    {
        Log.Information("Loading config...");
        ConfigFilePath = Path.Combine(Program.DataPath, "Config.json");
        Log.Debug("Using config file path: {ConfigFilePath}", ConfigFilePath);

        if (!File.Exists(ConfigFilePath))
        {
            Config = new ConfigModel();
            FirstRun();
        }
        else
        {
            try
            {
                Config = JsonConvert.DeserializeObject<ConfigModel>(File.ReadAllText(ConfigFilePath)) ??
                         new ConfigModel();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load config, creating new one...");
                Config = new ConfigModel();
                FirstRun();
            }
        }
        if (Config.YtdlpWebServerURL.EndsWith('/'))
            Config.YtdlpWebServerURL = Config.YtdlpWebServerURL.TrimEnd('/');

        UtilsPath = Path.GetDirectoryName(Config.YtdlpPath) ?? string.Empty;
        if (!UtilsPath.EndsWith("Utils"))
            UtilsPath = Path.Combine(UtilsPath, "Utils");

        if (!Path.IsPathRooted(UtilsPath))
            UtilsPath = Path.Combine(Program.DataPath, UtilsPath);

        Directory.CreateDirectory(UtilsPath);

        Log.Information("Loaded config.");
        TrySaveConfig();
    }

    public static void TrySaveConfig()
    {
        var newConfig = JsonConvert.SerializeObject(Config, Formatting.Indented);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;

        Log.Information("Config changed, saving...");
        File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(Config, Formatting.Indented));
        Log.Information("Config saved.");
        OnConfigChanged?.Invoke();
        CacheManager.TryFlushCache();
    }

    private static bool GetUserConfirmation(string prompt, bool defaultValue)
    {
        var defaultOption = defaultValue ? "Y/n" : "y/N";
        var message = $"{prompt} ({defaultOption}):";
        message = message.TrimStart();
        Log.Information(message);
        var input = Console.ReadLine();
        return string.IsNullOrEmpty(input) ? defaultValue : input.Equals("y", StringComparison.CurrentCultureIgnoreCase);
    }

    private static void FirstRun()
    {
        Log.Information("It appears this is your first time running VRCVideoCacher. Let's create a basic config file.");

        var autoSetup = GetUserConfirmation("Would you like to use VRCVideoCacher for only fixing YouTube videos?", true);
        if (autoSetup)
        {
            Log.Information("Basic config created. You can modify it later in the Config.json file.");
        }
        else
        {
            Config.CacheYouTube = GetUserConfirmation("Would you like to cache/download Youtube videos?", true);
            if (Config.CacheYouTube)
            {
                var maxResolution = GetUserConfirmation("Would you like to cache/download Youtube videos in 4k?", true);
                Config.CacheYouTubeMaxResolution = maxResolution ? 2160 : 1080;
            }

            var vrDancingPyPyChoice = GetUserConfirmation("Would you like to cache/download VRDancing & PyPyDance videos?", true);
            Config.CacheVRDancing = vrDancingPyPyChoice;
            Config.CachePyPyDance = vrDancingPyPyChoice;

            if (GetUserConfirmation("Would you like to cache/download music/videos from custom domains?", false))
            {
                Log.Information("Custom domains can be configured in Config.json under 'CacheCustomDomains'.");
                Log.Information("Example: \"CacheCustomDomains\": [\"cdn.example.com\", \"media.yourdomain.com\"]");
            }

            Log.Information("Would you like to use the companion extension to fetch youtube cookies? (This will fix bot errors, requires installation of the extension)");
            Log.Information("Extension can be found here: https://github.com/clienthax/VRCVideoCacherBrowserExtension");
            Config.YtdlpUseCookies = GetUserConfirmation("", true);

            Config.PatchResonite = GetUserConfirmation("Would you like to enable Resonite support?", false);
        }

        if (OperatingSystem.IsWindows() && GetUserConfirmation("Would you like to add VRCVideoCacher to VRCX auto start?", true))
        {
            AutoStartShortcut.CreateShortcut();
        }

        if (YtdlManager.GlobalYtdlConfigExists() && GetUserConfirmation(@"Would you like to delete global YT-DLP config in %AppData%\yt-dlp\config? (this is necessary for VRCVideoCacher to function)", true))
        {
            YtdlManager.DeleteGlobalYtdlConfig();
        }

        if (GetUserConfirmation("Would you like to cache custom domains? (You can configure domains later in Config.json)", false))
        {
            Log.Information("You can add custom domains to 'CacheCustomDomains' in Config.json later.");
        }

        Log.Information("You'll need to install our companion extension to fetch youtube cookies (This will fix YouTube bot errors)");
        Log.Information("Chrome: https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge");
        Log.Information("Firefox: https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter/");
        Log.Information("More info: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

        if (GetUserConfirmation("Have you installed the cookies browser extension?", false))
        {
            Config.CookieSetupCompleted = true;
        }

        TrySaveConfig();
    }
}

// ReSharper disable InconsistentNaming
public class ConfigModel
{
    // yt-dlp
    public string YtdlpWebServerURL = "http://localhost:9696";
    public string YtdlpPath = OperatingSystem.IsWindows() ? "Utils\\yt-dlp.exe" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCVideoCacher/Utils/yt-dlp");
    public bool YtdlpUseCookies = true;
    public bool YtdlpAutoUpdate = true;
    public string YtdlpAdditionalArgs = string.Empty;
    public string YtdlpArgsOverride = string.Empty;
    public string YtdlpDubLanguage = string.Empty;

    // Caching
    public string CachedAssetPath = "";
    public float CacheMaxSizeInGb = 10f;
    public bool CacheYouTube = false;
    public int CacheYouTubeMaxResolution = 1080;
    public int CacheYouTubeMaxLength = 120;
    public bool CachePyPyDance = false;
    public bool CacheVRDancing = false;
    public bool CacheOnly = false;
    public int MaxConcurrentDownloads = 2;
    public bool CacheCustomDomainsEnabled = false;
    public string[] CacheCustomDomains = [];

    // Eviction protection - skip these categories during cache flush
    public bool EvictionProtectYouTube = false;
    public bool EvictionProtectPyPyDance = false;
    public bool EvictionProtectVRDancing = false;
    public bool EvictionProtectCustomDomains = false;

    // Clear cache on exit
    public bool ClearYouTubeCacheOnExit = false;
    public bool ClearPyPyDanceCacheOnExit = false;
    public bool ClearVRDancingCacheOnExit = false;
    public string[] ClearCustomDomainsOnExit = [];

    // Cache Rules
    public string[] BlockedUrls = ["https://na2.vrdancing.club/sampleurl.mp4"];
    public string BlockRedirect = "https://www.youtube.com/watch?v=byv2bKekeWQ";
    public string[] PreCacheUrls = [];

    // Patching
    public bool PatchResonite = false;
    public string ResonitePath = "";
    public bool PatchVrChat = true;

    // Video Cacher
    public bool AutoUpdateVrcVideoCacher = true;
    public bool CookieSetupCompleted = false;
}
// ReSharper restore InconsistentNaming