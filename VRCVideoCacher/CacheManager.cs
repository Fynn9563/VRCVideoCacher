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
    private static readonly string LockFilePath = Path.Combine(Program.DataPath, ".cache.lock");

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
        CheckAndClearPreviousSession();
        BuildCache();
    }

    private static void CreateSubdirectories()
    {
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(Path.Combine(CachePath, "YouTube"));
        Directory.CreateDirectory(Path.Combine(CachePath, "PyPyDance"));
        Directory.CreateDirectory(Path.Combine(CachePath, "VRDancing"));
        Directory.CreateDirectory(Path.Combine(CachePath, "CustomDomains"));
    }

    public static string GetSubdirectoryPath(UrlType urlType, string? domain = null)
    {
        return urlType switch
        {
            UrlType.YouTube => Path.Combine(CachePath, "YouTube"),
            UrlType.PyPyDance => Path.Combine(CachePath, "PyPyDance"),
            UrlType.VRDancing => Path.Combine(CachePath, "VRDancing"),
            UrlType.CustomDomain when !string.IsNullOrEmpty(domain) => Path.Combine(CachePath, "CustomDomains", domain),
            UrlType.CustomDomain => Path.Combine(CachePath, "CustomDomains"),
            _ => CachePath
        };
    }

    public static string GetRelativePath(UrlType urlType, string fileName, string? domain = null)
    {
        return urlType switch
        {
            UrlType.YouTube => Path.Combine("YouTube", fileName),
            UrlType.PyPyDance => Path.Combine("PyPyDance", fileName),
            UrlType.VRDancing => Path.Combine("VRDancing", fileName),
            UrlType.CustomDomain when !string.IsNullOrEmpty(domain) => Path.Combine("CustomDomains", domain, fileName),
            UrlType.CustomDomain => Path.Combine("CustomDomains", fileName),
            _ => fileName
        };
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
        CreateLockFile();
        TryFlushCache();
    }

    private static void BuildCache()
    {
        CachedAssets.Clear();

        ScanDirectory(UrlType.YouTube);
        ScanDirectory(UrlType.PyPyDance);
        ScanDirectory(UrlType.VRDancing);
        ScanCustomDomainDirectories();
    }

    private static void ScanDirectory(UrlType urlType)
    {
        var subdirPath = GetSubdirectoryPath(urlType);
        if (!Directory.Exists(subdirPath))
            return;

        var files = Directory.GetFiles(subdirPath);
        foreach (var path in files)
        {
            var fileName = Path.GetFileName(path);
            AddToCache(fileName, urlType);
        }
    }

    private static void ScanCustomDomainDirectories()
    {
        var customDomainsPath = Path.Combine(CachePath, "CustomDomains");
        if (!Directory.Exists(customDomainsPath))
            return;

        var domainDirs = Directory.GetDirectories(customDomainsPath);
        foreach (var domainDir in domainDirs)
        {
            var domain = Path.GetFileName(domainDir);
            var files = Directory.GetFiles(domainDir);
            foreach (var path in files)
            {
                var fileName = Path.GetFileName(path);
                AddToCache(fileName, UrlType.CustomDomain, domain);
            }
        }

        var rootFiles = Directory.GetFiles(customDomainsPath);
        foreach (var path in rootFiles)
        {
            var fileName = Path.GetFileName(path);
            AddToCache(fileName, UrlType.CustomDomain);
        }
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
            var filePath = Path.Combine(CachePath, oldestFile.Value.FileName);
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
    {
        var subdirPath = GetSubdirectoryPath(urlType, domain);
        var filePath = Path.Combine(subdirPath, fileName);
        if (!File.Exists(filePath))
            return;

        var fileInfo = new FileInfo(filePath);
        var relativePath = GetRelativePath(urlType, fileName, domain);

        var videoCache = new VideoCache
        {
            FileName = relativePath,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };

        var existingCache = CachedAssets.GetOrAdd(videoCache.FileName, videoCache);
        existingCache.Size = fileInfo.Length;
        existingCache.LastModified = fileInfo.LastWriteTimeUtc;

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
            var filePath = Path.Combine(CachePath, fileName);
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
                Log.Error("Failed to delete {FileName}: {Error}", fileName, ex.Message);
            }
        }
        CachedAssets.Clear();

        // Clear all cached thumbnails
        try
        {
            if (Directory.Exists(ThumbnailManager.ThumbnailCacheDir))
            {
                foreach (var file in Directory.GetFiles(ThumbnailManager.ThumbnailCacheDir))
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to clear thumbnails: {Error}", ex.Message);
        }

        OnCacheChanged?.Invoke(string.Empty, CacheChangeType.Cleared);
        Log.Information("Cache cleared");
    }

    public static bool IsCustomDomainUrl(string url, out string? domain)
    {
        domain = null;
        if (ConfigManager.Config.CacheCustomDomains.Length == 0)
            return false;

        foreach (var customDomain in ConfigManager.Config.CacheCustomDomains)
        {
            if (url.Contains(customDomain, StringComparison.OrdinalIgnoreCase))
            {
                domain = customDomain;
                return true;
            }
        }
        return false;
    }

    public static void EnsureCustomDomainDirectory(string domain)
    {
        var domainPath = Path.Combine(CachePath, "CustomDomains", domain);
        Directory.CreateDirectory(domainPath);
    }

    public static void ClearCacheOnExit()
    {
        var directoriesToClear = new List<(UrlType type, string path)>();

        if (ConfigManager.Config.ClearYouTubeCacheOnExit)
            directoriesToClear.Add((UrlType.YouTube, GetSubdirectoryPath(UrlType.YouTube)));
        if (ConfigManager.Config.ClearPyPyDanceCacheOnExit)
            directoriesToClear.Add((UrlType.PyPyDance, GetSubdirectoryPath(UrlType.PyPyDance)));
        if (ConfigManager.Config.ClearVRDancingCacheOnExit)
            directoriesToClear.Add((UrlType.VRDancing, GetSubdirectoryPath(UrlType.VRDancing)));

        if (directoriesToClear.Count == 0 && ConfigManager.Config.ClearCustomDomainsOnExit.Length == 0)
            return;

        Log.Information("Clearing cache on exit...");

        foreach (var (type, path) in directoriesToClear)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path);
                    foreach (var file in files)
                    {
                        // Delete associated thumbnail
                        var videoId = Path.GetFileNameWithoutExtension(file);
                        var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
                        if (File.Exists(thumbnailPath))
                            File.Delete(thumbnailPath);

                        File.Delete(file);
                    }
                    Log.Information("Cleared {Type} cache ({Count} files)", type, files.Length);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to clear {Type} cache: {Error}", type, ex.Message);
            }
        }

        // Clear specific custom domains
        if (ConfigManager.Config.ClearCustomDomainsOnExit.Length > 0)
        {
            var customDomainsPath = Path.Combine(CachePath, "CustomDomains");
            foreach (var domain in ConfigManager.Config.ClearCustomDomainsOnExit)
            {
                try
                {
                    var domainPath = Path.Combine(customDomainsPath, domain);
                    if (Directory.Exists(domainPath))
                    {
                        var files = Directory.GetFiles(domainPath);
                        foreach (var file in files)
                        {
                            // Delete associated thumbnail
                            var videoId = Path.GetFileNameWithoutExtension(file);
                            var thumbnailPath = ThumbnailManager.GetThumbnailPath(videoId);
                            if (File.Exists(thumbnailPath))
                                File.Delete(thumbnailPath);

                            File.Delete(file);
                        }
                        Log.Information("Cleared CustomDomain cache for {Domain} ({Count} files)", domain, files.Length);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to clear CustomDomain cache for {Domain}: {Error}", domain, ex.Message);
                }
            }
        }

        Log.Information("Cache cleanup completed.");
        RemoveLockFile();
    }

    private static void CheckAndClearPreviousSession()
    {
        try
        {
            if (File.Exists(LockFilePath))
            {
                Log.Warning("Lock file found from previous session - previous session did not shut down cleanly");
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to check previous session lock file: {Message}", ex.Message);
        }
    }

    private static void CreateLockFile()
    {
        try
        {
            File.WriteAllText(LockFilePath, DateTime.UtcNow.ToString("O"));
            Log.Debug("Created cache lock file");
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to create lock file: {Message}", ex.Message);
        }
    }

    private static void RemoveLockFile()
    {
        try
        {
            if (File.Exists(LockFilePath))
            {
                File.Delete(LockFilePath);
                Log.Debug("Removed cache lock file on clean shutdown");
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to remove lock file: {Message}", ex.Message);
        }
    }
}