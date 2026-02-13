using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class CacheItemViewModel : ViewModelBase
{
    public string FileName { get; init; } = string.Empty;  // Relative path from cache root (e.g., "YouTube/abc123.mp4")
    public string VideoId { get; init; } = string.Empty;   // Just the video ID without path or extension
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public string Extension { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;  // Subdirectory category (YouTube, PyPyDance, etc.)

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _thumbnailSource = string.Empty;

    public string DisplayTitle => string.IsNullOrEmpty(Title) ? VideoId : Title;

    public string SizeFormatted => FormatSize(Size);

    public bool IsYouTube => Category == "YouTube";

    public bool IsAudioFile => Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                               Extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                               Extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
                               Extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
                               Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);

    // Shows music icon when it's an audio file without a thumbnail
    [ObservableProperty]
    private bool _showMusicIcon;

    // Event to notify parent when item is deleted
    public event Action<CacheItemViewModel>? OnDeleted;

    public async Task LoadMetadataAsync()
    {
        var filePath = Path.Combine(CacheManager.CachePath, FileName);

        // Load title from DB
        var videoInfo = await DatabaseManager.Database.VideoInfoCache.FindAsync(VideoId);
        if (VideoId.Length == 11 && string.IsNullOrEmpty(videoInfo?.Title))
            videoInfo = await YouTubeMetadataService.GetVideoTitleAsync(VideoId);

        if (!string.IsNullOrEmpty(videoInfo?.Title))
        {
            Title = videoInfo.Title;
            OnPropertyChanged(nameof(DisplayTitle));
        }

        // Detect audio-only files early (including .mp4 with no video stream)
        var isAudioOnly = File.Exists(filePath) && ThumbnailManager.IsAudioOnly(filePath);

        // Load thumbnail - try cached, then YouTube API, then extract from file
        var thumbnailPath = ThumbnailManager.GetThumbnail(VideoId);
        if (VideoId.Length == 11 && string.IsNullOrEmpty(thumbnailPath))
            thumbnailPath = await YouTubeMetadataService.GetThumbnail(VideoId);

        if (string.IsNullOrEmpty(thumbnailPath) && File.Exists(filePath))
            thumbnailPath = ThumbnailManager.TryExtractEmbeddedThumbnail(VideoId, filePath);

        // Shell thumbnails only for video files - audio-only files get the music icon instead
        if (string.IsNullOrEmpty(thumbnailPath) && File.Exists(filePath) && !isAudioOnly)
            thumbnailPath = ShellThumbnailExtractor.TryExtract(VideoId, filePath, ThumbnailManager.ThumbnailCacheDir);

        // For audio-only files, ignore shell-generated thumbnails (e.g. generic video player icons)
        if (isAudioOnly && !string.IsNullOrEmpty(thumbnailPath))
        {
            var ext = Path.GetExtension(thumbnailPath);
            if (ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
                thumbnailPath = null; // Discard shell thumbnail for audio files
        }

        if (!string.IsNullOrEmpty(thumbnailPath))
            ThumbnailSource = thumbnailPath;

        if (isAudioOnly)
            ShowMusicIcon = string.IsNullOrEmpty(ThumbnailSource);
    }

    [RelayCommand(CanExecute = nameof(IsYouTube))]
    private void OpenOnYouTube()
    {
        if (!IsYouTube) return;

        var url = $"https://www.youtube.com/watch?v={VideoId}";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { /* Ignore errors */ }
    }

    [RelayCommand]
    private void OpenInMediaPlayer()
    {
        var filePath = Path.Combine(CacheManager.CachePath, FileName);
        if (!File.Exists(filePath)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch { /* Ignore errors */ }
    }

    [RelayCommand]
    private async Task CopyUrl()
    {
        var url = $"{ConfigManager.Config.YtdlpWebServerURL}/{FileName}";
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(url);
            }
        }
    }

    [RelayCommand]
    private void Delete()
    {
        CacheManager.DeleteCacheItem(FileName);
        OnDeleted?.Invoke(this);
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        if (bytes == 0) return "0 B";
        var mag = (int)Math.Log(bytes, 1024);
        mag = Math.Min(mag, suffixes.Length - 1);
        var adjustedSize = bytes / Math.Pow(1024, mag);
        return $"{adjustedSize:N2} {suffixes[mag]}";
    }
}

public partial class CacheBrowserViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private CacheItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public ObservableCollection<CacheItemViewModel> CachedVideos { get; } = [];
    public ObservableCollection<CacheItemViewModel> FilteredVideos { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["All"];

    public CacheBrowserViewModel()
    {
        RefreshCache();
        CacheManager.OnCacheChanged += OnCacheChanged;
        VideoDownloader.OnDownloadCompleted += OnDownloadCompleted;
    }

    private void OnCacheChanged(string fileName, CacheChangeType changeType)
    {
        Dispatcher.UIThread.InvokeAsync(RefreshCache);
    }

    private void OnDownloadCompleted(VideoInfo video, bool success)
    {
        if (success)
            Dispatcher.UIThread.InvokeAsync(RefreshCache);
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredVideos.Clear();

        var filter = SearchFilter?.ToLowerInvariant() ?? string.Empty;
        var categoryFilter = SelectedCategory ?? "All";

        foreach (var video in CachedVideos)
        {
            // Category filter
            if (categoryFilter != "All" && video.Category != categoryFilter)
                continue;

            // Text search filter
            if (!string.IsNullOrEmpty(filter) &&
                !video.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !video.VideoId.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !video.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !video.DisplayTitle.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredVideos.Add(video);
        }

        StatusText = $"{FilteredVideos.Count} of {CachedVideos.Count} videos";
    }

    [RelayCommand]
    private void RefreshCache()
    {
        CachedVideos.Clear();
        FilteredVideos.Clear();

        var cachedAssets = CacheManager.GetCachedAssets();
        var itemsToLoad = new List<CacheItemViewModel>();
        var foundCategories = new HashSet<string>();

        foreach (var (fileName, cache) in cachedAssets.OrderByDescending(x => x.Value.LastModified))
        {
            // Filter out non-video files like index.html
            var actualFileName = Path.GetFileName(fileName);
            if (actualFileName.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                continue;

            // Extract video ID from the actual filename (handles subdirectory paths)
            var videoId = Path.GetFileNameWithoutExtension(actualFileName);
            var extension = Path.GetExtension(actualFileName);

            // Determine category from path
            var category = string.Empty;
            var pathParts = fileName.Replace('\\', '/').Split('/');
            if (pathParts.Length > 1)
            {
                category = pathParts[0];
                // Handle CustomDomains/domain_name/ paths
                if (category == "CustomDomains" && pathParts.Length > 2)
                {
                    category = $"Custom: {pathParts[1].Replace("_", ".")}";
                }
            }

            if (!string.IsNullOrEmpty(category))
                foundCategories.Add(category);

            var item = new CacheItemViewModel
            {
                FileName = fileName,
                VideoId = videoId,
                Size = cache.Size,
                LastModified = cache.LastModified,
                Extension = extension,
                Category = category
            };

            // Subscribe to delete event
            item.OnDeleted += OnItemDeleted;

            CachedVideos.Add(item);
            itemsToLoad.Add(item);
        }

        // Update categories list
        var previousSelection = SelectedCategory;
        Categories.Clear();
        Categories.Add("All");
        foreach (var cat in foundCategories.OrderBy(c => c))
        {
            Categories.Add(cat);
        }
        // Restore selection if still valid, otherwise default to "All"
        SelectedCategory = Categories.Contains(previousSelection) ? previousSelection : "All";

        ApplyFilter();

        // Load metadata (titles + thumbnails) asynchronously in the background
        _ = Task.Run(async () =>
        {
            foreach (var item in itemsToLoad)
            {
                await item.LoadMetadataAsync();
            }
        });
    }

    private void OnItemDeleted(CacheItemViewModel item)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CachedVideos.Remove(item);
            FilteredVideos.Remove(item);
            StatusText = $"{FilteredVideos.Count} of {CachedVideos.Count} videos";
        });
    }

    [RelayCommand]
    private void DeleteAll()
    {
        CacheManager.ClearCache();
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        var cachePath = CacheManager.CachePath;
        if (OperatingSystem.IsWindows())
        {
            if (SelectedItem != null)
            {
                var filePath = Path.Combine(cachePath, SelectedItem.FileName);
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", cachePath);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", cachePath);
        }
    }

}
