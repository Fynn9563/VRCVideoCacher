// ReSharper disable InconsistentNaming
namespace VRCVideoCacher.Models;

public enum UrlType
{
    YouTube,
    PyPyDance,
    VRDancing,
    CustomDomain,
    Other
}

public enum DownloadFormat
{
    MP4,
    Webm
}

public class VideoInfo
{
    public required string VideoUrl;
    public required string VideoId;
    public required UrlType UrlType;
    public required DownloadFormat DownloadFormat;
    // Set for UrlType.CustomDomain: selects the CustomDomains/<domain>/ cache subdirectory.
    public string? Domain;
    // HLS/DASH manifest rather than a plain file: needs yt-dlp, not a straight HTTP GET.
    public bool IsStreaming;
}