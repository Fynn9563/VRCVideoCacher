using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Elevator;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.Views;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class MainWindowViewModel;

public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _serverRunning = true;

    [ObservableProperty]
    private string _serverUrl = "http://localhost:9696";

    [ObservableProperty]
    private long _totalCacheSize;

    [ObservableProperty]
    private float _maxCacheSize;

    [ObservableProperty]
    private int _cachedVideoCount;

    [ObservableProperty]
    private int _downloadQueueCount;

    [ObservableProperty]
    private string _cookieStatus = Loc.Tr("NotSet");

    [ObservableProperty]
    private string _currentDownloadText = Loc.Tr("None");

    // Per-category cache sizes
    [ObservableProperty]
    private long _youTubeCacheSize;

    [ObservableProperty]
    private long _pyPyDanceCacheSize;

    [ObservableProperty]
    private long _vrDancingCacheSize;

    [ObservableProperty]
    private long _customDomainsCacheSize;

    // Visibility flags (only show enabled categories)
    [ObservableProperty]
    private bool _showYouTubeSize;

    [ObservableProperty]
    private bool _showPyPyDanceSize;

    [ObservableProperty]
    private bool _showVRDancingSize;

    [ObservableProperty]
    private bool _showCustomDomainsSize;

    [ObservableProperty]
    private bool? _hostState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMotd))]
    private string? _motd;

    public bool HasMotd => !string.IsNullOrWhiteSpace(Motd);

    public DashboardViewModel()
    {
        ServerUrl = ConfigManager.Config.YtdlpWebServerUrl;
        MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;
        HostState = ElevatorManager.HasHostsLine;

        // Initial data load
        RefreshData();

        Motd = VvcConfigService.CurrentConfig.motd;

        // Subscribe to events
        CacheManager.OnCacheChanged += OnCacheChanged;
        VideoDownloader.OnDownloadStarted += OnDownloadStarted;
        VideoDownloader.OnDownloadCompleted += OnDownloadCompleted;
        VideoDownloader.OnQueueChanged += OnQueueChanged;
        ConfigManager.OnConfigChanged += OnConfigChanged;
        Program.OnCookiesUpdated += OnCookiesUpdated;
        ElevatorManager.OnHostStateChanged += OnElevatorHostStateChanged;
    }

    private void OnCookiesUpdated()
    {
        _ = ValidateCookiesAsync();
    }

    private void OnCacheChanged(string fileName, CacheChangeType changeType)
    {
        Dispatcher.UIThread.InvokeAsync(RefreshCacheStats);
    }

    private void OnDownloadStarted(Models.VideoInfo video)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var active = VideoDownloader.GetActiveDownloads();
            CurrentDownloadText = active.Count <= 1
                ? $"{video.UrlType}: {video.VideoId}"
                : $"{active.Count} downloads active";
        });
    }

    private void OnDownloadCompleted(Models.VideoInfo video, bool success)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var active = VideoDownloader.GetActiveDownloads();
            CurrentDownloadText = active.Count > 0
                ? active.Count == 1
                    ? $"{active[0].UrlType}: {active[0].VideoId}"
                    : $"{active.Count} downloads active"
                : Loc.Tr("None");
        });
    }

    private void OnQueueChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadQueueCount = VideoDownloader.GetQueueCount();
        });
    }

    private void OnConfigChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            ServerUrl = ConfigManager.Config.YtdlpWebServerUrl;
            MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;
            RefreshCategoryVisibility();
        });
        _ = ValidateCookiesAsync();
    }

    private void OnElevatorHostStateChanged(bool? state)
    {
        Dispatcher.UIThread.InvokeAsync(() => HostState = state);
    }

    [RelayCommand]
    private async Task ToggleHost()
    {
        await Task.Run(() => ElevatorManager.ToggleHostLine());
    }

    [RelayCommand]
    private void RefreshData()
    {
        RefreshCacheStats();
        DownloadQueueCount = VideoDownloader.GetQueueCount();

        var activeDownloads = VideoDownloader.GetActiveDownloads();
        CurrentDownloadText = activeDownloads.Count > 0
            ? activeDownloads.Count == 1
                ? $"{activeDownloads[0].UrlType}: {activeDownloads[0].VideoId}"
                : $"{activeDownloads.Count} downloads active"
            : Loc.Tr("None");

        _ = ValidateCookiesAsync();
    }

    private void RefreshCacheStats()
    {
        TotalCacheSize = CacheManager.GetTotalCacheSize();
        // Subtract 1 for index.html if it exists in the cache
        var count = CacheManager.GetCachedVideoCount();
        var assets = CacheManager.GetCachedAssets();
        if (assets.ContainsKey("index.html"))
            count--;
        CachedVideoCount = count;

        var sizes = CacheManager.GetCategorySizes();
        YouTubeCacheSize = sizes["YouTube"];
        PyPyDanceCacheSize = sizes["PyPyDance"];
        VrDancingCacheSize = sizes["VRDancing"];
        CustomDomainsCacheSize = sizes["CustomDomains"];

        RefreshCategoryVisibility();
    }

    private void RefreshCategoryVisibility()
    {
        var config = ConfigManager.Config;
        ShowYouTubeSize = config.CacheYouTube;
        ShowPyPyDanceSize = config.CachePyPyDance;
        ShowVRDancingSize = config.CacheVRDancing;
        ShowCustomDomainsSize = config.CacheCustomDomainsEnabled;
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        var cachePath = CacheManager.CachePath;
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start("explorer.exe", cachePath);
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", cachePath);
        }
    }

    private async Task ValidateCookiesAsync()
    {
        if (!Program.IsCookiesEnabledAndValid())
        {
            Dispatcher.UIThread.Post(() => CookieStatus = Loc.Tr("NotSet"));
            return;
        }

        Dispatcher.UIThread.Post(() => CookieStatus = Loc.Tr("Checking"));

        var result = await Program.ValidateCookiesAsync();
        Dispatcher.UIThread.Post(() =>
        {
            CookieStatus = result switch
            {
                true => Loc.Tr("Valid"),
                false => Loc.Tr("Expired"),
                null => Loc.Tr("Unknown")
            };
        });
    }

    [RelayCommand]
    private async Task SetupCookieExtension()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new CookieSetupViewModel();
            var window = new CookieSetupWindow
            {
                DataContext = viewModel
            };

            viewModel.RequestClose += () => window.Close();

            await window.ShowDialog(desktop.MainWindow!);

            // Refresh cookies status after dialog closes
            _ = ValidateCookiesAsync();
        }
    }
}
