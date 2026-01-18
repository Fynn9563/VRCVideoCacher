# Changelog

## 2026.1.19
- Fix custom domain folder naming (use `vr-m.net` instead of `vr-m_net`)
- Add VRCX auto-start toggle in Settings UI

## 2026.1.18
- Merge UI project into main VRCVideoCacher project (single executable)
- Add 360p resolution option
- Retry without AVPro if prefetch fails
- Add mutex to prevent multiple instances
- Add custom domain caching support (`CacheCustomDomains` config)
- Add cache clearing on exit (`ClearYouTubeCacheOnExit`, `ClearPyPyDanceCacheOnExit`, `ClearVRDancingCacheOnExit`, `ClearCustomDomainsOnExit`)
- Add `avproOverride` to force AVPro mode for all requests
- Add `ytdlArgsOverride` to completely override yt-dlp arguments
- Organize cached videos into subdirectories by type (YouTube/, PyPyDance/, VRDancing/, CustomDomains/)
- Add category badges in cache browser UI
- Add UI settings for all new config options
- Update auto-updater to use Fynn9563 fork releases
- Add `PreCacheUrls` setting to UI with support for direct video URLs (YouTube, Vimeo, etc.)
- Fix "Open on YouTube" button for custom domain videos
- Add category filtering dropdown in cache browser
- Add video thumbnails for custom domain videos (extracted via FFmpeg)
- Add music icon for audio-only cached files
- Auto-refresh cache browser when video downloads complete
- Cache audio-only detection to avoid repeated FFmpeg runs
- Fix custom domain streaming URLs (m3u8/mpd) - skip yt-dlp and use direct URL

## 2025.11.24
- UI branch with Avalonia-based graphical interface
- Cache browser with thumbnails and video metadata
- Settings UI for all configuration options
- Download queue viewer
- Log viewer

## 2025.11.21
- Version bump

## 2025.11.15
- Fix autostart shortcut path updater
- Updater use absolute path
- Save config after edit
- Custom Resonite path support
- Simple setup with defaults

## 2025.11.8
- Add prefetching for YouTube to fix playback issues
- Ensure YouTube resolve delay runs when using third-party resolvers

## 2025.11.5
- PyPyDance error handling improvements
- Handle caching multiple formats for same video

## 2025.10.3
- Fix Linux updater not chmodding
- Add bypass for "VFI - Cinema" URLs

## 2025.9.29
- Auto remove readonly attribute when VRCVideoCacher isn't running
- Update block list behavior

## 2025.8.6
- SupportedOSPlatform attributes

## 2025.7.16
- Bug fixes

## 2025.7.14
- Bug fixes

## 2025.5.18
- Bug fixes

## 2025.5.14
- Bug fixes

## 2025.5.12
- Bug fixes

## 2025.5.9
- Bug fixes

## 2025.5.7
- Bug fixes

## 2025.4.21
- Bug fixes

## 2025.1.8
- Initial versioned release

## 2024.12.9
- Early release

## 2024.11.27
- Initial release
