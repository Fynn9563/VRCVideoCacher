using System.Diagnostics;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Semver;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher;

namespace VRCVideoCacher;

public class Updater
{
    private const string UpdateUrl = "https://api.github.com/repos/Fynn9563/VRCVideoCacher/releases/latest";
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher.Updater" } }
    };
    private static readonly ILogger Log = Program.Logger.ForContext<Updater>();
    private static readonly string FileName =  OperatingSystem.IsWindows() ? "VRCVideoCacher.exe" : "VRCVideoCacher";
    private static readonly string FilePath = Path.Combine(Program.CurrentProcessPath, FileName);
    private static readonly string BackupFilePath = Path.Combine(Program.CurrentProcessPath, $"VRCVideoCacher_{Program.Version}.bkp");
    private static readonly string TempFilePath = Path.Combine(Program.CurrentProcessPath, "VRCVideoCacher.Temp");

    public static async Task CheckForUpdates()
    {
        Log.Information("Checking for updates...");
        var isDebug = false;
#if DEBUG
            isDebug = true;
#endif
        if (Program.Version.Contains("-dev") || isDebug)
        {
            Log.Information("Running in dev mode. Skipping update check.");
            return;
        }
        using var response = await HttpClient.GetAsync(UpdateUrl);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Failed to check for updates.");
            return;
        }
        var data = await response.Content.ReadAsStringAsync();
        var latestRelease = JsonConvert.DeserializeObject<GitHubRelease>(data);
        if (latestRelease == null)
        {
            Log.Error("Failed to parse update response.");
            return;
        }
        var latestVersion = SemVersion.Parse(latestRelease.tag_name);
        var currentVersion = SemVersion.Parse(Program.Version);
        Log.Information("Latest release: {Latest}, Installed Version: {Installed}", latestVersion, currentVersion);

        // Check if update is available
        // Special case: date-based versions (major >= 2000) should always update to semver versions (major < 100)
        var isCurrentDateBased = currentVersion.Major >= 2000;
        var isLatestSemver = latestVersion.Major < 100;
        var updateAvailable = false;

        if (isCurrentDateBased && isLatestSemver)
        {
            // Transition from date-based to semver versioning
            Log.Information("Transitioning from date-based version to semver.");
            updateAvailable = true;
        }
        else if (SemVersion.ComparePrecedence(currentVersion, latestVersion) < 0)
        {
            updateAvailable = true;
        }

        if (!updateAvailable)
        {
            Log.Information("No updates available.");
            return;
        }
        Log.Information("Update available: {Version}", latestVersion);
        if (ConfigManager.Config.AutoUpdate)
        {
            await UpdateAsync(latestRelease);
            return;
        }
        Log.Information(
            "Auto Update is disabled. Please update manually from the releases page. https://github.com/Fynn9563/VRCVideoCacher/releases");
    }
        
    public static void Cleanup()
    {
        try
        {
            // Clean up any versioned backup files (VRCVideoCacher_*.bkp)
            var backupFiles = Directory.GetFiles(Program.CurrentProcessPath, "VRCVideoCacher_*.bkp");
            foreach (var backupFile in backupFiles)
            {
                try
                {
                    File.Delete(backupFile);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            // Also clean up old non-versioned backup file if it exists
            var oldBackupPath = Path.Combine(Program.CurrentProcessPath, "VRCVideoCacher.bkp");
            if (File.Exists(oldBackupPath))
            {
                try
                {
                    File.Delete(oldBackupPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch
        {
            // Ignore all cleanup errors
        }
    }
        
    private static async Task UpdateAsync(GitHubRelease release)
    {
        foreach (var asset in release.assets)
        {
            if (asset.name != FileName)
                continue;

            File.Move(FilePath, BackupFilePath);
            
            try
            {
                await using var stream = await HttpClient.GetStreamAsync(asset.browser_download_url);
                await using var fileStream = new FileStream(TempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
                fileStream.Close();

                if (await HashCheck(asset.digest))
                {
                    Log.Information("Hash check passed, Replacing binary.");
                    File.Move(TempFilePath, FilePath);
                }
                else
                {
                    Log.Information("Hash check failed, Reverting update.");
                    File.Move(BackupFilePath,FilePath);
                    return;
                }
                Log.Information("Updated to version {Version}", release.tag_name);
                if (!OperatingSystem.IsWindows())
                    FileTools.MarkFileExecutable(FilePath);

                var process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FilePath,
                        UseShellExecute = true,
                        WorkingDirectory = Program.CurrentProcessPath
                    }
                };
                process.Start();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to update: {Message}", ex.Message);
                File.Move(BackupFilePath, FilePath);
                Console.ReadKey();
            }
        }
    }

    private static async Task<bool> HashCheck(string githubHash)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.Open(TempFilePath, FileMode.Open);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        var hashString = Convert.ToHexString(hashBytes);
        githubHash = githubHash.Split(':')[1];
        var hashMatches = string.Equals(githubHash, hashString, StringComparison.OrdinalIgnoreCase);
        Log.Information("FileHash: {FileHash} GitHubHash: {GitHubHash} HashMatch: {HashMatches}", hashString, githubHash, hashMatches);
        return hashMatches;
    }
}