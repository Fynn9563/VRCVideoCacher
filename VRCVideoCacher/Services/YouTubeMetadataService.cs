using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using VRCVideoCacher;

namespace VRCVideoCacher.Services;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class MetadataJsonContext : JsonSerializerContext
{
}

public static class YouTubeMetadataService
{
    private static readonly ILogger Log = VRCVideoCacher.Program.Logger.ForContext(typeof(YouTubeMetadataService));

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly ConcurrentDictionary<string, string> TitleCache = new();
    private static readonly string CacheDir = Path.Combine(VRCVideoCacher.Program.DataPath, "MetadataCache");
    private static readonly string TitleCacheFile = Path.Combine(CacheDir, "titles.json");
    private static bool _cacheLoaded;
    private static bool _thumbnailDirCreated;

    // Thumbnail cache goes in the video cache folder so it follows the user's selected location
    private static string ThumbnailCacheDir => Path.Combine(CacheManager.CachePath, ".thumbnails");

    static YouTubeMetadataService()
    {
        Directory.CreateDirectory(CacheDir);
    }

    private static void EnsureThumbnailDirExists()
    {
        if (_thumbnailDirCreated) return;
        Directory.CreateDirectory(ThumbnailCacheDir);
        _thumbnailDirCreated = true;
    }

    private static void LoadCacheFromDisk()
    {
        if (_cacheLoaded) return;
        _cacheLoaded = true;

        try
        {
            if (File.Exists(TitleCacheFile))
            {
                var json = File.ReadAllText(TitleCacheFile);
                var cached = JsonSerializer.Deserialize(json, MetadataJsonContext.Default.DictionaryStringString);
                if (cached != null)
                {
                    foreach (var kvp in cached)
                    {
                        TitleCache.TryAdd(kvp.Key, kvp.Value);
                    }
                }
            }
        }
        catch
        {
            // Ignore cache load errors
        }
    }

    private static void SaveCacheToDisk()
    {
        try
        {
            var dict = TitleCache.ToDictionary(k => k.Key, v => v.Value);
            var json = JsonSerializer.Serialize(dict, MetadataJsonContext.Default.DictionaryStringString);
            File.WriteAllText(TitleCacheFile, json);
        }
        catch
        {
            // Ignore cache save errors
        }
    }

    public static async Task<string?> GetVideoTitleAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        LoadCacheFromDisk();

        // Check cache first
        if (TitleCache.TryGetValue(videoId, out var cachedTitle))
            return cachedTitle;

        try
        {
            var url = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={videoId}&format=json";
            var response = await HttpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("title", out var titleElement))
            {
                var title = titleElement.GetString();
                if (!string.IsNullOrEmpty(title))
                {
                    TitleCache[videoId] = title;
                    SaveCacheToDisk();
                    return title;
                }
            }
        }
        catch
        {
            // Silently fail - we'll just show the video ID
        }

        return null;
    }

    public static string GetThumbnailPath(string videoId)
    {
        EnsureThumbnailDirExists();
        return Path.Combine(ThumbnailCacheDir, $"{videoId}.jpg");
    }

    public static string GetCustomThumbnailPath(string videoId, string category)
    {
        EnsureThumbnailDirExists();
        // Sanitize category for use in filename (replace invalid chars)
        var safeCategory = category
            .Replace(":", "")
            .Replace(" ", "_")
            .Replace(".", "_");
        return Path.Combine(ThumbnailCacheDir, $"{safeCategory}_{videoId}.jpg");
    }

    private static string GetAudioOnlyMarkerPath(string videoId, string category)
    {
        EnsureThumbnailDirExists();
        var safeCategory = category
            .Replace(":", "")
            .Replace(" ", "_")
            .Replace(".", "_");
        return Path.Combine(ThumbnailCacheDir, $"{safeCategory}_{videoId}.audio");
    }

    public static async Task<string?> GetCachedThumbnailAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var localPath = GetThumbnailPath(videoId);

        // Return cached thumbnail if exists
        if (File.Exists(localPath))
            return localPath;

        // Download and cache thumbnail
        try
        {
            var url = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";
            var imageBytes = await HttpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, imageBytes);
            return localPath;
        }
        catch
        {
            // Fall back to remote URL
            return null;
        }
    }

    // Special return value indicating the file is audio-only (no video stream, no embedded art)
    public const string AudioOnlyMarker = "::AUDIO_ONLY::";

    /// <summary>
    /// Extracts a thumbnail from a media file using FFmpeg.
    /// Tries multiple methods: embedded artwork (for audio), video frame extraction.
    /// Returns AudioOnlyMarker if the file is audio-only without embedded artwork.
    /// </summary>
    public static async Task<string?> ExtractVideoThumbnailAsync(string videoFilePath, string videoId, string category)
    {
        if (string.IsNullOrEmpty(videoFilePath))
        {
            Log.Debug("ExtractVideoThumbnailAsync: videoFilePath is empty for {VideoId}", videoId);
            return null;
        }

        if (!File.Exists(videoFilePath))
        {
            Log.Debug("ExtractVideoThumbnailAsync: Video file does not exist: {VideoFilePath}", videoFilePath);
            return null;
        }

        var localPath = GetCustomThumbnailPath(videoId, category);
        var audioMarkerPath = GetAudioOnlyMarkerPath(videoId, category);

        // Return cached thumbnail if exists
        if (File.Exists(localPath))
            return localPath;

        // Check if we already determined this is audio-only
        if (File.Exists(audioMarkerPath))
            return AudioOnlyMarker;

        var ffmpegPath = Path.Combine(ConfigManager.UtilsPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (!File.Exists(ffmpegPath))
        {
            Log.Debug("ExtractVideoThumbnailAsync: FFmpeg not found at {FfmpegPath}", ffmpegPath);
            return null;
        }

        try
        {
            // Method 1: Try to extract embedded album art/cover image (works for MP3, M4A, etc.)
            // This extracts attached pictures like album covers
            var embeddedArtProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{videoFilePath}\" -an -vcodec copy -y \"{localPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            embeddedArtProcess.Start();
            var stderr = await embeddedArtProcess.StandardError.ReadToEndAsync();
            await embeddedArtProcess.WaitForExitAsync();

            if (embeddedArtProcess.ExitCode == 0 && File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            {
                Log.Debug("ExtractVideoThumbnailAsync: Successfully extracted embedded artwork for {VideoId}", videoId);
                return localPath;
            }

            // Check if FFmpeg reported this as audio-only (no video stream)
            // FFmpeg output will contain "Stream #0:0: Audio:" but no "Video:" stream (except attached pic which we already tried)
            var isAudioOnly = stderr.Contains("Audio:") &&
                              !stderr.Contains("Video:") ||
                              stderr.Contains("Output file does not contain any stream");

            // Method 2: Try video frame extraction at 15 seconds (to skip intro/black frames)
            Log.Debug("ExtractVideoThumbnailAsync: No embedded art for {VideoId}, trying video frame extraction", videoId);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{videoFilePath}\" -ss 00:00:15 -vframes 1 -vf \"scale=320:-1\" -y \"{localPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            process.Start();
            stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            {
                Log.Debug("ExtractVideoThumbnailAsync: Successfully extracted video frame for {VideoId}", videoId);
                return localPath;
            }

            // Update audio-only detection from this attempt too
            isAudioOnly = isAudioOnly || stderr.Contains("Output file does not contain any stream");

            // Method 3: Try extracting the very first frame (no seeking)
            Log.Debug("ExtractVideoThumbnailAsync: Frame extraction failed for {VideoId}, trying first frame", videoId);

            var retryProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-i \"{videoFilePath}\" -vframes 1 -vf \"scale=320:-1\" -y \"{localPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            retryProcess.Start();
            stderr = await retryProcess.StandardError.ReadToEndAsync();
            await retryProcess.WaitForExitAsync();

            if (retryProcess.ExitCode == 0 && File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            {
                Log.Debug("ExtractVideoThumbnailAsync: Successfully extracted first frame for {VideoId}", videoId);
                return localPath;
            }

            // Final audio-only check
            isAudioOnly = isAudioOnly || stderr.Contains("Output file does not contain any stream");

            // Return special marker if this is an audio-only file
            if (isAudioOnly)
            {
                Log.Debug("ExtractVideoThumbnailAsync: {VideoId} is audio-only, no thumbnail available", videoId);
                // Create marker file to avoid re-running FFmpeg on every refresh
                try { await File.WriteAllTextAsync(audioMarkerPath, "audio-only"); } catch { }
                return AudioOnlyMarker;
            }

            Log.Debug("ExtractVideoThumbnailAsync: Failed to extract thumbnail for {VideoId}", videoId);

            // Clean up any empty/invalid file that might have been created
            if (File.Exists(localPath) && new FileInfo(localPath).Length == 0)
            {
                try { File.Delete(localPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("ExtractVideoThumbnailAsync: Exception for {VideoId}: {Error}", videoId, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Deletes the cached thumbnail for a video (both YouTube and custom domain variants)
    /// </summary>
    public static void DeleteThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return;

        try
        {
            // Delete YouTube thumbnail
            var localPath = GetThumbnailPath(videoId);
            if (File.Exists(localPath))
                File.Delete(localPath);

            // Also delete any custom domain thumbnails with this videoId
            // They are named like "category_videoId.jpg" or "category_videoId.audio"
            if (Directory.Exists(ThumbnailCacheDir))
            {
                var patterns = new[] { $"*_{videoId}.jpg", $"*_{videoId}.audio" };
                foreach (var pattern in patterns)
                {
                    foreach (var file in Directory.GetFiles(ThumbnailCacheDir, pattern))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // Ignore individual deletion errors
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore deletion errors
        }
    }

    /// <summary>
    /// Clears all cached thumbnails
    /// </summary>
    public static void ClearAllThumbnails()
    {
        try
        {
            if (Directory.Exists(ThumbnailCacheDir))
            {
                // Delete both thumbnails and audio-only markers
                var patterns = new[] { "*.jpg", "*.audio" };
                foreach (var pattern in patterns)
                {
                    foreach (var file in Directory.GetFiles(ThumbnailCacheDir, pattern))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // Ignore individual file deletion errors
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
    }
}
