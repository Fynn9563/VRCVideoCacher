using System.ComponentModel.DataAnnotations;

namespace VRCVideoCacher.Database.Models;

public class TitleCache
{
    [Key]
    public required string Id { get; set; }
    public required string Title { get; set; }
}