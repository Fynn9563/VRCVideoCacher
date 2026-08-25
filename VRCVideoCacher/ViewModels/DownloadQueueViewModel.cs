using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class DownloadItemViewModel : ViewModelBase
{
    public string VideoUrl { get; init; } = string.Empty;
    public string VideoId { get; init; } = string.Empty;
    public string UrlType { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;

    // Matches VideoDownloader's ActiveDownloads key, so the cancel button can address one download.
    public string DownloadKey => $"{VideoId}:{Format}";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = string.Empty;

    // yt-dlp reports -1 until it knows the size, so the bar runs indeterminate until then.
    public bool IsProgressKnown => ProgressPercent >= 0;

    partial void OnProgressPercentChanged(double value) => OnPropertyChanged(nameof(IsProgressKnown));
}

public partial class DownloadQueueViewModel : ViewModelBase
{
    [ObservableProperty]
    private DownloadItemViewModel? _currentDownload;

    [ObservableProperty]
    private string _currentStatus = "Idle";

    [ObservableProperty]
    private string _manualUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<DownloadItemViewModel> QueuedDownloads { get; } = [];
    public ObservableCollection<DownloadItemViewModel> ActiveDownloads { get; } = [];

    public DownloadQueueViewModel()
    {
        RefreshQueue();

        VideoDownloader.OnDownloadStarted += OnDownloadStarted;
        VideoDownloader.OnDownloadCompleted += OnDownloadCompleted;
        VideoDownloader.OnQueueChanged += OnQueueChanged;
        VideoDownloader.OnDownloadProgress += OnDownloadProgress;
    }

    private void OnDownloadStarted(VideoInfo video)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            RefreshQueue();
        });
    }

    private void OnDownloadCompleted(VideoInfo video, bool success)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            StatusMessage = success
                ? $"Downloaded: {video.VideoId}"
                : $"Failed to download: {video.VideoId}";
            RefreshQueue();
        });
    }

    private void OnDownloadProgress(VideoInfo video, double percent, string text)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var key = $"{video.VideoId}:{video.DownloadFormat}";
            var item = ActiveDownloads.FirstOrDefault(x => x.DownloadKey == key);
            if (item == null)
                return;

            item.ProgressPercent = percent;
            item.ProgressText = text;
        });
    }

    private void OnQueueChanged()
    {
        Dispatcher.UIThread.InvokeAsync(RefreshQueue);
    }

    [RelayCommand]
    private void RefreshQueue()
    {
        QueuedDownloads.Clear();

        var queue = VideoDownloader.GetQueueSnapshot();
        foreach (var video in queue)
        {
            QueuedDownloads.Add(new DownloadItemViewModel
            {
                VideoUrl = video.VideoUrl,
                VideoId = video.VideoId,
                UrlType = video.UrlType.ToString(),
                Format = video.DownloadFormat.ToString()
            });
        }

        ActiveDownloads.Clear();
        foreach (var video in VideoDownloader.GetActiveDownloads())
        {
            ActiveDownloads.Add(new DownloadItemViewModel
            {
                VideoUrl = video.VideoUrl,
                VideoId = video.VideoId,
                UrlType = video.UrlType.ToString(),
                Format = video.DownloadFormat.ToString()
            });
        }

        CurrentDownload = ActiveDownloads.FirstOrDefault();
        if (ActiveDownloads.Count > 0)
        {
            CurrentStatus = ActiveDownloads.Count == 1
                ? $"Downloading {ActiveDownloads[0].VideoId}..."
                : $"Downloading {ActiveDownloads.Count} videos...";
        }
        else if (QueuedDownloads.Count == 0)
        {
            CurrentStatus = "Idle";
        }
    }

    [RelayCommand]
    private async Task AddManualDownload()
    {
        if (string.IsNullOrWhiteSpace(ManualUrl))
        {
            StatusMessage = "Please enter a URL";
            return;
        }

        try
        {
            var videoInfo = await VideoId.GetVideoId(ManualUrl, true);
            if (videoInfo != null)
            {
                VideoDownloader.QueueDownload(videoInfo);
                StatusMessage = $"Added to queue: {videoInfo.VideoId}";
                ManualUrl = string.Empty;
            }
            else
            {
                StatusMessage = "Could not parse URL";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex}";
        }
    }

    [RelayCommand]
    private void CancelDownload(DownloadItemViewModel item)
    {
        VideoDownloader.CancelDownload(item.DownloadKey);
        StatusMessage = $"Cancelling: {item.VideoId}";
    }

    [RelayCommand]
    private void ClearQueue()
    {
        VideoDownloader.ClearQueue();
        StatusMessage = "Download queue cleared";
    }
}
