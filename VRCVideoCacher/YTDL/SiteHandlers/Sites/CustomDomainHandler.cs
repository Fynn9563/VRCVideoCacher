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
        CacheManager.MatchCustomDomain(uri, out _);

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        CacheManager.MatchCustomDomain(uri, out var domain);
        var videoId = VideoId.HashUrl(url);
        Log.Information("Custom domain {Domain} matched for {URL}", domain, url);
        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.CustomDomain,
            DownloadFormat = DownloadFormat.MP4,
            Domain = domain
        });
    }
}
