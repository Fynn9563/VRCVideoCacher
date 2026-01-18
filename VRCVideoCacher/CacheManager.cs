using System.Collections.Concurrent;
using Serilog;
using VRCVideoCacher.Models;

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

    // Events for UI
    public static event Action<string, CacheChangeType>? OnCacheChanged;

    // Subdirectory names for different URL types
    private const string YouTubeSubdir = "YouTube";
    private const string PyPyDanceSubdir = "PyPyDance";
    private const string VRDancingSubdir = "VRDancing";
    private const string CustomDomainsSubdir = "CustomDomains";

    static CacheManager()
    {
        if (string.IsNullOrEmpty(ConfigManager.Config.CachedAssetPath))
            CachePath = Path.Combine(GetCacheFolder(), "CachedAssets");
        else if (Path.IsPathRooted(ConfigManager.Config.CachedAssetPath))
            CachePath = ConfigManager.Config.CachedAssetPath;
        else
            CachePath = Path.Combine(Program.CurrentProcessPath, ConfigManager.Config.CachedAssetPath);

        Log.Debug("Using cache path {CachePath}", CachePath);
        CreateSubdirectories();
        BuildCache();
    }

    private static void CreateSubdirectories()
    {
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(Path.Combine(CachePath, YouTubeSubdir));
        Directory.CreateDirectory(Path.Combine(CachePath, PyPyDanceSubdir));
        Directory.CreateDirectory(Path.Combine(CachePath, VRDancingSubdir));
        Directory.CreateDirectory(Path.Combine(CachePath, CustomDomainsSubdir));
    }

    /// <summary>
    /// Gets the subdirectory name for a given URL type.
    /// </summary>
    public static string GetSubdirectoryForUrlType(UrlType urlType)
    {
        return urlType switch
        {
            UrlType.YouTube => YouTubeSubdir,
            UrlType.PyPyDance => PyPyDanceSubdir,
            UrlType.VRDancing => VRDancingSubdir,
            _ => string.Empty
        };
    }

    /// <summary>
    /// Gets the full path for a cached file, including subdirectory organization.
    /// </summary>
    public static string GetCacheFilePath(string fileName, UrlType urlType, string? domain = null)
    {
        if (!string.IsNullOrEmpty(domain))
            return Path.Combine(CachePath, CustomDomainsSubdir, domain, fileName);

        var subdir = GetSubdirectoryForUrlType(urlType);
        if (string.IsNullOrEmpty(subdir))
            return Path.Combine(CachePath, fileName);

        return Path.Combine(CachePath, subdir, fileName);
    }

    /// <summary>
    /// Gets the relative path for a cached file from the cache root.
    /// </summary>
    public static string GetRelativePath(string fileName, UrlType urlType, string? domain = null)
    {
        if (!string.IsNullOrEmpty(domain))
            return Path.Combine(CustomDomainsSubdir, domain, fileName);

        var subdir = GetSubdirectoryForUrlType(urlType);
        if (string.IsNullOrEmpty(subdir))
            return fileName;

        return Path.Combine(subdir, fileName);
    }

    /// <summary>
    /// Checks if a URL matches any configured custom domain for caching.
    /// </summary>
    public static bool IsCustomDomainUrl(string url, out string? domain)
    {
        domain = null;
        if (ConfigManager.Config.CacheCustomDomains.Length == 0)
            return false;

        try
        {
            var uri = new Uri(url);
            foreach (var customDomain in ConfigManager.Config.CacheCustomDomains)
            {
                if (uri.Host.Contains(customDomain, StringComparison.OrdinalIgnoreCase))
                {
                    domain = customDomain.Replace(".", "_");
                    return true;
                }
            }
        }
        catch
        {
            // Invalid URL
        }

        return false;
    }

    private static string GetCacheFolder()
    {
        if (OperatingSystem.IsWindows())
            return Program.CurrentProcessPath;

        var cachePath = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrEmpty(cachePath))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        
        return Path.Combine(cachePath, "VRCVideoCacher");
    }
    
    public static void Init()
    {
        TryFlushCache();
    }
    
    private static void BuildCache()
    {
        CachedAssets.Clear();
        Directory.CreateDirectory(CachePath);

        // Scan root directory
        var files = Directory.GetFiles(CachePath);
        foreach (var path in files)
        {
            var file = Path.GetFileName(path);
            AddToCache(file, path);
        }

        // Scan subdirectories
        foreach (var subdir in new[] { YouTubeSubdir, PyPyDanceSubdir, VRDancingSubdir })
        {
            var subdirPath = Path.Combine(CachePath, subdir);
            if (!Directory.Exists(subdirPath)) continue;

            foreach (var path in Directory.GetFiles(subdirPath))
            {
                var relativePath = Path.Combine(subdir, Path.GetFileName(path));
                AddToCache(relativePath, path);
            }
        }

        // Scan custom domains subdirectory
        var customDomainsPath = Path.Combine(CachePath, CustomDomainsSubdir);
        if (Directory.Exists(customDomainsPath))
        {
            foreach (var domainDir in Directory.GetDirectories(customDomainsPath))
            {
                var domainName = Path.GetFileName(domainDir);
                foreach (var path in Directory.GetFiles(domainDir))
                {
                    var relativePath = Path.Combine(CustomDomainsSubdir, domainName, Path.GetFileName(path));
                    AddToCache(relativePath, path);
                }
            }
        }
    }
    
    private static void TryFlushCache()
    {
        if (ConfigManager.Config.CacheMaxSizeInGb <= 0f)
            return;

        var maxCacheSize = (long)(ConfigManager.Config.CacheMaxSizeInGb * 1024f * 1024f * 1024f);
        var cacheSize = GetCacheSize();
        if (cacheSize < maxCacheSize)
            return;

        var oldestFiles = CachedAssets.OrderBy(x => x.Value.LastModified).ToList();
        while (cacheSize >= maxCacheSize && oldestFiles.Count > 0)
        {
            var oldestFile = oldestFiles.First();
            var filePath = Path.Combine(CachePath, oldestFile.Value.FileName);
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    cacheSize -= oldestFile.Value.Size;
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to delete {FileName} during cache flush: {Error}", oldestFile.Value.FileName, ex.Message);
                }
            }
            CachedAssets.TryRemove(oldestFile.Key, out _);
            oldestFiles.RemoveAt(0);
        }
    }

    public static void AddToCache(string relativePath, string? fullPath = null)
    {
        var filePath = fullPath ?? Path.Combine(CachePath, relativePath);
        if (!File.Exists(filePath))
            return;

        var fileInfo = new FileInfo(filePath);
        var videoCache = new VideoCache
        {
            FileName = relativePath,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };

        var existingCache = CachedAssets.GetOrAdd(relativePath, videoCache);
        existingCache.Size = fileInfo.Length;
        existingCache.LastModified = fileInfo.LastWriteTimeUtc;

        OnCacheChanged?.Invoke(relativePath, CacheChangeType.Added);
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
        var filePath = Path.Combine(CachePath, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            CachedAssets.TryRemove(fileName, out _);
            OnCacheChanged?.Invoke(fileName, CacheChangeType.Removed);
            Log.Information("Deleted cached video: {FileName}", fileName);
        }
    }

    public static void ClearCache()
    {
        var files = CachedAssets.Keys.ToList();
        foreach (var fileName in files)
        {
            var filePath = Path.Combine(CachePath, fileName);
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to delete {FileName}: {Error}", fileName, ex.Message);
                }
            }
        }
        CachedAssets.Clear();
        OnCacheChanged?.Invoke(string.Empty, CacheChangeType.Cleared);
        Log.Information("Cache cleared");
    }

    /// <summary>
    /// Clears a specific subdirectory cache.
    /// </summary>
    private static void ClearSubdirectoryCache(string subdirName)
    {
        var subdirPath = Path.Combine(CachePath, subdirName);
        if (!Directory.Exists(subdirPath))
            return;

        var filesToRemove = CachedAssets.Keys
            .Where(k => k.StartsWith(subdirName + Path.DirectorySeparatorChar) || k.StartsWith(subdirName + "/"))
            .ToList();

        foreach (var relativePath in filesToRemove)
        {
            var filePath = Path.Combine(CachePath, relativePath);
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to delete {FileName}: {Error}", relativePath, ex.Message);
                }
            }
            CachedAssets.TryRemove(relativePath, out _);
        }

        Log.Information("Cleared {SubDir} cache ({Count} files)", subdirName, filesToRemove.Count);
    }

    /// <summary>
    /// Clears cache based on configured "ClearOnExit" settings.
    /// Should be called when the application is exiting.
    /// </summary>
    public static void ClearCacheOnExit()
    {
        if (ConfigManager.Config.ClearYouTubeCacheOnExit)
        {
            Log.Information("Clearing YouTube cache on exit...");
            ClearSubdirectoryCache(YouTubeSubdir);
        }

        if (ConfigManager.Config.ClearPyPyDanceCacheOnExit)
        {
            Log.Information("Clearing PyPyDance cache on exit...");
            ClearSubdirectoryCache(PyPyDanceSubdir);
        }

        if (ConfigManager.Config.ClearVRDancingCacheOnExit)
        {
            Log.Information("Clearing VRDancing cache on exit...");
            ClearSubdirectoryCache(VRDancingSubdir);
        }

        // Clear specific custom domains on exit
        foreach (var domain in ConfigManager.Config.ClearCustomDomainsOnExit)
        {
            var safeDomain = domain.Replace(".", "_");
            var domainPath = Path.Combine(CustomDomainsSubdir, safeDomain);
            Log.Information("Clearing custom domain cache on exit: {Domain}", domain);
            ClearSubdirectoryCache(domainPath);
        }
    }

    /// <summary>
    /// Creates the subdirectory for a custom domain if it doesn't exist.
    /// </summary>
    public static void EnsureCustomDomainDirectory(string domain)
    {
        var safeDomain = domain.Replace(".", "_");
        var domainPath = Path.Combine(CachePath, CustomDomainsSubdir, safeDomain);
        Directory.CreateDirectory(domainPath);
    }
}