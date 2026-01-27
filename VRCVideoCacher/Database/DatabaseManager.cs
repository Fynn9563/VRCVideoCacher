using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Database;

public static class DatabaseManager
{
    public static readonly Database Database = new();

    static DatabaseManager()
    {
        Database.Database.EnsureCreated();
    }
    
    public static void Init()
    {
        Database.SaveChanges();
    }
    
    public static void AddPlayHistory(VideoInfo videoInfo)
    {
        var history = new History
        {
            Timestamp = DateTime.UtcNow,
            Url = videoInfo.VideoUrl,
            Id = videoInfo.VideoId,
            Type = videoInfo.UrlType
        };
        Database.PlayHistory.Add(history);
        Database.SaveChanges();
    }

    public static void AddTitleCache(string id, string? title)
    {
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(id))
            return;

        var existing = Database.TitleCache.Find(id);
        if (existing != null)
            return;

        var titleCache = new TitleCache
        {
            Id = id,
            Title = title
        };
        Database.TitleCache.Add(titleCache);
        Database.SaveChanges();
    }
}