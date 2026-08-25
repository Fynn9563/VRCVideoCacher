using System.Diagnostics;
using Sentry.Serilog;
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.Utils;

public static class LoggerUtils
{
    // Empty: this fork ships no crash reporting. Set a DSN here to turn it back on.
    private const string SentryDsn = "";
    private static readonly string LogsPath = Path.Join(Program.DataPath, "Logs");
    private static DateTime? LoggerStartDateTime;

    private static bool ErrorReportingEnabled => LaunchArgs.ErrorReporting && !string.IsNullOrEmpty(SentryDsn);

    public static void InitializeLogger()
    {
        if (ErrorReportingEnabled)
        {
            SentrySdk.Init(GetSentryOptions());
        }

        LoggerStartDateTime = DateTime.Now;
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}" + Environment.NewLine + "{@x}",
                theme: TemplateTheme.Literate))
            .WriteTo.File(
                path: Path.Combine(LogsPath, "VRCVideoCacher.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5);

        if (ErrorReportingEnabled)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.Sentry(ConfigureSentryOptions);
        }

        if (LaunchArgs.HasGui)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.Sink(new UiLogSink());
        }

        Log.Logger = loggerConfiguration.CreateLogger();
    }

    public static void LogUnhandledException(Exception ex, string message)
    {
        try
        {
            Console.WriteLine($"{message}: " + ex);
        }
        catch
        {
        }

        try
        {
            if (ErrorReportingEnabled)
            {
                SentrySdk.ConfigureScope(scope =>
                {
                    var configPath = Path.Join(Program.DataPath, "Config.json");
                    if (File.Exists(configPath))
                        scope.AddAttachment(configPath);
                });
                SentrySdk.CaptureException(ex);
            }
        }
        catch
        {
        }

        try
        {
            Program.Logger.Error(ex, "{Message}", message);

            var logFile = Path.Combine(LogsPath, $"VRCVideoCacher{(LoggerStartDateTime ?? DateTime.Now):yyyyMMdd}.log");
            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(logFile))
                {
                    Process.Start("explorer.exe", $"/select,\"{logFile}\"");
                }
                else
                {
                    Process.Start("explorer.exe", LogsPath);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", LogsPath);
            }
        }
        catch
        {
        }
    }

    private static void ConfigureSentryOptions(SentrySerilogOptions o)
    {
        SentrySdk.SetTag("noGui", LaunchArgs.HasGui.ToString());
        SentrySdk.SetTag("globalPath", LaunchArgs.UseGlobalPath.ToString());
        o.Dsn = SentryDsn;
        o.AutoSessionTracking = true;
        o.IsGlobalModeEnabled = true;
        o.Release = Program.Version;
        var platform = OperatingSystem.IsLinux() ? "linux" : "windows";
#if STEAMRELEASE
        o.Environment = $"steam-{platform}";
#else
        o.Environment = platform;
#endif
        o.EnableLogs = true;
    }

    public static SentrySerilogOptions GetSentryOptions()
    {
        var options = new SentrySerilogOptions();
        ConfigureSentryOptions(options);
        return options;
    }

}
