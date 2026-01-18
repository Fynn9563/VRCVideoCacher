using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;
using VRCVideoCacher.API;
using VRCVideoCacher.Services;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher;

internal sealed class Program
{
    public static string YtdlpHash = string.Empty;
    public const string Version = "2026.1.18";
    public static readonly ILogger Logger = Log.ForContext("SourceContext", "Core");
    public static readonly string CurrentProcessPath = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
    public static readonly string DataPath = OperatingSystem.IsWindows()
        ? CurrentProcessPath
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCVideoCacher");
    public static event Action? OnCookiesUpdated;

    [STAThread]
    public static void Main(string[] args)
    {
        int count = Process.GetProcessesByName("VRCVideoCacher").Length;
        if (count > 1)
        {
            Console.WriteLine("Application is already running");
            Environment.Exit(0);
        }

        // Check for --nogui flag
        if (args.Contains("--nogui"))
        {
            // Run backend only (console mode)
            InitVRCVideoCacher().GetAwaiter().GetResult();
            return;
        }

        // Configure Serilog with UI sink
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                theme: TemplateTheme.Literate))
            .WriteTo.Sink(new UiLogSink())
            .CreateLogger();

        // Start backend on background thread
        Task.Run(async () =>
        {
            try
            {
                await InitVRCVideoCacher();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Backend error "+ex.Message+" "+ex.StackTrace);
            }
        });

        // Give the backend a moment to initialize
        Thread.Sleep(1000);

        // Start the UI
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static async Task InitVRCVideoCacher()
    {
        try { Console.Title = $"VRCVideoCacher v{Version}"; } catch { /* GUI mode, no console */ }

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
        Logger.Information("VRCVideoCacher version {Version} created by {Elly}, {Natsumi}, {Haxy}. Modified by {Fynn}", Version, elly, natsumi, haxy, fynn);

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

        if (ConfigManager.Config.ytdlAutoUpdate && !string.IsNullOrEmpty(ConfigManager.Config.ytdlPath))
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

        if (ConfigManager.Config.ytdlUseCookies && !IsCookiesEnabledAndValid())
            Logger.Warning("No cookies found, please use the browser extension to send cookies or disable \"ytdlUseCookies\" in config.");

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
        if (!ConfigManager.Config.ytdlUseCookies)
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
        // Clear cache on exit based on config settings
        CacheManager.ClearCacheOnExit();

        FileTools.RestoreAllYtdl();
        Logger.Information("Exiting...");
    }

    public static void NotifyCookiesUpdated()
    {
        OnCookiesUpdated?.Invoke();
    }
}
