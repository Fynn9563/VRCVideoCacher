using System.Net.Http.Headers;
using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Database;

namespace VRCVideoCacher;

public class VRDancingAPIService
{
    private const string VRDancingAPIBaseURL = "https://dbapi.vrdancing.club/";
    private static ILogger Logger = Log.ForContext<VRDancingAPIService>();
    public static HttpClient HttpClient { get; set; }

    static VRDancingAPIService()
    {
        HttpClient = new HttpClient();
        HttpClient.BaseAddress = new Uri(VRDancingAPIBaseURL);
        HttpClient.DefaultRequestHeaders.Add("User-Agent", $"VRCVideoCacher {Program.Version}");
        
    }
    
    public static async Task<VRDSongInfo> GetVideoInfo(string Code)
    {
        var req = await HttpClient.GetAsync($"/api/v1/public/getsong?code={Code}");
        var str = await req.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<VRDSongInfo>(str);
    }

    public static async Task DownloadVRDancingMeta(string code, string videoId)
    {
        try
        {
            var vrddata = await VRDancingAPIService.GetVideoInfo(code);
            var path = Path.Combine(Program.DataPath, "MetadataCache", "thumbnails", $"{videoId}.jpg");
            var imagebytes = await VRDancingAPIService.HttpClient.GetByteArrayAsync(vrddata.ThumbnailURL);
            await File.WriteAllBytesAsync(path, imagebytes);
            DatabaseManager.AddTitleCache(videoId, $"{vrddata.Song}");
        }
        catch (Exception e)
        {
            Logger.Error("Failed to download video metadata", e);
        }
    }
}

public class VRDSongInfo
{
    public string Artist;
    public string Song;
    public string Instructor;
    public string ThumbnailURL;
    public string Hash;
}