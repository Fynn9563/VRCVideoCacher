namespace VRCVideoCacher.Services;

public static class ThumbnailManager
{
    public static readonly string CacheDir = Path.Join(Program.DataPath, "MetadataCache");
    public static readonly string ThumbnailCacheDir = Path.Join(CacheDir, "thumbnails");

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    static ThumbnailManager()
    {
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ThumbnailCacheDir);
    }

    public static string GetThumbnailPath(string videoId)
    {
        return Path.Join(ThumbnailCacheDir, $"{videoId}.jpg");
    }

    public static string? GetThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        // Embedded art saves as .jpg, shell extraction as .bmp or .png.
        foreach (var ext in new[] { ".jpg", ".bmp", ".png" })
        {
            var path = Path.Join(ThumbnailCacheDir, $"{videoId}{ext}");
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static async Task<string?> TrySaveThumbnail(string videoId, string url)
    {
        try
        {
            var thumbnailPath = GetThumbnailPath(videoId);
            if (File.Exists(thumbnailPath))
                return null;

            var data = await HttpClient.GetStreamAsync(url);
            await using var fileStream = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write);
            await data.CopyToAsync(fileStream);
            return thumbnailPath;
        }
        catch
        {
            // Silently fail - thumbnail is not critical
            return null;
        }
    }

    /// Reads the real type from magic bytes: cached files routinely carry a wrong extension,
    /// e.g. a WAV or MP3 saved as .mp4.
    private static string? DetectMimeType(string filePath)
    {
        try
        {
            var header = new byte[12];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Read(header, 0, 12) < 12)
                return null;

            if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F')
                return "audio/wav";
            if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                return "audio/mpeg";
            if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
                return "audio/mpeg";
            if (header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p')
                return "video/mp4";
            if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
                return "video/webm";
            if (header[0] == 'f' && header[1] == 'L' && header[2] == 'a' && header[3] == 'C')
                return "audio/flac";
            if (header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S')
                return "audio/ogg";
        }
        catch { }

        return null;
    }

    private static TagLib.File? OpenTagLibFile(string filePath)
    {
        var mime = DetectMimeType(filePath);
        return mime != null
            ? TagLib.File.Create(filePath, mime, TagLib.ReadStyle.Average)
            : TagLib.File.Create(filePath, TagLib.ReadStyle.Average);
    }

    public static string? TryExtractEmbeddedThumbnail(string videoId, string filePath)
    {
        try
        {
            var thumbnailPath = GetThumbnailPath(videoId);
            if (File.Exists(thumbnailPath))
                return thumbnailPath;

            using var tagFile = OpenTagLibFile(filePath);
            if (tagFile == null || tagFile.Tag.Pictures.Length == 0)
                return null;

            var picture = tagFile.Tag.Pictures[0];
            File.WriteAllBytes(thumbnailPath, picture.Data.Data);
            return thumbnailPath;
        }
        catch
        {
            return null;
        }
    }

    /// True for audio-only media, including .mp4 files that carry no video stream.
    public static bool IsAudioOnly(string filePath)
    {
        try
        {
            using var tagFile = OpenTagLibFile(filePath);
            if (tagFile != null)
                return tagFile.Properties.VideoWidth == 0;
        }
        catch { }

        var ext = Path.GetExtension(filePath);
        return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }
}
