using System.Runtime.Versioning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace VRCVideoCacher.Utils;

public static class SteamVrStartup
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(SteamVrStartup));
    private const string AppKey = "fynn9563.vrcvideocacher";
    private const string ManifestFileName = "VRCVideoCacher.vrmanifest";

    private static string ManifestPath => Path.Join(Program.CurrentProcessPath, ManifestFileName);

    [SupportedOSPlatform("windows")]
    public static bool IsSteamVrInstalled()
    {
        var runtimePath = GetSteamVrRuntimePath();
        return !string.IsNullOrEmpty(runtimePath) && Directory.Exists(runtimePath);
    }

    [SupportedOSPlatform("windows")]
    public static bool IsAutoStartEnabled()
    {
        if (!File.Exists(ManifestPath))
            return false;

        var appConfigPath = GetAppConfigPath();
        if (appConfigPath == null || !File.Exists(appConfigPath))
            return false;

        try
        {
            var json = JObject.Parse(File.ReadAllText(appConfigPath));
            var manifestPaths = json["manifest_paths"] as JArray;
            if (manifestPaths == null)
                return false;

            var normalizedManifestPath = Path.GetFullPath(ManifestPath);
            return manifestPaths.Any(p =>
                string.Equals(Path.GetFullPath(p.ToString()), normalizedManifestPath,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check SteamVR auto-start status");
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    public static void Enable()
    {
        if (IsAutoStartEnabled())
            return;

        Log.Information("Enabling SteamVR auto-start...");
        try
        {
            WriteManifest();
            RegisterManifest();
            Log.Information("SteamVR auto-start enabled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enable SteamVR auto-start");
        }
    }

    [SupportedOSPlatform("windows")]
    public static void Disable()
    {
        Log.Information("Disabling SteamVR auto-start...");
        try
        {
            UnregisterManifest();
            if (File.Exists(ManifestPath))
                File.Delete(ManifestPath);
            Log.Information("SteamVR auto-start disabled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to disable SteamVR auto-start");
        }
    }

    [SupportedOSPlatform("windows")]
    public static void TryUpdateManifestPath()
    {
        if (!IsAutoStartEnabled())
            return;

        try
        {
            var currentExe = Path.GetFileName(Environment.ProcessPath ?? "VRCVideoCacher.exe");
            var json = JObject.Parse(File.ReadAllText(ManifestPath));
            var apps = json["applications"] as JArray;
            var existingPath = (apps?.FirstOrDefault() as JObject)?["binary_path_windows"]?.ToString();
            if (existingPath == currentExe)
                return;

            Log.Information("Updating SteamVR auto-start manifest path...");
            WriteManifest();
            RegisterManifest();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update SteamVR manifest path");
        }
    }

    private static void WriteManifest()
    {
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "VRCVideoCacher.exe");
        var manifest = new
        {
            source = "builtin",
            applications = new[]
            {
                new
                {
                    app_key = AppKey,
                    launch_type = "binary",
                    binary_path_windows = exeName,
                    is_dashboard_overlay = false,
                    auto_launch = true,
                    strings = new { en_us = new { name = "VRCVideoCacher" } }
                }
            }
        };
        File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
    }

    private static void RegisterManifest()
    {
        var appConfigPath = GetAppConfigPath();
        if (appConfigPath == null)
        {
            Log.Error("Could not find SteamVR appconfig.json");
            return;
        }

        var normalizedManifestPath = Path.GetFullPath(ManifestPath);

        JObject json;
        if (File.Exists(appConfigPath))
            json = JObject.Parse(File.ReadAllText(appConfigPath));
        else
            json = new JObject();

        var manifestPaths = json["manifest_paths"] as JArray ?? new JArray();
        json["manifest_paths"] = manifestPaths;

        var alreadyRegistered = manifestPaths.Any(p =>
            string.Equals(Path.GetFullPath(p.ToString()), normalizedManifestPath,
                StringComparison.OrdinalIgnoreCase));

        if (!alreadyRegistered)
        {
            manifestPaths.Add(normalizedManifestPath);
            File.WriteAllText(appConfigPath, json.ToString(Formatting.Indented));
        }
    }

    private static void UnregisterManifest()
    {
        var appConfigPath = GetAppConfigPath();
        if (appConfigPath == null || !File.Exists(appConfigPath))
            return;

        var normalizedManifestPath = Path.GetFullPath(ManifestPath);
        var json = JObject.Parse(File.ReadAllText(appConfigPath));
        var manifestPaths = json["manifest_paths"] as JArray;
        if (manifestPaths == null)
            return;

        var toRemove = manifestPaths
            .Where(p => string.Equals(Path.GetFullPath(p.ToString()), normalizedManifestPath,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (toRemove.Count == 0)
            return;

        foreach (var item in toRemove)
            item.Remove();

        File.WriteAllText(appConfigPath, json.ToString(Formatting.Indented));
    }

    private static string? GetAppConfigPath()
    {
        var configPath = GetSteamConfigPath();
        return configPath == null ? null : Path.Join(configPath, "appconfig.json");
    }

    private static string? GetSteamConfigPath()
    {
        var vrpathFile = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openvr", "openvrpaths.vrpath");

        if (!File.Exists(vrpathFile))
            return null;

        try
        {
            var json = JObject.Parse(File.ReadAllText(vrpathFile));
            return (json["config"] as JArray)?.FirstOrDefault()?.ToString();
        }
        catch { return null; }
    }

    private static string? GetSteamVrRuntimePath()
    {
        var vrpathFile = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openvr", "openvrpaths.vrpath");

        if (!File.Exists(vrpathFile))
            return null;

        try
        {
            var json = JObject.Parse(File.ReadAllText(vrpathFile));
            return (json["runtime"] as JArray)?.FirstOrDefault()?.ToString();
        }
        catch { return null; }
    }
}
