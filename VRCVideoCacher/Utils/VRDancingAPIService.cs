using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace VRCVideoCacher;

public class VRDancingAPIService
{
    private const string VRDancingAPIBaseURL = "https://dbapi.vrdancing.club/";

    private static HttpClient HttpClient { get; set; }

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
    
}

public class VRDSongInfo
{
    public string Artist;
    public string Song;
    public string Instructor;
    public string Hash;
}