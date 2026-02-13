using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class DownloadItemViewModel : ViewModelBase
{
    public string DownloadKey { get; init; } = string.Empty;
    public string VideoUrl { get; init; } = string.Empty;
    public string VideoId { get; init; } = string.Empty;
    public string UrlType { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [RelayCommand]
    private void CancelDownload()
    {
        if (!string.IsNullOrEmpty(DownloadKey))
            VideoDownloader.CancelDownload(DownloadKey);
    }
}

public partial class DownloadQueueViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _currentStatus = "Idle";

    [ObservableProperty]
    private string _manualUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<DownloadItemViewModel> ActiveDownloads { get; } = [];
    public ObservableCollection<DownloadItemViewModel> QueuedDownloads { get; } = [];

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
            ActiveDownloads.Add(new DownloadItemViewModel
            {
                DownloadKey = $"{video.VideoId}:{video.DownloadFormat}",
                VideoUrl = video.VideoUrl,
                VideoId = video.VideoId,
                UrlType = video.UrlType.ToString(),
                Format = video.DownloadFormat.ToString(),
                Status = "Downloading..."
            });
            CurrentStatus = ActiveDownloads.Count == 1
                ? $"Downloading {video.VideoId}..."
                : $"Downloading {ActiveDownloads.Count} videos...";
            RefreshQueue();
        });
    }

    private void OnDownloadCompleted(VideoInfo video, bool success)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var item = ActiveDownloads.FirstOrDefault(x => x.VideoId == video.VideoId);
            if (item != null)
                ActiveDownloads.Remove(item);

            CurrentStatus = ActiveDownloads.Count > 0
                ? $"Downloading {ActiveDownloads.Count} video(s)..."
                : "Completed";
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
            var item = ActiveDownloads.FirstOrDefault(x => x.VideoId == video.VideoId);
            if (item == null) return;

            if (percent < 0)
            {
                item.IsProgressIndeterminate = true;
                item.DownloadProgress = 0;
            }
            else
            {
                item.IsProgressIndeterminate = false;
                item.DownloadProgress = percent;
            }
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

        var activeDownloads = VideoDownloader.GetActiveDownloads();
        if (activeDownloads.Count > 0)
        {
            foreach (var dl in activeDownloads)
            {
                if (ActiveDownloads.All(x => x.VideoId != dl.VideoId))
                {
                    ActiveDownloads.Add(new DownloadItemViewModel
                    {
                        DownloadKey = $"{dl.VideoId}:{dl.DownloadFormat}",
                        VideoUrl = dl.VideoUrl,
                        VideoId = dl.VideoId,
                        UrlType = dl.UrlType.ToString(),
                        Format = dl.DownloadFormat.ToString(),
                        Status = "Downloading..."
                    });
                }
            }
            CurrentStatus = ActiveDownloads.Count == 1
                ? $"Downloading {ActiveDownloads[0].VideoId}..."
                : $"Downloading {ActiveDownloads.Count} videos...";
        }
        else
        {
            ActiveDownloads.Clear();
            if (QueuedDownloads.Count == 0)
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
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearQueue()
    {
        VideoDownloader.ClearQueue();
        StatusMessage = "Download queue cleared";
    }
}
