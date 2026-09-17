using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services.Vrm;

/// <summary>
/// Turns a vr-m.net playback URL into the human-readable title to cache it under.
///
/// VRChat hands us <c>https://vr-m.net/p/&lt;lockId&gt;.m3u8</c>, and the lockId is a bare number with
/// no meaning on disk. The title is recoverable from the manifest's first segment URL (see
/// <see cref="VrmTitleDecoder"/>), so we fetch the manifest once and remember the mapping.
///
/// The mapping has to persist: the cached file is named after the title, and the cache-hit check on
/// every later play has to arrive at that same name without a network round trip.
/// </summary>
public static class VrmTitleService
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(VrmTitleService));

    private const string VrmHost = "vr-m.net";

    /// Cloudflare bot management answers 403 to a default User-Agent, so present as a desktop browser.
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";

    /// Ids are not always numeric: "6htprEx3junxd482" shows up alongside "18104".
    private static readonly Regex PlaybackPath = new(@"^/p/([0-9a-zA-Z]+)\.m3u8$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", BrowserUserAgent }, { "Accept", "*/*" } },
        Timeout = TimeSpan.FromSeconds(15),
    };

    /// Spares the database a lookup per play; the durable copy lives in VideoInfoCache.
    private static readonly ConcurrentDictionary<string, string> Resolved = new();

    public static bool IsVrmHost(Uri uri) =>
        uri.Host.Equals(VrmHost, StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith('.' + VrmHost, StringComparison.OrdinalIgnoreCase);

    /// The lockId from a playback URL, or null if this is not one.
    public static string? LockIdFromUrl(Uri uri)
    {
        var match = PlaybackPath.Match(uri.AbsolutePath);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string CacheKey(string lockId) => $"{VrmHost}:{lockId}";

    /// <summary>
    /// The name to cache this video under, or null when the title could not be recovered (in which
    /// case the caller keeps using the lockId, which is always correct if not informative).
    /// </summary>
    public static async Task<string?> TryGetCacheNameAsync(Uri uri)
    {
        var lockId = LockIdFromUrl(uri);
        if (lockId is null)
            return null;

        if (Resolved.TryGetValue(lockId, out var memo))
            return memo;

        var stored = DatabaseManager.GetVideoInfoCache(CacheKey(lockId))?.Title;
        if (!string.IsNullOrWhiteSpace(stored))
        {
            Resolved[lockId] = stored;
            return stored;
        }

        var decoded = await FetchAndDecodeAsync(uri, lockId);
        if (decoded is null)
            return null;

        var name = VrmTitleDecoder.ToSafeFileName(decoded.Display);
        if (string.IsNullOrEmpty(name))
            return null;

        Resolved[lockId] = name;
        DatabaseManager.AddVideoInfoCache(new VideoInfoCache
        {
            Id = CacheKey(lockId),
            Title = name,
            Type = UrlType.CustomDomain,
        });

        Log.Information("vr-m {LockId} is {Title}", lockId, decoded.Display);
        return name;
    }

    private static async Task<VrmTitleDecoder.DecodedTitle?> FetchAndDecodeAsync(Uri uri, string lockId)
    {
        try
        {
            var manifest = await HttpClient.GetStringAsync(uri);
            var decoded = VrmTitleDecoder.DecodeManifest(manifest);
            if (decoded is null)
                Log.Warning("No title in the vr-m manifest for {LockId}; caching under the id instead", lockId);

            return decoded;
        }
        catch (Exception ex)
        {
            // Never fail a play over this. Falling back to the lockId only costs a worse filename.
            Log.Warning("Could not read the vr-m manifest for {LockId}: {Error}", lockId, ex.Message);
            return null;
        }
    }
}
