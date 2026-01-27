using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;

namespace VRCVideoCacher.Services;

public static class YouTubeMetadataService
{
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromSeconds(10)
    };
    
    private static readonly string CacheDir = Path.Combine(Program.DataPath, "MetadataCache");
    private static readonly string ThumbnailCacheDir = Path.Combine(CacheDir, "thumbnails");

    static YouTubeMetadataService()
    {
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ThumbnailCacheDir);
    }

    public static async Task<string?> GetVideoTitleAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var cachedTitle = await DatabaseManager.Database.TitleCache
            .Where(tc => tc.Id == videoId)
            .Select(tc => tc.Title)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(cachedTitle))
            return cachedTitle;

        try
        {
            var url = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={videoId}&format=json";
            var response = await HttpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("title", out var titleElement))
                return null;
            var title = titleElement.GetString();
            if (string.IsNullOrEmpty(title))
                return null;

            DatabaseManager.AddTitleCache(videoId, title);
            return title;
        }
        catch
        {
            // Silently fail - we'll just show the video ID
        }

        return null;
    }

    private static string GetThumbnailPath(string videoId)
    {
        return Path.Combine(ThumbnailCacheDir, $"{videoId}.jpg");
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
}
