using Newtonsoft.Json;

namespace VRCVideoCacher.Services;

public class VvcConfigService
{
    public static VvcConfig CurrentConfig = new();
    private static readonly HttpClient HttpClient = new();

    static VvcConfigService()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", $"VRCVideoCacher v{Program.Version}");
    }

    public static async Task GetConfig()
    {
        var req = await HttpClient.GetAsync("https://vvc.ellyvr.dev/api/v1/config");
        if (req.IsSuccessStatusCode)
        {
            var deserialized = JsonConvert.DeserializeObject<VvcConfig>(await req.Content.ReadAsStringAsync());
            if (deserialized != null)
                CurrentConfig = deserialized;
        }
    }
}

public class VvcConfig
{
    public string motd { get; set; } = string.Empty;
    public int retryCount { get; set; } = 7;
}
