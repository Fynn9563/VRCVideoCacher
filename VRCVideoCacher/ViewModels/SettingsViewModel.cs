using System.Collections.ObjectModel;
using System.Globalization;
using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.API;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.ViewModels;

public record LanguageOption(string Code, string DisplayName);

public partial class SettingsViewModel : ViewModelBase
{
    // Server Settings
    [ObservableProperty]
    private string _webServerUrl = string.Empty;

    // Download Settings
    [ObservableProperty]
    private bool _ytdlpGlobalPath;

    [ObservableProperty]
    private bool _ytdlUseCookies;

    [ObservableProperty]
    private string _cookieStatus = string.Empty;

    [ObservableProperty]
    private bool _ytdlAutoUpdate;

    [ObservableProperty]
    private string _ytdlAdditionalArgs = string.Empty;

    [ObservableProperty]
    private string _ytdlDubLanguage = string.Empty;

    // Cache Settings
    [ObservableProperty]
    private string _cachedAssetPath = string.Empty;

    [ObservableProperty]
    private bool _cacheYouTube;

    [ObservableProperty]
    private int _cacheYouTubeMaxResolution;

    // Resolution options for the dropdown
    public int[] ResolutionOptions { get; } = [720, 1080, 1440, 2160];

    [ObservableProperty]
    private int _cacheYouTubeMaxLength;

    [ObservableProperty]
    private float _cacheMaxSizeInGb;

    [ObservableProperty]
    private bool _cachePyPyDance;

    [ObservableProperty]
    private bool _cacheVRDancing;

    [ObservableProperty]
    private bool _cacheOnly;

    [ObservableProperty]
    private int _maxConcurrentDownloads;

    // Eviction Protection
    [ObservableProperty]
    private bool _evictionProtectYouTube;

    [ObservableProperty]
    private bool _evictionProtectPyPyDance;

    [ObservableProperty]
    private bool _evictionProtectVRDancing;

    [ObservableProperty]
    private bool _evictionProtectCustomDomains;

    // Patching
    [ObservableProperty]
    private bool _patchResonite;

    [ObservableProperty]
    private bool _patchVRC;

    // Updates
    [ObservableProperty]
    private bool _autoUpdate;

    [ObservableProperty]
    private bool _closeToTray;

    // VRCX Auto-start (Windows only, immediate effect - not saved to config)
    [ObservableProperty]
    private bool _vrcxAutoStart;

    [ObservableProperty]
    private bool _isVrcxInstalled;

    // SteamVR Auto-start (Windows only, immediate effect - not saved to config)
    [ObservableProperty]
    private bool _steamVrAutoStart;

    [ObservableProperty]
    private bool _isSteamVrInstalled;

    // Advanced Settings
    [ObservableProperty]
    private string _ytdlArgsOverride = string.Empty;

    // Custom Domains
    [ObservableProperty]
    private bool _cacheCustomDomainsEnabled;

    public ObservableCollection<EditableString> CacheCustomDomains { get; } = [];

    // Clear Cache on Exit
    [ObservableProperty]
    private bool _clearYouTubeCacheOnExit;

    [ObservableProperty]
    private bool _clearPyPyDanceCacheOnExit;

    [ObservableProperty]
    private bool _clearVRDancingCacheOnExit;

    public ObservableCollection<EditableString> ClearCustomDomainsOnExit { get; } = [];

    // Blocked URLs
    public ObservableCollection<EditableString> BlockedUrls { get; } = [];

    // Pre-Cache URLs
    public ObservableCollection<EditableString> PreCacheUrls { get; } = [];

    [ObservableProperty]
    private string _blockRedirect = string.Empty;

    // Language
    public List<LanguageOption> AvailableLanguageOptions { get; } = [];

    [ObservableProperty]
    private LanguageOption? _selectedLanguageOption;

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasChanges;

    private bool _isLoading;
    private bool _savedVrcxAutoStart;
    private bool _savedSteamVrAutoStart;

    public SettingsViewModel()
    {
        foreach (var lang in Loc.Instance.AvailableLanguages)
            AvailableLanguageOptions.Add(new LanguageOption(lang, GetLanguageDisplayName(lang)));

        ConfigManager.OnConfigChanged += LoadFromConfig;
        LoadFromConfig();

        // Subscribe to cookie updates to refresh status after initialization
        Program.OnCookiesUpdated += () => CookieStatus = Program.GetCookieStatus();
    }

    private static string GetLanguageDisplayName(string code)
    {
        try
        {
            var culture = new CultureInfo(code);
            return $"{culture.NativeName} ({code})";
        }
        catch { return code; }
    }

    private void LoadFromConfig()
    {
        _isLoading = true;
        var config = ConfigManager.Config;

        WebServerUrl = config.YtdlpWebServerUrl;
        YtdlpGlobalPath = config.YtdlpGlobalPath;
        YtdlUseCookies = config.YtdlpUseCookies;
        CookieStatus = Program.GetCookieStatus();
        YtdlAutoUpdate = config.YtdlpAutoUpdate;
        YtdlAdditionalArgs = config.YtdlpAdditionalArgs;
        YtdlDubLanguage = config.YtdlpDubLanguage;
        CachedAssetPath = config.CachedAssetPath;
        CacheYouTube = config.CacheYouTube;
        CacheYouTubeMaxResolution = config.CacheYouTubeMaxResolution;
        CacheYouTubeMaxLength = config.CacheYouTubeMaxLength;
        CacheMaxSizeInGb = config.CacheMaxSizeInGb;
        CachePyPyDance = config.CachePyPyDance;
        CacheVRDancing = config.CacheVRDancing;
        CacheOnly = config.CacheOnly;
        MaxConcurrentDownloads = config.MaxConcurrentDownloads;
        EvictionProtectYouTube = config.EvictionProtectYouTube;
        EvictionProtectPyPyDance = config.EvictionProtectPyPyDance;
        EvictionProtectVRDancing = config.EvictionProtectVRDancing;
        EvictionProtectCustomDomains = config.EvictionProtectCustomDomains;
        PatchResonite = config.PatchResonite;
        PatchVRC = config.PatchVrChat;
        AutoUpdate = config.AutoUpdateVrcVideoCacher;
        CloseToTray = config.CloseToTray;

        // VRCX Auto-start (Windows only)
        if (OperatingSystem.IsWindows())
        {
            IsVrcxInstalled = AutoStartShortcut.IsVrcxInstalled();
            VrcxAutoStart = AutoStartShortcut.IsStartupEnabled();
            _savedVrcxAutoStart = VrcxAutoStart;
        }

        // SteamVR Auto-start (Windows only)
        if (OperatingSystem.IsWindows())
        {
            IsSteamVrInstalled = SteamVrStartup.IsSteamVrInstalled();
            SteamVrAutoStart = SteamVrStartup.IsAutoStartEnabled();
            _savedSteamVrAutoStart = SteamVrAutoStart;
        }

        // Advanced Settings
        YtdlArgsOverride = config.YtdlpArgsOverride;

        // Clear Cache on Exit
        ClearYouTubeCacheOnExit = config.ClearYouTubeCacheOnExit;
        ClearPyPyDanceCacheOnExit = config.ClearPyPyDanceCacheOnExit;
        ClearVRDancingCacheOnExit = config.ClearVRDancingCacheOnExit;

        // Custom Domains
        CacheCustomDomainsEnabled = config.CacheCustomDomainsEnabled;
        CacheCustomDomains.Clear();
        foreach (var domain in config.CacheCustomDomains)
        {
            CacheCustomDomains.Add(new EditableString(domain));
        }

        ClearCustomDomainsOnExit.Clear();
        foreach (var domain in config.ClearCustomDomainsOnExit)
        {
            ClearCustomDomainsOnExit.Add(new EditableString(domain));
        }

        BlockedUrls.Clear();
        foreach (var url in config.BlockedUrls)
        {
            BlockedUrls.Add(new EditableString(url));
        }

        PreCacheUrls.Clear();
        foreach (var url in config.PreCacheUrls)
        {
            PreCacheUrls.Add(new EditableString(url));
        }
        BlockRedirect = config.BlockRedirect;

        SelectedLanguageOption = AvailableLanguageOptions.FirstOrDefault(l => l.Code == config.Language)
                                 ?? AvailableLanguageOptions.FirstOrDefault();

        HasChanges = false;
        StatusMessage = string.Empty;
        _isLoading = false;
    }

    private void CheckForChanges()
    {
        if (_isLoading) return;

        var config = ConfigManager.Config;
        var changed =
            WebServerUrl != config.YtdlpWebServerUrl ||
            YtdlpGlobalPath != config.YtdlpGlobalPath ||
            YtdlUseCookies != config.YtdlpUseCookies ||
            YtdlAutoUpdate != config.YtdlpAutoUpdate ||
            YtdlAdditionalArgs != config.YtdlpAdditionalArgs ||
            YtdlDubLanguage != config.YtdlpDubLanguage ||
            CachedAssetPath != config.CachedAssetPath ||
            CacheYouTube != config.CacheYouTube ||
            CacheYouTubeMaxResolution != config.CacheYouTubeMaxResolution ||
            CacheYouTubeMaxLength != config.CacheYouTubeMaxLength ||
            Math.Abs(CacheMaxSizeInGb - config.CacheMaxSizeInGb) > 0.001f ||
            CachePyPyDance != config.CachePyPyDance ||
            CacheVRDancing != config.CacheVRDancing ||
            CacheOnly != config.CacheOnly ||
            MaxConcurrentDownloads != config.MaxConcurrentDownloads ||
            EvictionProtectYouTube != config.EvictionProtectYouTube ||
            EvictionProtectPyPyDance != config.EvictionProtectPyPyDance ||
            EvictionProtectVRDancing != config.EvictionProtectVRDancing ||
            EvictionProtectCustomDomains != config.EvictionProtectCustomDomains ||
            PatchResonite != config.PatchResonite ||
            PatchVRC != config.PatchVrChat ||
            AutoUpdate != config.AutoUpdateVrcVideoCacher ||
            CloseToTray != config.CloseToTray ||
            YtdlArgsOverride != config.YtdlpArgsOverride ||
            CacheCustomDomainsEnabled != config.CacheCustomDomainsEnabled ||
            ClearYouTubeCacheOnExit != config.ClearYouTubeCacheOnExit ||
            ClearPyPyDanceCacheOnExit != config.ClearPyPyDanceCacheOnExit ||
            ClearVRDancingCacheOnExit != config.ClearVRDancingCacheOnExit ||
            BlockRedirect != config.BlockRedirect ||
            VrcxAutoStart != _savedVrcxAutoStart ||
            SteamVrAutoStart != _savedSteamVrAutoStart ||
            !CacheCustomDomains.Select(x => x.Value).SequenceEqual(config.CacheCustomDomains) ||
            !ClearCustomDomainsOnExit.Select(x => x.Value).SequenceEqual(config.ClearCustomDomainsOnExit) ||
            !BlockedUrls.Select(x => x.Value).SequenceEqual(config.BlockedUrls) ||
            !PreCacheUrls.Select(x => x.Value).SequenceEqual(config.PreCacheUrls);

        HasChanges = changed;
        StatusMessage = changed ? Loc.Tr("SettingsUnsavedChanges") : string.Empty;
    }

    partial void OnWebServerUrlChanged(string value) => CheckForChanges();
    partial void OnYtdlpGlobalPathChanged(bool value) => CheckForChanges();
    partial void OnYtdlUseCookiesChanged(bool value) => CheckForChanges();
    partial void OnYtdlAutoUpdateChanged(bool value) => CheckForChanges();
    partial void OnYtdlAdditionalArgsChanged(string value) => CheckForChanges();
    partial void OnYtdlDubLanguageChanged(string value) => CheckForChanges();
    partial void OnCachedAssetPathChanged(string value) => CheckForChanges();
    partial void OnCacheYouTubeChanged(bool value) => CheckForChanges();
    partial void OnCacheYouTubeMaxResolutionChanged(int value) => CheckForChanges();
    partial void OnCacheYouTubeMaxLengthChanged(int value) => CheckForChanges();
    partial void OnCacheMaxSizeInGbChanged(float value) => CheckForChanges();
    partial void OnCachePyPyDanceChanged(bool value) => CheckForChanges();
    partial void OnCacheVRDancingChanged(bool value) => CheckForChanges();
    partial void OnCacheOnlyChanged(bool value) => CheckForChanges();
    partial void OnMaxConcurrentDownloadsChanged(int value) => CheckForChanges();
    partial void OnPatchResoniteChanged(bool value) => CheckForChanges();
    partial void OnPatchVRCChanged(bool value) => CheckForChanges();
    partial void OnAutoUpdateChanged(bool value) => CheckForChanges();
    partial void OnCloseToTrayChanged(bool value) => CheckForChanges();
    partial void OnYtdlArgsOverrideChanged(string value) => CheckForChanges();
    partial void OnCacheCustomDomainsEnabledChanged(bool value) => CheckForChanges();
    partial void OnEvictionProtectYouTubeChanged(bool value) => CheckForChanges();
    partial void OnEvictionProtectPyPyDanceChanged(bool value) => CheckForChanges();
    partial void OnEvictionProtectVRDancingChanged(bool value) => CheckForChanges();
    partial void OnEvictionProtectCustomDomainsChanged(bool value) => CheckForChanges();
    partial void OnClearYouTubeCacheOnExitChanged(bool value) => CheckForChanges();
    partial void OnClearPyPyDanceCacheOnExitChanged(bool value) => CheckForChanges();
    partial void OnClearVRDancingCacheOnExitChanged(bool value) => CheckForChanges();
    partial void OnBlockRedirectChanged(string value) => CheckForChanges();
    partial void OnVrcxAutoStartChanged(bool value) => CheckForChanges();
    partial void OnSteamVrAutoStartChanged(bool value) => CheckForChanges();

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value == null) return;
        Loc.Instance.CurrentLanguage = value.Code;
        ConfigManager.Config.Language = value.Code;
        ConfigManager.TrySaveConfig();
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var config = ConfigManager.Config;

        if (config.YtdlpWebServerUrl != WebServerUrl)
        {
            config.YtdlpWebServerUrl = WebServerUrl;
            WebServer.Init();
        }

        config.YtdlpGlobalPath = YtdlpGlobalPath;
        config.YtdlpUseCookies = YtdlUseCookies;
        config.YtdlpAutoUpdate = YtdlAutoUpdate;
        config.YtdlpAdditionalArgs = YtdlAdditionalArgs;
        config.YtdlpDubLanguage = YtdlDubLanguage;
        config.CachedAssetPath = CachedAssetPath;
        config.CacheYouTube = CacheYouTube;
        config.CacheYouTubeMaxResolution = CacheYouTubeMaxResolution;
        config.CacheYouTubeMaxLength = CacheYouTubeMaxLength;
        config.CacheMaxSizeInGb = CacheMaxSizeInGb;
        config.CachePyPyDance = CachePyPyDance;
        config.CacheVRDancing = CacheVRDancing;
        config.MaxConcurrentDownloads = MaxConcurrentDownloads;
        config.EvictionProtectYouTube = EvictionProtectYouTube;
        config.EvictionProtectPyPyDance = EvictionProtectPyPyDance;
        config.EvictionProtectVRDancing = EvictionProtectVRDancing;
        config.EvictionProtectCustomDomains = EvictionProtectCustomDomains;
        config.PatchResonite = PatchResonite;
        config.PatchVrChat = PatchVRC;
        config.AutoUpdateVrcVideoCacher = AutoUpdate;
        config.CloseToTray = CloseToTray;

        // Advanced Settings
        config.YtdlpArgsOverride = YtdlArgsOverride;

        // Clear Cache on Exit
        config.ClearYouTubeCacheOnExit = ClearYouTubeCacheOnExit;
        config.ClearPyPyDanceCacheOnExit = ClearPyPyDanceCacheOnExit;
        config.ClearVRDancingCacheOnExit = ClearVRDancingCacheOnExit;

        // Custom Domains
        config.CacheCustomDomainsEnabled = CacheCustomDomainsEnabled;
        config.CacheCustomDomains = CacheCustomDomains.Select(x => x.Value).ToArray();
        config.ClearCustomDomainsOnExit = ClearCustomDomainsOnExit.Select(x => x.Value).ToArray();

        config.BlockedUrls = BlockedUrls.Select(x => x.Value).ToArray();
        config.BlockRedirect = BlockRedirect;
        config.PreCacheUrls = PreCacheUrls.Select(x => x.Value).ToArray();

        ConfigManager.TrySaveConfig();

        // Apply VRCX auto-start setting (Windows only)
        if (OperatingSystem.IsWindows())
        {
            if (VrcxAutoStart)
                AutoStartShortcut.CreateShortcut();
            else
                AutoStartShortcut.RemoveShortcut();
            _savedVrcxAutoStart = VrcxAutoStart;
        }

        // Apply SteamVR auto-start setting (Windows only)
        if (OperatingSystem.IsWindows())
        {
            if (SteamVrAutoStart)
                SteamVrStartup.Enable();
            else
                SteamVrStartup.Disable();
            _savedSteamVrAutoStart = SteamVrAutoStart;
        }

        HasChanges = false;
        StatusMessage = Loc.Tr("SettingsSaved");
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        LoadFromConfig();
        StatusMessage = Loc.Tr("SettingsReset");
    }

    [RelayCommand]
    private void AddBlockedUrl()
    {
        BlockedUrls.Add(new EditableString("https://"));
        CheckForChanges();
    }

    [RelayCommand]
    private void RemoveBlockedUrl(EditableString url)
    {
        BlockedUrls.Remove(url);
        CheckForChanges();
    }

    [RelayCommand]
    private void AddCacheCustomDomain()
    {
        CacheCustomDomains.Add(new EditableString("example.com"));
        CheckForChanges();
    }

    [RelayCommand]
    private void RemoveCacheCustomDomain(EditableString domain)
    {
        CacheCustomDomains.Remove(domain);
        CheckForChanges();
    }

    [RelayCommand]
    private void AddClearCustomDomainOnExit()
    {
        ClearCustomDomainsOnExit.Add(new EditableString("example.com"));
        CheckForChanges();
    }

    [RelayCommand]
    private void RemoveClearCustomDomainOnExit(EditableString domain)
    {
        ClearCustomDomainsOnExit.Remove(domain);
        CheckForChanges();
    }

    [RelayCommand]
    private void AddPreCacheUrl()
    {
        PreCacheUrls.Add(new EditableString("https://"));
        CheckForChanges();
    }

    [RelayCommand]
    private void RemovePreCacheUrl(EditableString url)
    {
        PreCacheUrls.Remove(url);
        CheckForChanges();
    }
}
