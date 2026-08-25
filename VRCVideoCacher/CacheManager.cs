using System.Collections.Concurrent;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;

namespace VRCVideoCacher;

public enum CacheChangeType
{
    Added,
    Removed,
    Cleared
}

public class CacheManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<CacheManager>();
    private static readonly ConcurrentDictionary<string, VideoCache> CachedAssets = new();
    public static readonly string CachePath;
    // Present while a session is running. Left behind means that session was killed, not closed.
    private static readonly string LockFilePath = Path.Join(Program.DataPath, ".cache.lock");

    private const string YouTubeSubdir = "YouTube";
    private const string PyPyDanceSubdir = "PyPyDance";
    private const string VRDancingSubdir = "VRDancing";
    private const string CustomDomainsSubdir = "CustomDomains";

    // Events for UI
    public static event Action<string, CacheChangeType>? OnCacheChanged;

    static CacheManager()
    {
        if (string.IsNullOrEmpty(ConfigManager.Config.CachedAssetPath))
            CachePath = Path.Join(GetSystemCacheFolder(), "CachedAssets");
        else if (Path.IsPathRooted(ConfigManager.Config.CachedAssetPath))
            CachePath = ConfigManager.Config.CachedAssetPath;
        else
            CachePath = Path.Join(Program.CurrentProcessPath, ConfigManager.Config.CachedAssetPath);

        Log.Debug("Using cache path {CachePath}", CachePath);
        CreateSubdirectories();
        ClearCacheFromUncleanShutdown();
        BuildCache();
    }

    private static string GetSystemCacheFolder()
    {
        if (OperatingSystem.IsWindows())
            return Program.DataPath;

        var cachePath = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrEmpty(cachePath))
            cachePath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");

        return Path.Join(cachePath, "VRCVideoCacher");
    }

    private static void CreateSubdirectories()
    {
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(Path.Join(CachePath, YouTubeSubdir));
        Directory.CreateDirectory(Path.Join(CachePath, PyPyDanceSubdir));
        Directory.CreateDirectory(Path.Join(CachePath, VRDancingSubdir));
        Directory.CreateDirectory(Path.Join(CachePath, CustomDomainsSubdir));
    }

    public static string GetSubdirectoryPath(UrlType urlType, string? domain = null)
    {
        return urlType switch
        {
            UrlType.YouTube => Path.Join(CachePath, YouTubeSubdir),
            UrlType.PyPyDance => Path.Join(CachePath, PyPyDanceSubdir),
            UrlType.VRDancing => Path.Join(CachePath, VRDancingSubdir),
            UrlType.CustomDomain when !string.IsNullOrEmpty(domain) => Path.Join(CachePath, CustomDomainsSubdir, domain),
            UrlType.CustomDomain => Path.Join(CachePath, CustomDomainsSubdir),
            _ => CachePath
        };
    }

    /// Cache-relative path. Doubles as the CachedAssets key and combines with CachePath on disk.
    public static string GetRelativePath(UrlType urlType, string fileName, string? domain = null)
    {
        return urlType switch
        {
            UrlType.YouTube => Path.Join(YouTubeSubdir, fileName),
            UrlType.PyPyDance => Path.Join(PyPyDanceSubdir, fileName),
            UrlType.VRDancing => Path.Join(VRDancingSubdir, fileName),
            UrlType.CustomDomain when !string.IsNullOrEmpty(domain) => Path.Join(CustomDomainsSubdir, domain, fileName),
            UrlType.CustomDomain => Path.Join(CustomDomainsSubdir, fileName),
            _ => fileName
        };
    }

    /// Same path expressed for a URL. Path.Join yields backslashes on Windows, which do not belong in one.
    public static string GetRelativeUrl(UrlType urlType, string fileName, string? domain = null)
        => GetRelativePath(urlType, fileName, domain).Replace('\\', '/');

    public static void Init()
    {
        CreateLockFile();
        TryFlushCache();
    }

    private static void BuildCache()
    {
        CachedAssets.Clear();
        Directory.CreateDirectory(CachePath);

        ScanDirectory(UrlType.YouTube);
        ScanDirectory(UrlType.PyPyDance);
        ScanDirectory(UrlType.VRDancing);
        ScanCustomDomainDirectories();

        // Anything sitting in the cache root, including leftovers from the old flat layout.
        foreach (var path in Directory.GetFiles(CachePath))
        {
            var file = Path.GetFileName(path);
            if (file.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                continue;
            AddToCache(file);
        }
    }

    private static void ScanDirectory(UrlType urlType)
    {
        var subdirPath = GetSubdirectoryPath(urlType);
        if (!Directory.Exists(subdirPath))
            return;

        foreach (var path in Directory.GetFiles(subdirPath))
            AddToCache(GetRelativePath(urlType, Path.GetFileName(path)));
    }

    private static void ScanCustomDomainDirectories()
    {
        var customDomainsPath = Path.Join(CachePath, CustomDomainsSubdir);
        if (!Directory.Exists(customDomainsPath))
            return;

        foreach (var domainDir in Directory.GetDirectories(customDomainsPath))
        {
            var domain = Path.GetFileName(domainDir);
            foreach (var path in Directory.GetFiles(domainDir))
                AddToCache(GetRelativePath(UrlType.CustomDomain, Path.GetFileName(path), domain));
        }

        foreach (var path in Directory.GetFiles(customDomainsPath))
            AddToCache(GetRelativePath(UrlType.CustomDomain, Path.GetFileName(path)));
    }

    public static void TryFlushCache()
    {
        if (ConfigManager.Config.CacheMaxSizeInGb <= 0f)
            return;

        var maxCacheSize = (long)(ConfigManager.Config.CacheMaxSizeInGb * 1024f * 1024f * 1024f);
        // Protected categories are exempt, so only evictable content is measured against the limit.
        var cacheSize = GetEvictableCacheSize();
        if (cacheSize < maxCacheSize)
            return;

        var recentPlayHistory = DatabaseManager.GetPlayHistory();
        // Category priority first (YouTube evicted before custom domains), then oldest.
        var oldestFiles = CachedAssets
            .Where(x => !IsEvictionProtected(x.Value.FileName))
            .OrderBy(x => GetEvictionPriority(x.Value.FileName))
            .ThenBy(x => x.Value.LastModified)
            .ToList();
        while (cacheSize >= maxCacheSize && oldestFiles.Count > 0)
        {
            var oldestFile = oldestFiles.First();
            var filePath = Path.Join(CachePath, oldestFile.Value.FileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                cacheSize -= oldestFile.Value.Size;

                // delete thumbnail if not in recent history
                var videoId = Path.GetFileNameWithoutExtension(oldestFile.Value.FileName);
                if (recentPlayHistory.All(h => h.Id != videoId))
                {
                    var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
                    if (File.Exists(thumbnailPath))
                        File.Delete(thumbnailPath);
                }
            }
            CachedAssets.TryRemove(oldestFile.Key, out _);
            oldestFiles.RemoveAt(0);
        }
    }

    public static void AddToCache(string fileName, UrlType urlType, string? domain = null)
        => AddToCache(GetRelativePath(urlType, fileName, domain));

    public static void AddToCache(string fileName)
    {
        var filePath = Path.Join(CachePath, fileName);
        if (!File.Exists(filePath))
            return;

        var fileInfo = new FileInfo(filePath);
        var videoCache = new VideoCache
        {
            FileName = fileName,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };

        var existingCache = CachedAssets.GetOrAdd(videoCache.FileName, videoCache);
        existingCache.Size = fileInfo.Length;
        existingCache.LastModified = fileInfo.LastWriteTimeUtc;

        OnCacheChanged?.Invoke(fileName, CacheChangeType.Added);
        TryFlushCache();
    }

    private static bool IsEvictionProtected(string relativePath)
    {
        var config = ConfigManager.Config;
        if (relativePath.StartsWith(YouTubeSubdir) && config.EvictionProtectYouTube) return true;
        if (relativePath.StartsWith(PyPyDanceSubdir) && config.EvictionProtectPyPyDance) return true;
        if (relativePath.StartsWith(VRDancingSubdir) && config.EvictionProtectVRDancing) return true;
        if (relativePath.StartsWith(CustomDomainsSubdir) && config.EvictionProtectCustomDomains) return true;
        return false;
    }

    /// Lower is evicted first: YouTube (0), PyPyDance (1), VRDancing (2), CustomDomains (3).
    private static int GetEvictionPriority(string relativePath)
    {
        if (relativePath.StartsWith(YouTubeSubdir)) return 0;
        if (relativePath.StartsWith(PyPyDanceSubdir)) return 1;
        if (relativePath.StartsWith(VRDancingSubdir)) return 2;
        if (relativePath.StartsWith(CustomDomainsSubdir)) return 3;
        return 4;
    }

    private static long GetCacheSize()
    {
        var totalSize = 0L;
        foreach (var cache in CachedAssets)
        {
            totalSize += cache.Value.Size;
        }

        return totalSize;
    }

    // Public accessors for UI
    public static IReadOnlyDictionary<string, VideoCache> GetCachedAssets()
        => CachedAssets.ToDictionary(k => k.Key, v => v.Value);

    public static long GetTotalCacheSize() => GetCacheSize();

    public static long GetEvictableCacheSize() =>
        CachedAssets.Where(x => !IsEvictionProtected(x.Value.FileName)).Sum(x => x.Value.Size);

    public static int GetCachedVideoCount() => CachedAssets.Count;

    public static Dictionary<string, long> GetCategorySizes()
    {
        var sizes = new Dictionary<string, long>
        {
            [YouTubeSubdir] = 0,
            [PyPyDanceSubdir] = 0,
            [VRDancingSubdir] = 0,
            [CustomDomainsSubdir] = 0
        };

        foreach (var cache in CachedAssets)
        {
            var name = cache.Value.FileName;
            if (name.StartsWith(YouTubeSubdir)) sizes[YouTubeSubdir] += cache.Value.Size;
            else if (name.StartsWith(PyPyDanceSubdir)) sizes[PyPyDanceSubdir] += cache.Value.Size;
            else if (name.StartsWith(VRDancingSubdir)) sizes[VRDancingSubdir] += cache.Value.Size;
            else if (name.StartsWith(CustomDomainsSubdir)) sizes[CustomDomainsSubdir] += cache.Value.Size;
        }

        return sizes;
    }

    public static bool IsEvictionProtectionActive() =>
        ConfigManager.Config.EvictionProtectYouTube || ConfigManager.Config.EvictionProtectPyPyDance ||
        ConfigManager.Config.EvictionProtectVRDancing || ConfigManager.Config.EvictionProtectCustomDomains;

    public static void DeleteCacheItem(string fileName)
    {
        var filePath = Path.Join(CachePath, fileName);
        if (!File.Exists(filePath))
            return;

        File.Delete(filePath);
        CachedAssets.TryRemove(fileName, out _);
        OnCacheChanged?.Invoke(fileName, CacheChangeType.Removed);
        Log.Information("Deleted cached video: {FileName}", fileName);
    }

    /// Matches a URL's host against the configured custom domains. Compares the host itself, never
    /// the whole URL, so "https://evil.example/?x=cdn.mysite.com" cannot pass as "cdn.mysite.com".
    public static bool MatchCustomDomain(Uri uri, out string? domain)
    {
        domain = null;
        var host = uri.Host;
        foreach (var candidate in ConfigManager.Config.CacheCustomDomains)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var trimmed = candidate.Trim().TrimStart('.');
            if (host.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith('.' + trimmed, StringComparison.OrdinalIgnoreCase))
            {
                domain = trimmed;
                return true;
            }
        }

        return false;
    }

    /// Clean shutdown: clear what the user asked for, then release the lock so the next start
    /// knows this session ended properly.
    public static void ClearCacheOnExit()
    {
        try
        {
            ClearConfiguredCategories();
        }
        finally
        {
            RemoveLockFile();
        }
    }

    /// VRCX force-closes this app when VRChat exits, and --kill-existing-instance does the same to a
    /// previous instance. Neither runs ProcessExit, so a configured clear-on-exit never happens and
    /// the cache survives a session it was meant to be wiped after. A leftover lock file is the
    /// signal that happened, so do the clearing now, before the cache index is built from disk.
    private static void ClearCacheFromUncleanShutdown()
    {
        try
        {
            if (!File.Exists(LockFilePath))
                return;

            Log.Warning("Previous session was killed rather than closed, clearing now instead");
            ClearConfiguredCategories();
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to clear cache after unclean shutdown: {Message}", ex.Message);
        }
    }

    private static void CreateLockFile()
    {
        try
        {
            File.WriteAllText(LockFilePath, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to create cache lock file: {Message}", ex.Message);
        }
    }

    private static void RemoveLockFile()
    {
        try
        {
            if (File.Exists(LockFilePath))
                File.Delete(LockFilePath);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to remove cache lock file: {Message}", ex.Message);
        }
    }

    private static void ClearConfiguredCategories()
    {
        var directoriesToClear = new List<(UrlType type, string path)>();

        if (ConfigManager.Config.ClearYouTubeCacheOnExit)
            directoriesToClear.Add((UrlType.YouTube, GetSubdirectoryPath(UrlType.YouTube)));
        if (ConfigManager.Config.ClearPyPyDanceCacheOnExit)
            directoriesToClear.Add((UrlType.PyPyDance, GetSubdirectoryPath(UrlType.PyPyDance)));
        if (ConfigManager.Config.ClearVRDancingCacheOnExit)
            directoriesToClear.Add((UrlType.VRDancing, GetSubdirectoryPath(UrlType.VRDancing)));

        var domainsToClear = ConfigManager.Config.ClearCustomDomainsOnExit;
        if (directoriesToClear.Count == 0 && domainsToClear.Length == 0)
            return;

        Log.Information("Clearing cache on exit...");

        foreach (var (type, path) in directoriesToClear)
        {
            try
            {
                if (!Directory.Exists(path))
                    continue;

                var files = Directory.GetFiles(path);
                foreach (var file in files)
                    DeleteCachedFileAndThumbnail(file);

                Log.Information("Cleared {Type} cache ({Count} files)", type, files.Length);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to clear {Type} cache: {Error}", type, ex.ToString());
            }
        }

        foreach (var domain in domainsToClear)
        {
            try
            {
                var domainPath = GetSubdirectoryPath(UrlType.CustomDomain, domain);
                if (!Directory.Exists(domainPath))
                    continue;

                var files = Directory.GetFiles(domainPath);
                foreach (var file in files)
                    DeleteCachedFileAndThumbnail(file);

                Log.Information("Cleared CustomDomain cache for {Domain} ({Count} files)", domain, files.Length);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to clear CustomDomain cache for {Domain}: {Error}", domain, ex.ToString());
            }
        }

        Log.Information("Cache cleanup completed.");
    }

    private static void DeleteCachedFileAndThumbnail(string filePath)
    {
        var videoId = Path.GetFileNameWithoutExtension(filePath);
        var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
        if (File.Exists(thumbnailPath))
            File.Delete(thumbnailPath);

        File.Delete(filePath);
    }

    public static void ClearCache()
    {
        var recentPlayHistory = DatabaseManager.GetPlayHistory();
        var files = CachedAssets.Keys.ToList();
        foreach (var fileName in files)
        {
            var filePath = Path.Join(CachePath, fileName);
            if (!File.Exists(filePath))
                continue;

            try
            {
                File.Delete(filePath);

                // delete thumbnail if not in recent history
                var videoId = Path.GetFileNameWithoutExtension(fileName);
                if (recentPlayHistory.All(h => h.Id != videoId))
                {
                    var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
                    if (File.Exists(thumbnailPath))
                        File.Delete(thumbnailPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete {FileName}: {Error}", fileName, ex.ToString());
            }
        }
        CachedAssets.Clear();
        OnCacheChanged?.Invoke(string.Empty, CacheChangeType.Cleared);
        Log.Information("Cache cleared");
    }
}