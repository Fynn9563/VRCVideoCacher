using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VRCVideoCacher.ViewModels;

public partial class CookieSetupViewModel : ViewModelBase
{
    private const string ChromeExtensionUrl = "https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge";
    private const string FirefoxExtensionUrl = "https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter";

    public event Action? RequestClose;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _isChrome;

    [ObservableProperty]
    private bool _cookiesReceived;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;

    public bool CanGoBack => CurrentStep > 1 && CurrentStep < 4;
    public bool CanGoNext => CurrentStep switch
    {
        1 => false, // Must select browser
        2 => true,
        3 => CookiesReceived,
        4 => true,
        _ => false
    };

    public string NextButtonText => CurrentStep == 4 ? "Done" : "Next";

    public string ExtensionStoreButtonText => IsChrome
        ? "Open Chrome Web Store"
        : "Open Firefox Add-ons";

    public string CookieStatusText => CookiesReceived
        ? "Cookies received!"
        : "Waiting for cookies...";

    public string CookieStatusIcon => CookiesReceived
        ? "CheckCircle"
        : "TimerSand";

    public IBrush CookieStatusColor => CookiesReceived
        ? new SolidColorBrush(Color.Parse("#81C784"))
        : new SolidColorBrush(Color.Parse("#FFB74D"));

    public CookieSetupViewModel()
    {
        // Subscribe to cookies updated event
        VRCVideoCacher.Program.OnCookiesUpdated += OnCookiesUpdated;

        // Check if cookies are already valid
        CookiesReceived = VRCVideoCacher.Program.IsCookiesEnabledAndValid();
    }

    private void OnCookiesUpdated()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CookiesReceived = VRCVideoCacher.Program.IsCookiesEnabledAndValid();
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CookieStatusText));
            OnPropertyChanged(nameof(CookieStatusIcon));
            OnPropertyChanged(nameof(CookieStatusColor));

            // Auto-advance to step 4 when cookies are received
            if (CookiesReceived && CurrentStep == 3)
            {
                CurrentStep = 4;
                UpdateStepProperties();
            }
        });
    }

    partial void OnCurrentStepChanged(int value)
    {
        UpdateStepProperties();
    }

    private void UpdateStepProperties()
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(NextButtonText));
    }

    [RelayCommand]
    private void SelectChrome()
    {
        IsChrome = true;
        CurrentStep = 2;
        OnPropertyChanged(nameof(ExtensionStoreButtonText));
    }

    [RelayCommand]
    private void SelectFirefox()
    {
        IsChrome = false;
        CurrentStep = 2;
        OnPropertyChanged(nameof(ExtensionStoreButtonText));
    }

    [RelayCommand]
    private void OpenExtensionStore()
    {
        var url = IsChrome ? ChromeExtensionUrl : FirefoxExtensionUrl;
        OpenUrlInBrowser(url, IsChrome);
    }

    [RelayCommand]
    private void OpenYouTube()
    {
        OpenUrlInBrowser("https://www.youtube.com", IsChrome);
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep == 4)
        {
            // Mark setup as completed and save config
            ConfigManager.Config.CookieSetupCompleted = true;
            ConfigManager.TrySaveConfig();

            // Done - close the window
            VRCVideoCacher.Program.OnCookiesUpdated -= OnCookiesUpdated;
            RequestClose?.Invoke();
            return;
        }

        if (CurrentStep < 4)
        {
            CurrentStep++;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        VRCVideoCacher.Program.OnCookiesUpdated -= OnCookiesUpdated;
        RequestClose?.Invoke();
    }

    private static void OpenUrlInBrowser(string url, bool useChrome)
    {
        var browserPath = useChrome ? FindChromePath() : FindFirefoxPath();

        try
        {
            if (!string.IsNullOrEmpty(browserPath))
            {
                // Open with specific browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = browserPath,
                    Arguments = url,
                    UseShellExecute = false
                });
            }
            else
            {
                // Fallback to default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        }
        catch { /* Ignore errors */ }
    }

    private static string? FindChromePath()
    {
        if (OperatingSystem.IsWindows())
        {
            // Try registry first
            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
            };

            foreach (var regPath in registryPaths)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath);
                    var path = key?.GetValue(null)?.ToString();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return path;
                }
                catch { /* Registry access failed */ }
            }

            // Try common paths as fallback
            var commonPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux: check common locations and PATH
            var linuxPaths = new[] { "google-chrome", "google-chrome-stable", "chromium", "chromium-browser" };
            foreach (var browser in linuxPaths)
            {
                var path = FindInPath(browser);
                if (path != null)
                    return path;
            }
        }
        return null;
    }

    private static string? FindFirefoxPath()
    {
        if (OperatingSystem.IsWindows())
        {
            // Try registry first
            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe"
            };

            foreach (var regPath in registryPaths)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath);
                    var path = key?.GetValue(null)?.ToString();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return path;
                }
                catch { /* Registry access failed */ }
            }

            // Try common paths as fallback
            var commonPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Mozilla Firefox\firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Mozilla Firefox\firefox.exe")
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var linuxPaths = new[] { "firefox", "firefox-esr" };
            foreach (var browser in linuxPaths)
            {
                var path = FindInPath(browser);
                if (path != null)
                    return path;
            }
        }
        return null;
    }

    private static string? FindInPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, executable);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }
}
