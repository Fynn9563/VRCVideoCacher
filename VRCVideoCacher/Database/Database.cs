using Microsoft.EntityFrameworkCore;
using VRCVideoCacher.Database.Models;

namespace VRCVideoCacher.Database;

public class Database : DbContext
{
    private static readonly string CacheDir = Path.Combine(Program.DataPath, "MetadataCache");
    private static readonly string DbPath = Path.Join(CacheDir, "database.db");
    
    public DbSet<History> PlayHistory { get; set; }
    public DbSet<TitleCache> TitleCache { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            Directory.CreateDirectory(CacheDir);
            optionsBuilder.UseSqlite($"Data Source={DbPath}");
        }
    }
}