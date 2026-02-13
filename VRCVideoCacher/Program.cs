using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Avalonia;
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;
using VRCVideoCacher.API;
using VRCVideoCacher.Database;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher;

internal sealed partial class Program
{
    public static string YtdlpHash = string.Empty;
    public const string Version = "2.6.0";
    public static readonly ILogger Logger = Log.ForContext("SourceContext", "Core");
    public static readonly string CurrentProcessPath = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
    public static readonly string DataPath = OperatingSystem.IsWindows()
        ? CurrentProcessPath
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCVideoCacher");
    public static event Action? OnCookiesUpdated;
    private static string? _youtubeUsername;

    [STAThread]
    public static void Main(string[] args)
    {
        AdminCheck.SetupArguements(args);

        var processes = Process.GetProcessesByName("VRCVideoCacher");
        if (processes.Length > 1)
        {
            Console.WriteLine("Application is already running, Exiting...");
            Environment.Exit(0);
        }
        foreach (var process in processes)
            process.Dispose();

        // Check for --nogui flag
        if (args.Contains("--nogui"))
        {
            // Run backend only (console mode)
            InitVRCVideoCacher(false).GetAwaiter().GetResult();
            return;
        }

        // Configure Serilog with UI sink
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n\r{@x}",
                theme: TemplateTheme.Literate))
            .WriteTo.File(
                path: "logs/VRCVideoCacher.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5)
            .WriteTo.Sink(new UiLogSink())
            .CreateLogger();

        if (AdminCheck.IsRunningAsAdmin())
        {
            Logger.Warning("Application is running with administrator privileges. This is not recommended for security reasons.");
        }

        // Don't run backend if admin warning is shown
        if (!AdminCheck.ShouldShowAdminWarning())
        {
            // Start backend on background thread
            Task.Run(async () =>
            {
                try
                {
                    await InitVRCVideoCacher(true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Backend error " + ex.Message + " " + ex.StackTrace);
                }
            });
        }

        // Start the UI
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static async Task InitVRCVideoCacher(bool hasGui)
    {
        try { Console.Title = $"VRCVideoCacher v{Version}{AdminCheck.GetAdminTitleWarning()}"; } catch { /* GUI mode, no console */ }

        // Only configure logger if not already configured (e.g., by UI)
        if (Log.Logger.GetType().Name == "SilentLogger")
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(new ExpressionTemplate(
                    "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                    theme: TemplateTheme.Literate))
                .CreateLogger();
        }
        const string elly = "Elly";
        const string natsumi = "Natsumi";
        const string haxy = "Haxy";
        const string fynn = "Fynn9563";
        Logger.Information("VRCVideoCacher version {Version} created by {Elly}, {Natsumi}, {Haxy}", Version, elly, natsumi, haxy);
        Logger.Information("Modified by {Fynn}", fynn);

        if (!hasGui && AdminCheck.ShouldShowAdminWarning())
        {
            Logger.Error(AdminCheck.AdminWarningMessage);
            Environment.Exit(0);
        }

        Directory.CreateDirectory(DataPath);
        await Updater.CheckForUpdates();
        Updater.Cleanup();
        if (Environment.CommandLine.Contains("--Reset"))
        {
            FileTools.RestoreAllYtdl();
            Environment.Exit(0);
        }
        if (Environment.CommandLine.Contains("--Hash"))
        {
            Console.WriteLine(GetOurYtdlpHash());
            Environment.Exit(0);
        }
        Console.CancelKeyPress += (_, _) => Environment.Exit(0);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => OnAppQuit();

        YtdlpHash = GetOurYtdlpHash();

        DatabaseManager.Init();

        if (ConfigManager.Config.YtdlpAutoUpdate && !string.IsNullOrEmpty(ConfigManager.Config.YtdlpPath))
        {
            await YtdlManager.TryDownloadYtdlp();
            YtdlManager.StartYtdlDownloadThread();
            _ = YtdlManager.TryDownloadDeno();
            _ = YtdlManager.TryDownloadFfmpeg();
        }

        if (OperatingSystem.IsWindows())
            AutoStartShortcut.TryUpdateShortcutPath();
        WebServer.Init();
        FileTools.BackupAllYtdl();
        await BulkPreCache.DownloadFileList();

        if (ConfigManager.Config.YtdlpUseCookies && !IsCookiesEnabledAndValid())
            Logger.Warning("No cookies found, please use the browser extension to send cookies or disable \"YtdlpUseCookies\" in config.");
        else if (IsCookiesEnabledAndValid())
            _ = FetchYouTubeUsernameAsync();

        CacheManager.Init();

        // run after init to avoid text spam blocking user input
        if (OperatingSystem.IsWindows())
            _ = WinGet.TryInstallPackages();

        if (YtdlManager.GlobalYtdlConfigExists())
            Logger.Error("Global yt-dlp config file found in \"%AppData%\\yt-dlp\". Please delete it to avoid conflicts with VRCVideoCacher.");

        await Task.Delay(-1);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static bool IsCookiesEnabledAndValid()
    {
        if (!ConfigManager.Config.YtdlpUseCookies)
            return false;

        if (!File.Exists(YtdlManager.CookiesPath))
            return false;

        var cookies = File.ReadAllText(YtdlManager.CookiesPath);
        return IsCookiesValid(cookies);
    }

    public static bool IsCookiesValid(string cookies)
    {
        if (string.IsNullOrEmpty(cookies))
            return false;

        if (cookies.Contains("youtube.com") && cookies.Contains("LOGIN_INFO"))
            return true;

        return false;
    }
    
    public static async Task<bool?> ValidateCookiesAsync()
    {
        if (!IsCookiesEnabledAndValid())
            return null;

        try
        {
            var cookieContainer = new CookieContainer();
            var lines = await File.ReadAllLinesAsync(YtdlManager.CookiesPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length < 7)
                    continue;

                try
                {
                    var domain = parts[0];
                    var path = parts[2];
                    var secure = parts[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                    var name = parts[5];
                    var value = parts[6];

                    cookieContainer.Add(new Cookie(name, value, path, domain) { Secure = secure });
                }
                catch
                {
                    // Skip malformed cookie lines
                }
            }

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CookieContainer = cookieContainer,
                UseCookies = true
            };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await client.GetAsync("https://www.youtube.com/account", cts.Token);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            Logger.Warning("Failed to validate cookies online: {Error}", ex.Message);
            return null;
        }
    }

    // Returns cookie status for display in Settings UI (only when cookies enabled)
    public static string GetCookieStatus()
    {
        if (!ConfigManager.Config.YtdlpUseCookies)
            return string.Empty;

        if (!File.Exists(YtdlManager.CookiesPath))
            return "No cookies";

        var cookies = File.ReadAllText(YtdlManager.CookiesPath);
        if (!IsCookiesValid(cookies))
            return "Invalid cookies";

        return string.IsNullOrEmpty(_youtubeUsername) ? "Logged in" : $"Logged in as {_youtubeUsername}";
    }

    public static Stream GetYtDlpStub()
    {
        return GetEmbeddedResource("VRCVideoCacher.yt-dlp-stub.exe");
    }

    private static Stream GetEmbeddedResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new Exception($"{resourceName} not found in resources.");

        return stream;
    }

    private static string GetOurYtdlpHash()
    {
        var stream = GetYtDlpStub();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        stream.Dispose();
        return ComputeBinaryContentHash(ms.ToArray());
    }

    public static string ComputeBinaryContentHash(byte[] base64)
    {
        return Convert.ToBase64String(SHA256.HashData(base64));
    }

    private static void OnAppQuit()
    {
        CacheManager.ClearCacheOnExit();
        FileTools.RestoreAllYtdl();
        Logger.Information("Exiting...");
    }

    public static void NotifyCookiesUpdated()
    {
        OnCookiesUpdated?.Invoke();
        _ = FetchYouTubeUsernameAsync();
    }

    private static async Task FetchYouTubeUsernameAsync()
    {
        Logger.Debug("FetchYouTubeUsernameAsync called");

        if (!IsCookiesEnabledAndValid())
        {
            Logger.Debug("Cookies not enabled or invalid, skipping username fetch");
            _youtubeUsername = null;
            return;
        }

        try
        {
            // Parse cookies from the Netscape cookie file
            var cookieContainer = new System.Net.CookieContainer();
            var cookieLines = await File.ReadAllLinesAsync(YtdlManager.CookiesPath);
            foreach (var line in cookieLines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length >= 7)
                {
                    try
                    {
                        var domain = parts[0].TrimStart('.');
                        var cookie = new System.Net.Cookie(parts[5], parts[6], parts[2], domain);
                        cookieContainer.Add(cookie);
                    }
                    catch { /* Skip invalid cookies */ }
                }
            }

            using var handler = new HttpClientHandler { CookieContainer = cookieContainer };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            var html = await client.GetStringAsync("https://www.youtube.com/account");
            Logger.Debug("YouTube account page fetched, length: {Length}", html.Length);

            // Extract email from "Signed in as" text in page
            var match = SignedInAsRegex().Match(html);
            if (match.Success)
            {
                _youtubeUsername = match.Groups[1].Value;
                Logger.Information("YouTube account fetched: {Username}", _youtubeUsername);
                OnCookiesUpdated?.Invoke(); // Notify UI to update
            }
            else
            {
                Logger.Debug("Could not find account email in YouTube account page");
                _youtubeUsername = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Failed to fetch YouTube username: {Error}", ex.Message);
            _youtubeUsername = null;
        }
    }

    [GeneratedRegex("\"text\":\"Signed in as \".*?\"text\":\"([^\"]+)\"")]
    private static partial Regex SignedInAsRegex();
}
