using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
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
    private static readonly ConcurrentDictionary<string, ActiveDownload> ActiveDownloads = new();

    // Events for UI
    public static event Action<VideoInfo>? OnDownloadStarted;
    public static event Action<VideoInfo, bool>? OnDownloadCompleted;
    public static event Action? OnQueueChanged;
    public static event Action<VideoInfo, double, string>? OnDownloadProgress;

    private class DownloadQueueItem
    {
        public required VideoInfo VideoInfo { get; init; }
        public string? CustomDomain { get; init; }
    }

    private class ActiveDownload
    {
        public required VideoInfo VideoInfo { get; init; }
        public required CancellationTokenSource Cts { get; init; }
    }

    private class TempPaths
    {
        public required string Directory { get; init; }
        public required string Mp4Path { get; init; }
        public required string WebmPath { get; init; }
    }

    static VideoDownloader()
    {
        CleanupStaleTempDirectories();
        Task.Run(DownloadDispatcher);
    }

    private static void CleanupStaleTempDirectories()
    {
        var tempRoot = Path.Combine(CacheManager.CachePath, "_temp");
        if (!Directory.Exists(tempRoot)) return;
        try { Directory.Delete(tempRoot, true); } catch { }
    }

    private static TempPaths CreateTempPaths()
    {
        var dir = Path.Combine(CacheManager.CachePath, "_temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new TempPaths
        {
            Directory = dir,
            Mp4Path = Path.Combine(dir, "_tempVideo.mp4"),
            WebmPath = Path.Combine(dir, "_tempVideo.webm")
        };
    }

    private static void CleanupTempPaths(TempPaths paths)
    {
        try
        {
            if (Directory.Exists(paths.Directory))
                Directory.Delete(paths.Directory, true);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to cleanup temp directory: {ex}", ex.Message);
        }
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

            if (!DownloadQueue.TryDequeue(out var queueItem))
                continue;

            _ = Task.Run(() => ProcessDownload(queueItem));
        }
    }

    private static async Task ProcessDownload(DownloadQueueItem queueItem)
    {
        var videoInfo = queueItem.VideoInfo;
        var customDomain = queueItem.CustomDomain;
        var downloadKey = $"{videoInfo.VideoId}:{videoInfo.DownloadFormat}";
        var tempPaths = CreateTempPaths();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        ActiveDownloads.TryAdd(downloadKey, new ActiveDownload { VideoInfo = videoInfo, Cts = cts });
        OnDownloadStarted?.Invoke(videoInfo);

        var success = false;
        try
        {
            switch (videoInfo.UrlType)
            {
                case UrlType.YouTube:
                    success = await DownloadYouTubeVideo(videoInfo, tempPaths, token);
                    break;
                case UrlType.PyPyDance:
                case UrlType.VRDancing:
                    success = await DownloadVideoWithId(videoInfo, tempPaths, token, customDomain);
                    break;
                case UrlType.CustomDomain:
                    if (videoInfo.IsStreaming)
                        success = await DownloadCustomDomainWithYtdlp(videoInfo, tempPaths, token);
                    else
                        success = await DownloadVideoWithId(videoInfo, tempPaths, token, customDomain);
                    break;
                case UrlType.Other:
                    if (!string.IsNullOrEmpty(customDomain))
                        success = await DownloadVideoWithId(videoInfo, tempPaths, token, customDomain);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Download cancelled: {VideoId}", videoInfo.VideoId);
            success = false;
        }
        catch (Exception ex)
        {
            Log.Error("Exception during download: {Ex}", ex.Message);
            success = false;
        }
        finally
        {
            CleanupTempPaths(tempPaths);
        }

        ActiveDownloads.TryRemove(downloadKey, out _);
        OnDownloadCompleted?.Invoke(videoInfo, success);
        OnQueueChanged?.Invoke();
    }

    public static void QueueDownload(VideoInfo videoInfo, string? customDomain = null)
    {
        if (DownloadQueue.Any(x => x.VideoInfo.VideoId == videoInfo.VideoId &&
                                   x.VideoInfo.DownloadFormat == videoInfo.DownloadFormat))
        {
            Log.Information("Skipping queue: already queued ({VideoId})", videoInfo.VideoId);
            return;
        }
        if (ActiveDownloads.ContainsKey($"{videoInfo.VideoId}:{videoInfo.DownloadFormat}"))
        {
            Log.Information("Skipping queue: already downloading ({VideoId})", videoInfo.VideoId);
            return;
        }

        if (!string.IsNullOrEmpty(customDomain))
        {
            CacheManager.EnsureCustomDomainDirectory(customDomain);
        }

        Log.Information("Queued download: {VideoId} ({Type}, streaming={IsStreaming})", videoInfo.VideoId, videoInfo.UrlType, videoInfo.IsStreaming);
        DownloadQueue.Enqueue(new DownloadQueueItem
        {
            VideoInfo = videoInfo,
            CustomDomain = customDomain
        });
        OnQueueChanged?.Invoke();
    }

    public static void ClearQueue()
    {
        DownloadQueue.Clear();
        OnQueueChanged?.Invoke();
    }

    // Public accessors for UI
    public static IReadOnlyList<VideoInfo> GetQueueSnapshot() => DownloadQueue.Select(x => x.VideoInfo).ToArray();
    public static int GetQueueCount() => DownloadQueue.Count;
    public static VideoInfo? GetCurrentDownload() => ActiveDownloads.Values.FirstOrDefault()?.VideoInfo;
    public static IReadOnlyList<VideoInfo> GetActiveDownloads() => ActiveDownloads.Values.Select(x => x.VideoInfo).ToArray();

    public static void CancelDownload(string downloadKey)
    {
        if (ActiveDownloads.TryGetValue(downloadKey, out var active))
        {
            Log.Information("Cancelling download: {Key}", downloadKey);
            active.Cts.Cancel();
        }
    }

    private static async Task<bool> DownloadYouTubeVideo(VideoInfo videoInfo, TempPaths tempPaths, CancellationToken ct)
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

        Log.Information("Downloading YouTube Video: {URL}", url);

        var additionalArgs = ConfigManager.Config.YtdlpAdditionalArgs;
        var cookieArg = string.Empty;
        if (Program.IsCookiesEnabledAndValid())
            cookieArg = $"--cookies \"{YtdlManager.CookiesPath}\"";

        var audioArg = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
            ? "+ba[acodec=opus][ext=webm]"
            : $"+(ba[acodec=opus][ext=webm][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[acodec=opus][ext=webm])";

        var audioArgPotato = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
            ? "+ba[ext=m4a]"
            : $"+(ba[ext=m4a][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[ext=m4a])";

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
            process.StartInfo.Arguments = $"--encoding utf-8 --newline --progress-template \"download:%(progress)j\" -o \"{tempPaths.WebmPath}\" -f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^av01'][ext=mp4][dynamic_range='SDR']{audioArg}/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='vp9'][ext=webm][dynamic_range='SDR']{audioArg}\" --no-mtime --no-playlist {cookieArg} {additionalArgs} -- \"{videoId}\"";
        }
        else
        {
            // Potato mode.
            var potatoMaxRes = Math.Min(ConfigManager.Config.CacheYouTubeMaxResolution, 1080);
            process.StartInfo.Arguments = $"--encoding utf-8 --newline --progress-template \"download:%(progress)j\" -o \"{tempPaths.Mp4Path}\" -f \"bv*[height<={potatoMaxRes}][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<={potatoMaxRes}][vcodec~='^av01'][dynamic_range='SDR']\" --no-mtime --no-playlist --remux-video mp4 {cookieArg} {additionalArgs} -- \"{videoId}\"";
        }

        process.Start();
        await using var reg = ct.Register(() => { try { process.Kill(true); } catch { } });
        var stdoutTask = ReadStdoutWithProgress(process, line => ParseYtdlpProgress(line, videoInfo), ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await stdoutTask;
        var error = (await stderrTask).Trim();
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download YouTube Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            if (error.Contains("Sign in to confirm you're not a bot"))
                Log.Error("Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

            return false;
        }
        Thread.Sleep(100);

        var fileName = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var subdirPath = CacheManager.GetSubdirectoryPath(UrlType.YouTube);
        var filePath = Path.Combine(subdirPath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            return false;
        }

        if (File.Exists(tempPaths.Mp4Path))
        {
            File.Move(tempPaths.Mp4Path, filePath);
        }
        else if (File.Exists(tempPaths.WebmPath))
        {
            File.Move(tempPaths.WebmPath, filePath);
        }
        else
        {
            Log.Error("Failed to download YouTube Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(fileName, UrlType.YouTube);
        var relativeUrl = CacheManager.GetRelativePath(UrlType.YouTube, fileName);
        Log.Information("YouTube Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerURL}/{relativeUrl}");
        return true;
    }

    private static async Task<bool> DownloadVideoWithId(VideoInfo videoInfo, TempPaths tempPaths, CancellationToken ct, string? customDomain = null)
    {
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

        var totalBytes = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempPaths.Mp4Path, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long downloaded = 0;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloaded += bytesRead;
            if (totalBytes > 0)
            {
                var percent = (double)downloaded / totalBytes.Value * 100;
                OnDownloadProgress?.Invoke(videoInfo, percent, $"{percent:F1}% — {FormatBytes(downloaded)} / {FormatBytes(totalBytes.Value)}");
            }
            else
            {
                OnDownloadProgress?.Invoke(videoInfo, -1, FormatBytes(downloaded));
            }
        }
        fileStream.Close();
        response.Dispose();
        await Task.Delay(10);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var subdirPath = CacheManager.GetSubdirectoryPath(videoInfo.UrlType, videoInfo.Domain);

        if (!string.IsNullOrEmpty(videoInfo.Domain))
            Directory.CreateDirectory(subdirPath);

        var filePath = Path.Combine(subdirPath, fileName);
        if (File.Exists(tempPaths.Mp4Path))
        {
            File.Move(tempPaths.Mp4Path, filePath);
        }
        else if (File.Exists(tempPaths.WebmPath))
        {
            File.Move(tempPaths.WebmPath, filePath);
        }
        else
        {
            Log.Error("Failed to download Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(fileName, videoInfo.UrlType, videoInfo.Domain);
        var relativeUrl = CacheManager.GetRelativePath(videoInfo.UrlType, fileName, videoInfo.Domain);
        Log.Information("Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerURL}/{relativeUrl}");
        return true;
    }

    private static async Task<bool> DownloadCustomDomainWithYtdlp(VideoInfo videoInfo, TempPaths tempPaths, CancellationToken ct)
    {
        var url = videoInfo.VideoUrl;
        Log.Information("Downloading Streaming Video: {URL}", url);

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

        var uri = new Uri(url);
        var referer = $"{uri.Scheme}://{uri.Host}/";
        process.StartInfo.Arguments = $"--encoding utf-8 --newline --progress-template \"download:%(progress)j\" -o \"{tempPaths.Mp4Path}\" --user-agent \"NSPlayer/12.00.19041.6926 WMFSDK/12.00.19041.6926\" --referer \"{referer}\" --no-playlist -- \"{url}\"";

        process.Start();
        await using var reg = ct.Register(() => { try { process.Kill(true); } catch { } });
        var stdoutTask = ReadStdoutWithProgress(process, line => ParseYtdlpProgress(line, videoInfo), ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await stdoutTask;
        var error = (await stderrTask).Trim();
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            ct.ThrowIfCancellationRequested();
            Log.Warning("yt-dlp failed ({exitCode}): {error}", process.ExitCode, error);
            Log.Information("Trying ffmpeg fallback for: {URL}", url);
            var ffmpegSuccess = await DownloadStreamingWithFfmpeg(url, tempPaths);
            if (!ffmpegSuccess)
            {
                Log.Error("Failed to download streaming video (yt-dlp and ffmpeg both failed): {URL}", url);
                return false;
            }
        }
        Thread.Sleep(10);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var subdirPath = CacheManager.GetSubdirectoryPath(UrlType.CustomDomain, videoInfo.Domain);

        if (!string.IsNullOrEmpty(videoInfo.Domain))
            Directory.CreateDirectory(subdirPath);

        var filePath = Path.Combine(subdirPath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            return false;
        }

        if (File.Exists(tempPaths.Mp4Path))
        {
            File.Move(tempPaths.Mp4Path, filePath);
        }
        else
        {
            Log.Error("Failed to download streaming video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(fileName, UrlType.CustomDomain, videoInfo.Domain);
        var relativeUrl = CacheManager.GetRelativePath(UrlType.CustomDomain, fileName, videoInfo.Domain);
        Log.Information("Streaming Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerURL}/{relativeUrl}");
        return true;
    }

    private static async Task<bool> DownloadStreamingWithFfmpeg(string url, TempPaths tempPaths)
    {
        var ffmpegPath = Path.Combine(ConfigManager.UtilsPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (!File.Exists(ffmpegPath))
        {
            Log.Error("ffmpeg not found at {Path}, cannot fallback", ffmpegPath);
            return false;
        }

        if (File.Exists(tempPaths.Mp4Path))
            File.Delete(tempPaths.Mp4Path);

        var uri = new Uri(url);
        var referer = $"{uri.Scheme}://{uri.Host}/";

        var process = new Process
        {
            StartInfo =
            {
                FileName = ffmpegPath,
                Arguments = $"-user_agent \"NSPlayer/12.00.19041.6926 WMFSDK/12.00.19041.6926\" -referer \"{referer}\" -i \"{url}\" -c copy -y \"{tempPaths.Mp4Path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await stdoutTask;
        var error = (await stderrTask).Trim();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Log.Error("ffmpeg failed ({exitCode}): {error}", process.ExitCode, error);
            return false;
        }

        return File.Exists(tempPaths.Mp4Path);
    }

    private static void ParseYtdlpProgress(string line, VideoInfo videoInfo)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('{')) return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
            if (status != "downloading") return;

            double percent = -1;
            if (root.TryGetProperty("_percent_str", out var pctEl))
            {
                var pctStr = pctEl.GetString()?.Trim().TrimEnd('%');
                if (pctStr != null) double.TryParse(pctStr, CultureInfo.InvariantCulture, out percent);
            }

            var speed = root.TryGetProperty("_speed_str", out var spdEl) ? spdEl.GetString()?.Trim() ?? "?" : "?";
            var eta = root.TryGetProperty("_eta_str", out var etaEl) ? etaEl.GetString()?.Trim() ?? "?" : "?";
            var total = root.TryGetProperty("_total_bytes_str", out var totEl)
                ? totEl.GetString()?.Trim()
                : root.TryGetProperty("_total_bytes_estimate_str", out var estEl)
                    ? $"~{estEl.GetString()?.Trim()}"
                    : null;

            var fragSuffix = "";
            if (root.TryGetProperty("fragment_index", out var fragIdx) && root.TryGetProperty("fragment_count", out var fragCnt))
                fragSuffix = $" (frag {fragIdx.GetInt32()}/{fragCnt.GetInt32()})";

            var text = total != null
                ? $"{percent:F1}% of {total} at {speed} ETA {eta}{fragSuffix}"
                : $"{percent:F1}% at {speed} ETA {eta}{fragSuffix}";

            OnDownloadProgress?.Invoke(videoInfo, percent, text);
        }
        catch (JsonException) { }
    }

    private static async Task ReadStdoutWithProgress(Process process, Action<string> progressParser, CancellationToken ct = default)
    {
        while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            progressParser(line);
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2}GiB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1}MiB",
            >= 1024 => $"{bytes / 1024.0:F0}KiB",
            _ => $"{bytes}B"
        };
    }
}
