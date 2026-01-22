using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    // Server Settings
    [ObservableProperty]
    private string _webServerUrl = string.Empty;

    // Download Settings
    [ObservableProperty]
    private string _ytdlPath = string.Empty;

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
    public int[] ResolutionOptions { get; } = [360, 720, 1080, 1440, 2160];

    [ObservableProperty]
    private int _cacheYouTubeMaxLength;

    [ObservableProperty]
    private float _cacheMaxSizeInGb;

    [ObservableProperty]
    private bool _cachePyPyDance;

    [ObservableProperty]
    private bool _cacheVRDancing;

    // Patching
    [ObservableProperty]
    private bool _patchResonite;

    [ObservableProperty]
    private bool _patchVRC;

    // Updates
    [ObservableProperty]
    private bool _autoUpdate;

    // VRCX Auto-start (Windows only, immediate effect - not saved to config)
    [ObservableProperty]
    private bool _vrcxAutoStart;

    [ObservableProperty]
    private bool _isVrcxInstalled;

    // Advanced Settings
    [ObservableProperty]
    private string _ytdlArgsOverride = string.Empty;

    [ObservableProperty]
    private string _avproOverride = "default";

    // AvproOverride options for dropdown
    public string[] AvproOverrideOptions { get; } = ["default", "true", "false"];

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

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasChanges;

    public SettingsViewModel()
    {
        LoadFromConfig();

        // Subscribe to cookie updates to refresh status after initialization
        Program.OnCookiesUpdated += () => CookieStatus = Program.GetCookieStatus();
    }

    private void LoadFromConfig()
    {
        var config = ConfigManager.Config;

        WebServerUrl = config.ytdlWebServerURL;
        YtdlPath = config.ytdlPath;
        YtdlUseCookies = config.ytdlUseCookies;
        CookieStatus = Program.GetCookieStatus();
        YtdlAutoUpdate = config.ytdlAutoUpdate;
        YtdlAdditionalArgs = config.ytdlAdditionalArgs;
        YtdlDubLanguage = config.ytdlDubLanguage;
        CachedAssetPath = config.CachedAssetPath;
        CacheYouTube = config.CacheYouTube;
        CacheYouTubeMaxResolution = config.CacheYouTubeMaxResolution;
        CacheYouTubeMaxLength = config.CacheYouTubeMaxLength;
        CacheMaxSizeInGb = config.CacheMaxSizeInGb;
        CachePyPyDance = config.CachePyPyDance;
        CacheVRDancing = config.CacheVRDancing;
        PatchResonite = config.PatchResonite;
        PatchVRC = config.PatchVRC;
        AutoUpdate = config.AutoUpdate;

        // VRCX Auto-start (Windows only)
        if (OperatingSystem.IsWindows())
        {
            IsVrcxInstalled = AutoStartShortcut.IsVrcxInstalled();
            VrcxAutoStart = AutoStartShortcut.IsStartupEnabled();
        }

        // Advanced Settings
        YtdlArgsOverride = config.ytdlArgsOverride;
        AvproOverride = config.avproOverride;

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

        HasChanges = false;
        StatusMessage = string.Empty;
    }

    partial void OnWebServerUrlChanged(string value) => HasChanges = true;
    partial void OnYtdlPathChanged(string value) => HasChanges = true;
    partial void OnYtdlUseCookiesChanged(bool value) => HasChanges = true;
    partial void OnYtdlAutoUpdateChanged(bool value) => HasChanges = true;
    partial void OnYtdlAdditionalArgsChanged(string value) => HasChanges = true;
    partial void OnYtdlDubLanguageChanged(string value) => HasChanges = true;
    partial void OnCachedAssetPathChanged(string value) => HasChanges = true;
    partial void OnCacheYouTubeChanged(bool value) => HasChanges = true;
    partial void OnCacheYouTubeMaxResolutionChanged(int value) => HasChanges = true;
    partial void OnCacheYouTubeMaxLengthChanged(int value) => HasChanges = true;
    partial void OnCacheMaxSizeInGbChanged(float value) => HasChanges = true;
    partial void OnCachePyPyDanceChanged(bool value) => HasChanges = true;
    partial void OnCacheVRDancingChanged(bool value) => HasChanges = true;
    partial void OnPatchResoniteChanged(bool value) => HasChanges = true;
    partial void OnPatchVRCChanged(bool value) => HasChanges = true;
    partial void OnAutoUpdateChanged(bool value) => HasChanges = true;
    partial void OnYtdlArgsOverrideChanged(string value) => HasChanges = true;
    partial void OnAvproOverrideChanged(string value) => HasChanges = true;
    partial void OnCacheCustomDomainsEnabledChanged(bool value) => HasChanges = true;
    partial void OnClearYouTubeCacheOnExitChanged(bool value) => HasChanges = true;
    partial void OnClearPyPyDanceCacheOnExitChanged(bool value) => HasChanges = true;
    partial void OnClearVRDancingCacheOnExitChanged(bool value) => HasChanges = true;

    partial void OnVrcxAutoStartChanged(bool value) => HasChanges = true;

    [RelayCommand]
    private void SaveSettings()
    {
        var config = ConfigManager.Config;

        config.ytdlWebServerURL = WebServerUrl;
        config.ytdlPath = YtdlPath;
        config.ytdlUseCookies = YtdlUseCookies;
        config.ytdlAutoUpdate = YtdlAutoUpdate;
        config.ytdlAdditionalArgs = YtdlAdditionalArgs;
        config.ytdlDubLanguage = YtdlDubLanguage;
        config.CachedAssetPath = CachedAssetPath;
        config.CacheYouTube = CacheYouTube;
        config.CacheYouTubeMaxResolution = CacheYouTubeMaxResolution;
        config.CacheYouTubeMaxLength = CacheYouTubeMaxLength;
        config.CacheMaxSizeInGb = CacheMaxSizeInGb;
        config.CachePyPyDance = CachePyPyDance;
        config.CacheVRDancing = CacheVRDancing;
        config.PatchResonite = PatchResonite;
        config.PatchVRC = PatchVRC;
        config.AutoUpdate = AutoUpdate;

        // Advanced Settings
        config.ytdlArgsOverride = YtdlArgsOverride;
        config.avproOverride = AvproOverride;

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
        }

        HasChanges = false;
        StatusMessage = "Settings saved!";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        LoadFromConfig();
        StatusMessage = "Settings reset to last saved values.";
    }

    [RelayCommand]
    private void AddBlockedUrl()
    {
        BlockedUrls.Add(new EditableString("https://"));
        HasChanges = true;
    }

    [RelayCommand]
    private void RemoveBlockedUrl(EditableString url)
    {
        BlockedUrls.Remove(url);
        HasChanges = true;
    }

    [RelayCommand]
    private void AddCacheCustomDomain()
    {
        CacheCustomDomains.Add(new EditableString("example.com"));
        HasChanges = true;
    }

    [RelayCommand]
    private void RemoveCacheCustomDomain(EditableString domain)
    {
        CacheCustomDomains.Remove(domain);
        HasChanges = true;
    }

    [RelayCommand]
    private void AddClearCustomDomainOnExit()
    {
        ClearCustomDomainsOnExit.Add(new EditableString("example.com"));
        HasChanges = true;
    }

    [RelayCommand]
    private void RemoveClearCustomDomainOnExit(EditableString domain)
    {
        ClearCustomDomainsOnExit.Remove(domain);
        HasChanges = true;
    }

    [RelayCommand]
    private void AddPreCacheUrl()
    {
        PreCacheUrls.Add(new EditableString("https://"));
        HasChanges = true;
    }

    [RelayCommand]
    private void RemovePreCacheUrl(EditableString url)
    {
        PreCacheUrls.Remove(url);
        HasChanges = true;
    }
}
