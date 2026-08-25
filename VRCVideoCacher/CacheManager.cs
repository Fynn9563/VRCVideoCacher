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
        var cacheSize = GetCacheSize();
        if (cacheSize < maxCacheSize)
            return;

        var recentPlayHistory = DatabaseManager.GetPlayHistory();
        var oldestFiles = CachedAssets.OrderBy(x => x.Value.LastModified).ToList();
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

    public static int GetCachedVideoCount() => CachedAssets.Count;

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