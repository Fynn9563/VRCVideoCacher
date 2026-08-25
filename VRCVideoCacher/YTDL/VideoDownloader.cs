using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL;

public class VideoDownloader
{
    private const string TempDownloadMp4Name = "_tempVideo.mp4";
    private const string TempDownloadWebmName = "_tempVideo.webm";
    private static readonly ILogger Log = Program.Logger.ForContext<VideoDownloader>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
    private static readonly ConcurrentQueue<VideoInfo> DownloadQueue = new();

    // Events for UI
    public static event Action<VideoInfo>? OnDownloadStarted;
    public static event Action<VideoInfo, bool>? OnDownloadCompleted;
    public static event Action? OnQueueChanged;

    // Active download tracking, keyed by video id and format
    private static readonly ConcurrentDictionary<string, ActiveDownload> ActiveDownloads = new();

    private sealed class ActiveDownload
    {
        public required VideoInfo VideoInfo { get; init; }
        public required CancellationTokenSource Cts { get; init; }
    }

    private static string DownloadKey(VideoInfo videoInfo) =>
        $"{videoInfo.VideoId}:{videoInfo.DownloadFormat}";

    static VideoDownloader()
    {
        Task.Run(DownloadDispatcher);
    }

    private static async Task DownloadDispatcher()
    {
        while (true)
        {
            await Task.Delay(100);
            if (DownloadQueue.IsEmpty)
                continue;

            var max = Math.Max(1, ConfigManager.Config.MaxConcurrentDownloads);
            if (ActiveDownloads.Count >= max)
                continue;

            if (!DownloadQueue.TryDequeue(out var queueItem) || queueItem == null)
                continue;

            _ = Task.Run(() => ProcessDownload(queueItem));
        }
    }

    private static async Task ProcessDownload(VideoInfo videoInfo)
    {
        var key = DownloadKey(videoInfo);
        using var cts = new CancellationTokenSource();
        if (!ActiveDownloads.TryAdd(key, new ActiveDownload { VideoInfo = videoInfo, Cts = cts }))
            return;

        OnDownloadStarted?.Invoke(videoInfo);
        OnQueueChanged?.Invoke();

        var success = false;
        try
        {
            var ct = cts.Token;
            switch (videoInfo.UrlType)
            {
                case UrlType.YouTube:
                    success = await DownloadYouTubeVideo(videoInfo, ct);
                    break;
                case UrlType.PyPyDance:
                    success = await DownloadVideoWithId(videoInfo, ct);
                    break;
                case UrlType.VRDancing:
                    success = await DownloadVRDancingVideoWithId(videoInfo, ct);
                    break;
                case UrlType.CustomDomain:
                    // A .m3u8 or .mpd is a manifest, so a direct fetch would save the playlist text
                    // rather than the video. yt-dlp muxes the segments instead.
                    success = videoInfo.IsStreaming
                        ? await DownloadVRDancingVideoWithId(videoInfo, ct)
                        : await DownloadVideoWithId(videoInfo, ct);
                    break;
                case UrlType.Other:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Download cancelled: {VideoId}", videoInfo.VideoId);
        }
        catch (Exception ex)
        {
            Log.Error("Exception during download: {Ex}", ex.ToString());
        }
        finally
        {
            ActiveDownloads.TryRemove(key, out _);
        }

        OnDownloadCompleted?.Invoke(videoInfo, success);
        OnQueueChanged?.Invoke();
    }

    /// Starts the process and kills its whole tree if the token trips, so a cancelled download
    /// leaves no orphaned yt-dlp behind.
    private static async Task RunProcessAsync(Process process, CancellationToken ct)
    {
        process.Start();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to kill cancelled download process: {Ex}", ex.Message);
            }

            throw;
        }
    }

    public static void QueueDownload(VideoInfo videoInfo)
    {
        if (DownloadQueue.Any(x => x.VideoId == videoInfo.VideoId &&
                                   x.DownloadFormat == videoInfo.DownloadFormat))
        {
            // Log.Information("URL is already in the download queue.");
            return;
        }
        if (ActiveDownloads.ContainsKey(DownloadKey(videoInfo)))
        {
            // Log.Information("URL is already being downloaded.");
            return;
        }

        DownloadQueue.Enqueue(videoInfo);
        OnQueueChanged?.Invoke();
    }

    public static void ClearQueue()
    {
        DownloadQueue.Clear();
        OnQueueChanged?.Invoke();
    }

    // Public accessors for UI
    public static IReadOnlyList<VideoInfo> GetQueueSnapshot() => DownloadQueue.ToArray();
    public static int GetQueueCount() => DownloadQueue.Count;
    public static VideoInfo? GetCurrentDownload() => ActiveDownloads.Values.FirstOrDefault()?.VideoInfo;
    public static IReadOnlyList<VideoInfo> GetActiveDownloads() =>
        ActiveDownloads.Values.Select(x => x.VideoInfo).ToArray();

    public static void CancelDownload(string downloadKey)
    {
        if (!ActiveDownloads.TryGetValue(downloadKey, out var active))
            return;

        Log.Information("Cancelling download: {Key}", downloadKey);
        active.Cts.Cancel();
    }

    public static void CancelDownload(VideoInfo videoInfo) => CancelDownload(DownloadKey(videoInfo));

    private static async Task<bool> DownloadYouTubeVideo(VideoInfo videoInfo, CancellationToken ct)
    {
        var url = videoInfo.VideoUrl;

        // Don't run yt-dlp against a video we already know is gone — both to spare the download the two
        // YouTube hits below (id lookup + download) and to avoid the failure spam that gets us bot-checked.
        if (UnavailableVideoCache.IsUnavailable(videoInfo.VideoId))
        {
            Log.Information("Skipping download of known-unavailable YouTube video {VideoId}", videoInfo.VideoId);
            return false;
        }

        string? videoId;
        try
        {
            videoId = await VideoId.TryGetYouTubeVideoId(url);
            if (string.IsNullOrEmpty(videoId))
            {
                Log.Warning("Invalid YouTube URL: {URL}", url);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Not downloading YouTube video: {URL} {ex}", url, ex.ToString());
            return false;
        }

        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);
        var tempDownloadWebmPath = Path.Join(tempDir.FullName, TempDownloadWebmName);

        var args = new List<string>();
        args.Add("-q");

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
            // process.StartInfo.Arguments = $"-q -o \"{TempDownloadMp4Path}\" -f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^(avc|h264)']+ba[ext=m4a]/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec!=av01][vcodec!=vp9.2][protocol^=http]\" --remux-video mp4 {additionalArgs} -- \"{videoId}\"";
            var audioArg = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[acodec=opus][ext=webm]"
                : $"+(ba[acodec=opus][ext=webm][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[acodec=opus][ext=webm])";
            args.Add($"-o \"{tempDownloadWebmPath}\"");
            args.Add($"-f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^av01'][ext=mp4][dynamic_range='SDR']{audioArg}/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='vp9'][ext=webm][dynamic_range='SDR']{audioArg}\"");
        }
        else
        {
            // Potato mode.
            var audioArgPotato = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[ext=m4a]"
                : $"+(ba[ext=m4a][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[ext=m4a])";
            args.Add($"-o \"{tempDownloadMp4Path}\"");
            args.Add($"-f \"bv*[height<=1080][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<=1080][vcodec~='^av01'][dynamic_range='SDR']\"");
            args.Add("--remux-video mp4");
            // $@"-f best/bestvideo[height<=?720]+bestaudio {url} " %(id)s.%(ext)s
        }

        process.StartInfo.Arguments = YtdlManager.GenerateYtdlArgs(args, $"-- \"{videoId}\"");
        Log.Information("Downloading YouTube Video: {Args}", process.StartInfo.Arguments);

        // yt-dlp rewrites the cookie jar on exit; overlapping this download with a URL resolution
        // corrupts the session and gets us bot-checked. See YtdlCookieJar.
        string error;
        using (await YtdlCookieJar.AcquireAsync(ct))
        {
            await RunProcessAsync(process, ct);
            error = (await process.StandardError.ReadToEndAsync()).Trim();
        }

        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download YouTube Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            if (error.Contains("Sign in to confirm you’re not a bot"))
                Log.Error("Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

            return false;
        }
        Thread.Sleep(100);

        var baseFileName = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var fileName = CacheManager.GetRelativePath(UrlType.YouTube, baseFileName);
        var relativeUrl = CacheManager.GetRelativeUrl(UrlType.YouTube, baseFileName);
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
                if (File.Exists(tempDownloadWebmPath))
                    File.Delete(tempDownloadWebmPath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.ToString());
            }
            return false;
        }

        if (File.Exists(tempDownloadMp4Path))
        {
            File.Move(tempDownloadMp4Path, filePath);
        }
        else if (File.Exists(tempDownloadWebmPath))
        {
            File.Move(tempDownloadWebmPath, filePath);
        }
        else
        {
            Log.Error("Failed to download YouTube Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("YouTube Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{relativeUrl}");
        return true;
    }

    private static async Task<bool> DownloadVRDancingVideoWithId(VideoInfo videoInfo, CancellationToken ct)
    {
        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);

        var url = videoInfo.VideoUrl;
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
        process.StartInfo.Arguments = $"-q -o \"{tempDownloadMp4Path}\" --remux-video mp4 \"{url}\"";
        Log.Information("Downloading VRDancing Video: {Args}", process.StartInfo.Arguments);
        await RunProcessAsync(process, ct);
        var error = await process.StandardError.ReadToEndAsync();
        error = error.Trim();
        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download VRDancing Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            return false;
        }
        Thread.Sleep(100);

        var baseFileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var fileName = CacheManager.GetRelativePath(UrlType.VRDancing, baseFileName);
        var relativeUrl = CacheManager.GetRelativeUrl(UrlType.VRDancing, baseFileName);
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(tempDownloadMp4Path))
                    File.Delete(tempDownloadMp4Path);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.ToString());
            }

            return false;
        }
        if (File.Exists(tempDownloadMp4Path))
        {
            File.Move(tempDownloadMp4Path, filePath);
        }
        else
        {
            Log.Error("Failed to download VRDancing Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("VRDancing Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{relativeUrl}");
        return true;
    }

    private static async Task<bool> DownloadVideoWithId(VideoInfo videoInfo, CancellationToken ct)
    {
        using var tempDir = new TempDir();
        var tempDownloadMp4Path = Path.Join(tempDir.FullName, TempDownloadMp4Name);

        Log.Information("Downloading Video: {URL}", videoInfo.VideoUrl);
        var url = videoInfo.VideoUrl;
        var response = await HttpClient.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Log.Information("Redirected to: {URL}", response.Headers.Location);
            url = response.Headers.Location?.ToString();
            response = await HttpClient.GetAsync(url, ct);
        }
        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Failed to download video: {URL}", url);
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempDownloadMp4Path, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream, ct);
        fileStream.Close();
        response.Dispose();
        await Task.Delay(10);

        var baseFileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var fileName = CacheManager.GetRelativePath(videoInfo.UrlType, baseFileName, videoInfo.Domain);
        var relativeUrl = CacheManager.GetRelativeUrl(videoInfo.UrlType, baseFileName, videoInfo.Domain);
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        if (File.Exists(tempDownloadMp4Path))
        {
            File.Move(tempDownloadMp4Path, filePath);
        }
        else
        {
            Log.Error("Failed to download Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{relativeUrl}");
        return true;
    }
}