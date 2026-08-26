using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

/// Caches direct video links from user-nominated domains. Registered ahead of GenericHandler so a
/// configured domain is recognised, and falls through to it when the feature is off or unmatched.
public class CustomDomainHandler : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<CustomDomainHandler>();

    public bool CanHandle(Uri uri) =>
        ConfigManager.Config.CacheCustomDomainsEnabled &&
        CacheManager.IsCustomDomainUrl(uri, out _);

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        CacheManager.IsCustomDomainUrl(uri, out var domain);

        // Name the cache entry after the URL's own filename, matching how this fork has always
        // stored custom domain videos. Hash only when the URL carries no usable filename.
        var fileName = Path.GetFileName(Uri.UnescapeDataString(uri.LocalPath));
        var videoId = fileName.Split('.')[0];
        if (string.IsNullOrWhiteSpace(videoId))
            videoId = VideoId.HashUrl(url);

        var isStreaming = url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                          url.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase);

        Log.Information("Custom domain {Domain} matched for {URL}", domain, url);
        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.CustomDomain,
            DownloadFormat = DownloadFormat.MP4,
            Domain = domain,
            IsStreaming = isStreaming
        });
    }
}
