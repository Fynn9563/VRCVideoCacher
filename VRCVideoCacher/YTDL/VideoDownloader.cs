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
    private static readonly ConcurrentQueue<VideoInfo> DownloadQueue = new();
    private static readonly string TempDownloadMp4Path;
    private static readonly string TempDownloadWebmPath;
    
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
                continue;

            DownloadQueue.TryPeek(out var queueItem);
            if (queueItem == null)
                continue;
            
            switch (queueItem.UrlType)
            {
                case UrlType.YouTube:
                    if (ConfigManager.Config.CacheYouTube)
                        await DownloadYouTubeVideo(queueItem);
                    break;
                case UrlType.PyPyDance:
                    if (ConfigManager.Config.CachePyPyDance)
                        await DownloadVideoWithId(queueItem);
                    break;
                case UrlType.VRDancing:
                    if (ConfigManager.Config.CacheVRDancing)
                        await DownloadVideoWithId(queueItem);
                    break;
                case UrlType.CustomDomain:
                    if (ConfigManager.Config.CacheCustomDomains.Length > 0)
                    {
                        if (queueItem.IsStreaming)
                            await DownloadCustomDomainWithYtdlp(queueItem);
                        else
                            await DownloadVideoWithId(queueItem);
                    }
                    break;
                case UrlType.Other:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            DownloadQueue.TryDequeue(out _);
        }
    }
    
    public static void QueueDownload(VideoInfo videoInfo)
    {
        if (DownloadQueue.Any(x => x.VideoId == videoInfo.VideoId))
        {
            Log.Information("URL is already in the download queue.");
            return;
        }
        DownloadQueue.Enqueue(videoInfo);
    }

    private static async Task DownloadYouTubeVideo(VideoInfo videoInfo)
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
            return;
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

        var additionalArgs = YtdlArgsHelper.GetYtdlArgs();
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
            
            return;
        }
        Thread.Sleep(10);
        
        var fileName = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var subdirPath = CacheManager.GetSubdirectoryPath(UrlType.YouTube);
        var filePath = Path.Combine(subdirPath, fileName);
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
            return;
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
            return;
        }

        CacheManager.AddToCache(fileName, UrlType.YouTube);
        var relativeUrl = CacheManager.GetRelativePath(UrlType.YouTube, fileName);
        Log.Information("YouTube Video Downloaded: {URL}", $"{ConfigManager.Config.ytdlWebServerURL}/{relativeUrl}");
    }
    
    private static async Task DownloadVideoWithId(VideoInfo videoInfo)
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
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(TempDownloadMp4Path, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream);
        fileStream.Close();
        await Task.Delay(10);
        
        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var subdirPath = CacheManager.GetSubdirectoryPath(videoInfo.UrlType, videoInfo.Domain);

        // Create domain subdirectory if it doesn't exist
        if (!string.IsNullOrEmpty(videoInfo.Domain))
            Directory.CreateDirectory(subdirPath);

        var filePath = Path.Combine(subdirPath, fileName);
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
            return;
        }

        CacheManager.AddToCache(fileName, videoInfo.UrlType, videoInfo.Domain);
        var relativeUrl = CacheManager.GetRelativePath(videoInfo.UrlType, fileName, videoInfo.Domain);
        Log.Information("Video Downloaded: {URL}", $"{ConfigManager.Config.ytdlWebServerURL}/{relativeUrl}");
    }

    private static async Task DownloadCustomDomainWithYtdlp(VideoInfo videoInfo)
    {
        var url = videoInfo.VideoUrl;

        if (File.Exists(TempDownloadMp4Path))
        {
            Log.Error("Temp file already exists, deleting...");
            File.Delete(TempDownloadMp4Path);
        }

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

        process.StartInfo.Arguments = $"--encoding utf-8 -q -o \"{TempDownloadMp4Path}\" --no-playlist --no-progress -- \"{url}\"";

        process.Start();
        await process.WaitForExitAsync();
        var error = await process.StandardError.ReadToEndAsync();
        error = error.Trim();
        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download streaming video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            return;
        }
        Thread.Sleep(10);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var subdirPath = CacheManager.GetSubdirectoryPath(UrlType.CustomDomain, videoInfo.Domain);

        // Create domain subdirectory if it doesn't exist
        if (!string.IsNullOrEmpty(videoInfo.Domain))
            Directory.CreateDirectory(subdirPath);

        var filePath = Path.Combine(subdirPath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(TempDownloadMp4Path))
                    File.Delete(TempDownloadMp4Path);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.Message);
            }
            return;
        }

        if (File.Exists(TempDownloadMp4Path))
        {
            File.Move(TempDownloadMp4Path, filePath);
        }
        else
        {
            Log.Error("Failed to download streaming video: {URL}", url);
            return;
        }

        CacheManager.AddToCache(fileName, UrlType.CustomDomain, videoInfo.Domain);
        var relativeUrl = CacheManager.GetRelativePath(UrlType.CustomDomain, fileName, videoInfo.Domain);
        Log.Information("Streaming Video Downloaded: {URL}", $"{ConfigManager.Config.ytdlWebServerURL}/{relativeUrl}");
    }
}