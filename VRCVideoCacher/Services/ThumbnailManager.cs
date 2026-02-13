namespace VRCVideoCacher.Services;

public class ThumbnailManager
{
    public static readonly string CacheDir = Path.Combine(Program.DataPath, "MetadataCache");
    public static readonly string ThumbnailCacheDir = Path.Combine(CacheDir, "thumbnails");

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
        return Path.Combine(ThumbnailCacheDir, $"{videoId}.jpg");
    }

    public static string? GetThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        // Check all supported thumbnail formats (embedded saves as .jpg, shell saves as .bmp/.png)
        var jpgPath = Path.Combine(ThumbnailCacheDir, $"{videoId}.jpg");
        if (File.Exists(jpgPath)) return jpgPath;

        var bmpPath = Path.Combine(ThumbnailCacheDir, $"{videoId}.bmp");
        if (File.Exists(bmpPath)) return bmpPath;

        var pngPath = Path.Combine(ThumbnailCacheDir, $"{videoId}.png");
        if (File.Exists(pngPath)) return pngPath;

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

    /// <summary>
    /// Detects the actual mime type from file magic bytes.
    /// Files may have incorrect extensions (e.g. WAV/MP3 files with .mp4 extension).
    /// </summary>
    private static string? DetectMimeType(string filePath)
    {
        try
        {
            var header = new byte[12];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Read(header, 0, 12) < 12)
                return null;

            // RIFF/WAVE
            if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F')
                return "audio/wav";
            // ID3 (MP3 with ID3 tags)
            if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                return "audio/mpeg";
            // MP3 sync bytes (no ID3 header)
            if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
                return "audio/mpeg";
            // MPEG-4 (ftyp box)
            if (header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p')
                return "video/mp4";
            // WebM/Matroska
            if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
                return "video/webm";
            // FLAC
            if (header[0] == 'f' && header[1] == 'L' && header[2] == 'a' && header[3] == 'C')
                return "audio/flac";
            // OGG
            if (header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S')
                return "audio/ogg";
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Opens a media file with TagLib using detected mime type instead of trusting the extension.
    /// </summary>
    private static TagLib.File? OpenTagLibFile(string filePath)
    {
        var mime = DetectMimeType(filePath);
        return mime != null
            ? TagLib.File.Create(filePath, mime, TagLib.ReadStyle.Average)
            : TagLib.File.Create(filePath, TagLib.ReadStyle.Average);
    }

    /// <summary>
    /// Extracts an embedded thumbnail/cover art from a media file using TagLib.
    /// Returns the saved thumbnail path, or null if none found.
    /// </summary>
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

    /// <summary>
    /// Checks if a media file is audio-only (no video stream).
    /// Handles audio-only .mp4 files that wouldn't be caught by extension checks.
    /// </summary>
    public static bool IsAudioOnly(string filePath)
    {
        try
        {
            using var tagFile = OpenTagLibFile(filePath);
            if (tagFile != null)
                return tagFile.Properties.VideoWidth == 0;
        }
        catch { }

        // If we can't read the file, fall back to extension check
        var ext = Path.GetExtension(filePath);
        return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }
}
