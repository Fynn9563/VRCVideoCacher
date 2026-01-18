using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL;

public class VideoDownloader
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoDownloader>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
    private static readonly ConcurrentQueue<DownloadQueueItem> DownloadQueue = new();
    private static readonly string TempDownloadMp4Path;
    private static readonly string TempDownloadWebmPath;

    // Events for UI
    public static event Action<VideoInfo>? OnDownloadStarted;
    public static event Action<VideoInfo, bool>? OnDownloadCompleted;
    public static event Action? OnQueueChanged;

    // Current download tracking
    private static VideoInfo? _currentDownload;

    // Internal class to track queue items with their custom domain
    private class DownloadQueueItem
    {
        public required VideoInfo VideoInfo { get; init; }
        public string? CustomDomain { get; init; }
    }

    static VideoDownloader()
    {
        TempDownloadMp4Path = Path.Combine(CacheManager.CachePath, "_tempVideo.mp4");
        TempDownloadWebmPath = Path.Combine(CacheManager.CachePath, "_tempVideo.webm");
        Task.Run(DownloadThread);
    }

    private static async Task DownloadThread()
    {
        while (true)
        {
            await Task.Delay(100);
            if (DownloadQueue.IsEmpty)
            {
                _currentDownload = null;
                continue;
            }

            DownloadQueue.TryPeek(out var queueItem);
            if (queueItem == null)
                continue;

            var videoInfo = queueItem.VideoInfo;
            var customDomain = queueItem.CustomDomain;
            _currentDownload = videoInfo;
            OnDownloadStarted?.Invoke(videoInfo);

            var success = false;
            switch (videoInfo.UrlType)
            {
                case UrlType.YouTube:
                    if (ConfigManager.Config.CacheYouTube)
                        success = await DownloadYouTubeVideo(videoInfo);
                    break;
                case UrlType.PyPyDance:
                    if (ConfigManager.Config.CachePyPyDance)
                        success = await DownloadVideoWithId(videoInfo, customDomain);
                    break;
                case UrlType.VRDancing:
                    if (ConfigManager.Config.CacheVRDancing)
                        success = await DownloadVideoWithId(videoInfo, customDomain);
                    break;
                case UrlType.Other:
                    // Check if this is a custom domain URL
                    if (!string.IsNullOrEmpty(customDomain))
                        success = await DownloadVideoWithId(videoInfo, customDomain);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            DownloadQueue.TryDequeue(out _);
            OnDownloadCompleted?.Invoke(videoInfo, success);
            OnQueueChanged?.Invoke();
            _currentDownload = null;
        }
    }
    
    public static void QueueDownload(VideoInfo videoInfo, string? customDomain = null)
    {
        if (DownloadQueue.Any(x => x.VideoInfo.VideoId == videoInfo.VideoId &&
                                   x.VideoInfo.DownloadFormat == videoInfo.DownloadFormat))
        {
            // Log.Information("URL is already in the download queue.");
            return;
        }

        // Create custom domain directory if needed
        if (!string.IsNullOrEmpty(customDomain))
        {
            CacheManager.EnsureCustomDomainDirectory(customDomain);
        }

        DownloadQueue.Enqueue(new DownloadQueueItem
        {
            VideoInfo = videoInfo,
            CustomDomain = customDomain
        });
        OnQueueChanged?.Invoke();
    }

    // Public accessors for UI
    public static IReadOnlyList<VideoInfo> GetQueueSnapshot() => DownloadQueue.Select(x => x.VideoInfo).ToArray();
    public static int GetQueueCount() => DownloadQueue.Count;
    public static VideoInfo? GetCurrentDownload() => _currentDownload;

    private static async Task<bool> DownloadYouTubeVideo(VideoInfo videoInfo)
    {
        var url = videoInfo.VideoUrl;
        string? videoId;
        try
        {
            videoId = await VideoId.TryGetYouTubeVideoId(url);
        }
        catch (Exception ex)
        {
            Log.Error("Not downloading YouTube video: {URL} {ex}", url, ex.Message);
            return false;
        }

        if (File.Exists(TempDownloadMp4Path))
        {
            Log.Error("Temp file already exists, deleting...");
            File.Delete(TempDownloadMp4Path);
        }
        if (File.Exists(TempDownloadWebmPath))
        {
            Log.Error("Temp file already exists, deleting...");
            File.Delete(TempDownloadWebmPath);
        }

        Log.Information("Downloading YouTube Video: {URL}", url);

        var additionalArgs = ConfigManager.Config.ytdlAdditionalArgs;
        var cookieArg = string.Empty;
        if (Program.IsCookiesEnabledAndValid())
            cookieArg = $"--cookies \"{YtdlManager.CookiesPath}\"";
        
        var audioArg = string.IsNullOrEmpty(ConfigManager.Config.ytdlDubLanguage)
            ? "+ba[acodec=opus][ext=webm]"
            : $"+(ba[acodec=opus][ext=webm][language={ConfigManager.Config.ytdlDubLanguage}]/ba[acodec=opus][ext=webm])";
        
        var audioArgPotato = string.IsNullOrEmpty(ConfigManager.Config.ytdlDubLanguage)
            ? "+ba[ext=m4a]"
            : $"+(ba[ext=m4a][language={ConfigManager.Config.ytdlDubLanguage}]/ba[ext=m4a])";

        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.YtdlPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };
        
        if (videoInfo.DownloadFormat == DownloadFormat.Webm)
        {
            // process.StartInfo.Arguments = $"--encoding utf-8 -q -o \"{TempDownloadMp4Path}\" -f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^(avc|h264)']+ba[ext=m4a]/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec!=av01][vcodec!=vp9.2][protocol^=http]\" --no-playlist --remux-video mp4 --no-progress {cookieArg} {additionalArgs} -- \"{videoId}\"";
            process.StartInfo.Arguments = $"--encoding utf-8 -q -o \"{TempDownloadWebmPath}\" -f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^av01'][ext=mp4][dynamic_range='SDR']{audioArg}/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='vp9'][ext=webm][dynamic_range='SDR']{audioArg}\" --no-mtime --no-playlist --no-progress {cookieArg} {additionalArgs} -- \"{videoId}\"";
        }
        else
        {
            // Potato mode.
            process.StartInfo.Arguments = $"--encoding utf-8 -q -o \"{TempDownloadMp4Path}\" -f \"bv*[height<=1080][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<=1080][vcodec~='^av01'][dynamic_range='SDR']\" --no-mtime --no-playlist --remux-video mp4 --no-progress {cookieArg} {additionalArgs} -- \"{videoId}\"";
            // $@"-f best/bestvideo[height<=?720]+bestaudio --no-playlist --no-warnings {url} " %(id)s.%(ext)s
        }

        process.Start();
        await process.WaitForExitAsync();
        var error = await process.StandardError.ReadToEndAsync();
        error = error.Trim();
        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download YouTube Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            if (error.Contains("Sign in to confirm you’re not a bot"))
                Log.Error("Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

            return false;
        }
        Thread.Sleep(100);
        
        var fileNameOnly = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var relativePath = CacheManager.GetRelativePath(fileNameOnly, UrlType.YouTube);
        var filePath = Path.Combine(CacheManager.CachePath, relativePath);

        // Ensure subdirectory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(TempDownloadMp4Path))
                    File.Delete(TempDownloadMp4Path);
                if (File.Exists(TempDownloadWebmPath))
                    File.Delete(TempDownloadWebmPath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.Message);
            }
            return false;
        }

        if (File.Exists(TempDownloadMp4Path))
        {
            File.Move(TempDownloadMp4Path, filePath);
        }
        else if (File.Exists(TempDownloadWebmPath))
        {
            File.Move(TempDownloadWebmPath, filePath);
        }
        else
        {
            Log.Error("Failed to download YouTube Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(relativePath);
        Log.Information("YouTube Video Downloaded: {URL}", $"{ConfigManager.Config.ytdlWebServerURL}/{relativePath.Replace('\\', '/')}");
        return true;
    }

    private static async Task<bool> DownloadVideoWithId(VideoInfo videoInfo, string? customDomain = null)
    {
        if (File.Exists(TempDownloadMp4Path))
        {
            Log.Error("Temp file already exists, deleting...");
            File.Delete(TempDownloadMp4Path);
        }
        if (File.Exists(TempDownloadWebmPath))
        {
            Log.Error("Temp file already exists, deleting...");
            File.Delete(TempDownloadWebmPath);
        }

        Log.Information("Downloading Video: {URL}", videoInfo.VideoUrl);
        var url = videoInfo.VideoUrl;
        var response = await HttpClient.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Log.Information("Redirected to: {URL}", response.Headers.Location);
            url = response.Headers.Location?.ToString();
            response = await HttpClient.GetAsync(url);
        }
        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Failed to download video: {URL}", url);
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(TempDownloadMp4Path, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream);
        fileStream.Close();
        response.Dispose();
        await Task.Delay(10);

        var fileNameOnly = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var relativePath = CacheManager.GetRelativePath(fileNameOnly, videoInfo.UrlType, customDomain);
        var filePath = Path.Combine(CacheManager.CachePath, relativePath);

        // Ensure subdirectory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(TempDownloadMp4Path))
        {
            File.Move(TempDownloadMp4Path, filePath);
        }
        else if (File.Exists(TempDownloadWebmPath))
        {
            File.Move(TempDownloadWebmPath, filePath);
        }
        else
        {
            Log.Error("Failed to download Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(relativePath);
        Log.Information("Video Downloaded: {URL}", $"{ConfigManager.Config.ytdlWebServerURL}/{relativePath.Replace('\\', '/')}");
        return true;
    }
}