# Changelog

## [2.4.0] - 2026-01-22

### Added
- Clickable URL button in log viewer for YouTube and custom domain URLs (excludes localhost and googlevideo.com)
- Cookie status display in Settings (shows logged-in YouTube account email)
- Browser-specific URL opening in cookie setup wizard (opens in Chrome/Firefox based on selection)

## [2.3.1] - 2026-01-21

### Fixed
- VRCX auto-start toggle applying immediately instead of waiting for save

## [2.3.0] - 2026-01-21

### Added
- File logging (logs/VRCVideoCacher.log with 5-day retention)

## [2.2.0] - 2026-01-21

### Changed
- Switch to semantic versioning (from date-based versioning)

### Fixed
- build-dev.bat now preserves Config.json

## [2026.1.21] - 2026-01-20

### Added
- Transition logic for semver versioning (next release will be `2.2.0`)
- Resonite mode support (from EllyVR/UI branch)
- BlockRedirect setting for blocked URLs

### Changed
- Moved utility classes to Utils/ folder
- Removed ytdlDelay setting

## [2026.1.20] - 2026-01-18

### Fixed
- Update loop caused by version mismatch in 2026.1.19 release
- Updater failing when backup file already exists (now uses versioned backup filename)

## [2026.1.19] - 2026-01-18

### Added
- VRCX auto-start toggle in Settings UI

### Fixed
- Custom domain folder naming (use `vr-m.net` instead of `vr-m_net`)

## [2026.1.18] - 2026-01-18

### Added
- Merged UI project into main VRCVideoCacher project (single executable)
- 360p resolution option
- Custom domain caching support (`CacheCustomDomains` config)
- Cache clearing on exit (`ClearYouTubeCacheOnExit`, `ClearPyPyDanceCacheOnExit`, `ClearVRDancingCacheOnExit`, `ClearCustomDomainsOnExit`)
- `avproOverride` to force AVPro mode for all requests
- `ytdlArgsOverride` to completely override yt-dlp arguments
- Category badges in cache browser UI
- UI settings for all new config options
- `PreCacheUrls` setting with support for direct video URLs
- Category filtering dropdown in cache browser
- Video thumbnails for custom domain videos (extracted via FFmpeg)
- Music icon for audio-only cached files
- Auto-refresh cache browser when video downloads complete
- Mutex to prevent multiple instances

### Changed
- Organized cached videos into subdirectories by type (YouTube/, PyPyDance/, VRDancing/, CustomDomains/)
- Updated auto-updater to use Fynn9563 fork releases
- Cache audio-only detection to avoid repeated FFmpeg runs

### Fixed
- "Open on YouTube" button for custom domain videos
- Custom domain streaming URLs (m3u8/mpd) - skip yt-dlp and use direct URL
- Retry without AVPro if prefetch fails

## [2025.11.24] - 2025-11-24

### Added
- Avalonia-based graphical interface
- Cache browser with thumbnails and video metadata
- Settings UI for all configuration options
- Download queue viewer
- Log viewer

## [2025.11.15] - 2025-11-15

### Added
- Custom Resonite path support
- Simple setup with defaults

### Fixed
- Autostart shortcut path updater
- Updater now uses absolute path
- Config saves after edit

## [2025.11.8] - 2025-11-08

### Added
- Prefetching for YouTube to fix playback issues

### Fixed
- YouTube resolve delay now runs when using third-party resolvers

## [2025.11.5] - 2025-11-11

### Fixed
- PyPyDance error handling improvements
- Handle caching multiple formats for same video

## [2025.10.3] - 2025-10-03

### Added
- Bypass for "VFI - Cinema" URLs

### Fixed
- Linux updater not setting executable permission

## [2025.9.29] - 2025-09-29

### Changed
- Auto remove readonly attribute when VRCVideoCacher isn't running
- Updated block list behavior

## [2025.1.8] - 2025-01-08

### Added
- Initial versioned release

## [2024.11.27] - 2024-12-03

### Added
- Initial release

---

For older releases, see [GitHub Releases](https://github.com/Fynn9563/VRCVideoCacher/releases).
