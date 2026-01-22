using System.Text.RegularExpressions;
using Avalonia.Media;
// ReSharper disable ReplaceWithFieldKeyword

namespace VRCVideoCacher.Models;

public partial class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    private readonly Color _errorColor = Color.Parse("#CF6679");
    private readonly Color _warnColor = Color.Parse("#FFB74D");
    private readonly Color _infoColor = Color.Parse("#81C784");
    private readonly Color _debugColor = Color.Parse("#64B5F6");
    private readonly Color _stdColor = Color.Parse("#FFFFFF");

    public Color LevelColor => Level switch
    {
        "ERR" or "FTL" => _errorColor,
        "WRN" => _warnColor,
        "INF" => _infoColor,
        "DBG" => _debugColor,
        _ => _stdColor
    };

    // Extract clickable URL from message (YouTube, custom domains, etc.)
    // Excludes localhost, googlevideo, and other internal URLs
    private string? _clickableUrl;
    public string? ClickableUrl => _clickableUrl ??= ExtractClickableUrl();

    public bool HasClickableUrl => !string.IsNullOrEmpty(ClickableUrl);

    private string? ExtractClickableUrl()
    {
        var match = UrlRegex().Match(Message);
        if (!match.Success)
            return null;

        var url = match.Value;

        // Exclude internal/technical URLs
        if (url.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("127.0.0.1") ||
            url.Contains("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("videoplayback", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return url;
    }

    [GeneratedRegex(@"https?://[^\s""<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
