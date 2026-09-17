using System.Text;
using System.Text.RegularExpressions;

namespace VRCVideoCacher.Services.Vrm;

/// <summary>
/// vr-m.net hides the video title in plain sight: the HLS segment URLs live under a directory whose
/// name is the title with every space replaced by a separator token. Decoding that directory name
/// gives the title without any catalogue lookup, which matters because the API is index-based and
/// its indices are positions in a live result set rather than stable ids.
///
/// Example segment URL from /p/18104.m3u8:
///   https://&lt;cdn&gt;/0/y25/w50/502356XucIl82JTheXucIl82JSuperXucIl82JMarioXucIl82JBrosXucIl82JMovieXucIl82J2023XucIl82J4K/&lt;file&gt;.html
/// which decodes to tmdbId 502356, "The Super Mario Bros Movie", year 2023, tag 4K.
/// </summary>
public static class VrmTitleDecoder
{
    // Separators seen standing in for a space, longest first so matching is unambiguous.
    private static readonly Regex SpacePattern = new("XucIl82J|0x0x0", RegexOptions.Compiled);

    // Some names are wrapped in a marker, e.g. "zxcScary Movie 5zxc".
    private static readonly string[] WrapperMarkers = ["zxc"];

    // Trailing markers that describe the encode rather than the work itself.
    private static readonly HashSet<string> QualityTags = new(StringComparer.OrdinalIgnoreCase)
        { "4K", "8K", "HD", "FHD", "UHD", "3D", "SBS", "OU", "HDR", "DV", "REMUX" };

    // Language track markers, kept separate because they can sit mid-name.
    private static readonly HashSet<string> TrackTags = new(StringComparer.OrdinalIgnoreCase)
        { "SUB", "DUB", "SUBBED", "DUBBED" };

    private static readonly Regex YearPattern = new(@"^(19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly Regex GluedTmdbId = new(@"^(\d{3,})_(.*)$", RegexOptions.Compiled);
    private static readonly Regex BareTmdbId = new(@"^\d{3,}$", RegexOptions.Compiled);
    private static readonly Regex CompactSeasonEpisode = new(@"^s(\d+)e(?:p)?(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ShortSeason = new(@"^s(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ShortEpisode = new(@"^ep?(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ObfuscatedFirstToken = new(@"^\d+[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex FileExtension = new(@"\.[a-z0-9]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "Episode" is sometimes glued to the preceding word ("Seki-kunEpisode 13", "DragonSEpisode 5").
    private static readonly Regex StandaloneEpisode = new(@"\bS?Episode\b", RegexOptions.Compiled);
    private static readonly Regex GluedEpisode = new(@"([a-z\d])S?Episode", RegexOptions.Compiled);
    private static readonly Regex GluedSeason = new(@"([a-z\d])S(\d+)\b", RegexOptions.Compiled);

    public sealed record DecodedTitle
    {
        public required string Title { get; init; }
        /// <summary>
        /// The leading numeric token, when there is one. Usually a TMDB id, but not always: padded
        /// values like "111111111339713" occur, so this is wider than an int and may be meaningless.
        /// </summary>
        public long? TmdbId { get; init; }
        public int? Year { get; init; }
        public int? Season { get; init; }
        public int? Episode { get; init; }
        /// Encode and language markers such as 4K, 3D, Dub.
        public required IReadOnlyList<string> Tags { get; init; }
        /// The raw directory name the title was decoded from, kept for auditing.
        public required string Encoded { get; init; }
        /// A single display string, e.g. "Undead Unluck S1E10 [Sub]".
        public required string Display { get; init; }
    }

    private static string StripWrappers(string name)
    {
        var result = name;
        foreach (var marker in WrapperMarkers)
        {
            if (result.StartsWith(marker, StringComparison.OrdinalIgnoreCase) &&
                result.EndsWith(marker, StringComparison.OrdinalIgnoreCase) &&
                result.Length > marker.Length * 2)
            {
                result = result[marker.Length..^marker.Length];
            }
        }
        return result;
    }

    private static string UnglueEpisode(string name) =>
        GluedSeason.Replace(
            GluedEpisode.Replace(
                StandaloneEpisode.Replace(name, "Episode"),
                "$1 Episode"),
            "$1 S$2");

    private static List<string> Tokenise(string encoded) =>
        UnglueEpisode(StripWrappers(SpacePattern.Replace(encoded, " ")))
            .Split(' ')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0 && part != "-")
            .ToList();

    /// Pulls "S1 EP10", "Season 1 Episode 03" and bare "Episode 7" out of the token list.
    private static (List<string> Tokens, int? Season, int? Episode) ExtractSeasonEpisode(List<string> input)
    {
        var tokens = new List<string>();
        int? season = null;
        int? episode = null;

        for (var index = 0; index < input.Count; index++)
        {
            var token = input[index];
            var next = index + 1 < input.Count ? input[index + 1] : null;

            // Every parse below is a TryParse: the digit runs in these patterns are unbounded, and a
            // token too large to fit is kept as part of the title rather than throwing.
            var compact = CompactSeasonEpisode.Match(token);
            if (compact.Success &&
                int.TryParse(compact.Groups[1].Value, out var compactSeason) &&
                int.TryParse(compact.Groups[2].Value, out var compactEpisode))
            {
                season = compactSeason;
                episode = compactEpisode;
                continue;
            }

            var shortSeason = ShortSeason.Match(token);
            if (shortSeason.Success && int.TryParse(shortSeason.Groups[1].Value, out var seasonValue))
            {
                season = seasonValue;
                continue;
            }

            var shortEpisode = ShortEpisode.Match(token);
            if (shortEpisode.Success && int.TryParse(shortEpisode.Groups[1].Value, out var episodeValue))
            {
                episode = episodeValue;
                continue;
            }

            var lower = token.ToLowerInvariant();
            if ((lower is "season" or "episode") && next is not null && next.Length > 0 &&
                next.All(char.IsDigit) && int.TryParse(next, out var value))
            {
                if (lower == "season") season = value;
                else episode = value;
                index++;
                continue;
            }

            tokens.Add(token);
        }

        return (tokens, season, episode);
    }

    private static long? ParseIdOrNull(string token) =>
        long.TryParse(token, out var value) ? value : null;

    public static DecodedTitle DecodeEncodedName(string encoded)
    {
        var parts = Tokenise(encoded);

        long? tmdbId = null;
        if (parts.Count > 1)
        {
            // Either a bare leading id ("502356 The Super ...") or one glued with an underscore
            // ("0457146_Squidbillies"). The token is stripped from the title either way, including when
            // it is too long to be a real id, because it is never part of the name.
            var glued = GluedTmdbId.Match(parts[0]);
            if (glued.Success)
            {
                tmdbId = ParseIdOrNull(glued.Groups[1].Value);
                parts[0] = glued.Groups[2].Value;
                if (parts[0].Length == 0) parts.RemoveAt(0);
            }
            else if (BareTmdbId.IsMatch(parts[0]))
            {
                tmdbId = ParseIdOrNull(parts[0]);
                parts.RemoveAt(0);
            }
        }

        var tags = new List<string>();
        parts = parts.Where(token =>
        {
            if (!TrackTags.Contains(token)) return true;
            tags.Add(token);
            return false;
        }).ToList();

        var (remaining, season, episode) = ExtractSeasonEpisode(parts);
        parts = remaining;

        // Strip trailing encode markers, then the year, then any marker that was sitting behind the
        // year ("<title> 4K 2023" and "<title> 2023 4K" both occur).
        while (parts.Count > 1 && QualityTags.Contains(parts[^1]))
        {
            tags.Add(parts[^1]);
            parts.RemoveAt(parts.Count - 1);
        }

        int? year = null;
        for (var index = parts.Count - 1; index >= 0; index--)
        {
            if (!YearPattern.IsMatch(parts[index]) || !int.TryParse(parts[index], out var yearValue)) continue;
            year = yearValue;
            parts.RemoveAt(index);
            break;
        }

        while (parts.Count > 1 && QualityTags.Contains(parts[^1]))
        {
            tags.Add(parts[^1]);
            parts.RemoveAt(parts.Count - 1);
        }

        var title = string.Join(' ', parts);
        var distinctTags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var display = new StringBuilder(title);
        if (season is not null && episode is not null) display.Append($" S{season}E{episode}");
        else if (episode is not null) display.Append($" E{episode}");
        else if (season is not null) display.Append($" S{season}");
        if (year is not null) display.Append($" ({year})");
        if (distinctTags.Count > 0) display.Append($" [{string.Join(' ', distinctTags)}]");

        return new DecodedTitle
        {
            Title = title,
            TmdbId = tmdbId,
            Year = year,
            Season = season,
            Episode = episode,
            Tags = distinctTags,
            Encoded = encoded,
            Display = display.ToString(),
        };
    }

    /// <summary>
    /// A minority of entries have an obfuscated directory name, where each word gains a trailing
    /// letter and a leading number is moved to the front: "The Bad Guys 2" is stored as
    /// "2Thed Badd Guysd". The segment filename keeps the clean name, so a title whose first token is
    /// digits glued to letters is taken from there instead.
    /// </summary>
    private static bool LooksObfuscated(DecodedTitle decoded)
    {
        var firstToken = decoded.Title.Split(' ').FirstOrDefault() ?? string.Empty;
        return ObfuscatedFirstToken.IsMatch(firstToken);
    }

    /// <summary>
    /// The segment filename is "&lt;name tokens joined by a separator&gt;&lt;segmentIndex&gt;.html".
    /// Only the first segment is ever read, so its index is always the single digit 0, which is
    /// trimmed back off before decoding.
    /// </summary>
    private static string? EncodedNameFromFilename(string filename)
    {
        var withoutExtension = FileExtension.Replace(filename, string.Empty);
        return withoutExtension.EndsWith('0') ? withoutExtension[..^1] : null;
    }

    /// <summary>
    /// Pulls the encoded name out of a segment URL. The title is carried twice: once as the directory
    /// the segments live in, and once as the segment filename.
    /// </summary>
    public static DecodedTitle? DecodeSegmentUrl(string segmentUrl)
    {
        if (!Uri.TryCreate(segmentUrl, UriKind.Absolute, out var uri))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return null;

        var directory = Uri.UnescapeDataString(segments[^2]);
        var filename = Uri.UnescapeDataString(segments[^1]);

        var fromDirectory = DecodeEncodedName(directory);

        if (LooksObfuscated(fromDirectory))
        {
            var encodedFilename = EncodedNameFromFilename(filename);
            if (encodedFilename is not null)
            {
                var fromFilename = DecodeEncodedName(encodedFilename);
                if (fromFilename.Title.Length > 0 && !LooksObfuscated(fromFilename))
                {
                    // Keep the directory form as the audit trail, since that is the id-bearing one.
                    return fromFilename with { Encoded = $"{directory} | {encodedFilename}" };
                }
            }
        }

        return fromDirectory.Title.Length > 0 ? fromDirectory : null;
    }

    /// Finds the first media segment URL in an HLS manifest.
    public static string? FirstSegmentUrl(string manifest)
    {
        foreach (var rawLine in manifest.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            return line;
        }
        return null;
    }

    public static DecodedTitle? DecodeManifest(string manifest)
    {
        var segment = FirstSegmentUrl(manifest);
        return segment is null ? null : DecodeSegmentUrl(segment);
    }

    /// <summary>
    /// Titles routinely carry characters Windows rejects in a filename ("Mission: Impossible"), and a
    /// name ending in a dot or space is silently mangled by the shell.
    /// </summary>
    public static string ToSafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) ? ' ' : c).ToArray());

        // Collapse the runs the substitution leaves behind, then strip what Windows trims anyway.
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim().TrimEnd('.').Trim();

        // Guard against a pathological title producing a name no filesystem will take.
        return cleaned.Length > 150 ? cleaned[..150].Trim() : cleaned;
    }
}
