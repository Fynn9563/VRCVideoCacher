@echo off
REM Development build script.
REM
REM Builds with the DevRelease configuration, which appends "-dev" to the version and disables the
REM self-updater. Without that the test build would see the published release as newer and replace
REM itself with it mid-test.
REM
REM NOTE: this only isolates the BUILD. Config.json, CachedAssets, Utils and MetadataCache all live
REM under %%AppData%%\VRCVideoCacher, so a dev build shares them with an installed copy.

set "DEV_BUILD_DIR=DevBuild"

echo Cleaning previous dev build...
if exist %DEV_BUILD_DIR% (
    REM Only remove build artifacts
    del /q "%DEV_BUILD_DIR%\*.exe" 2>nul
    del /q "%DEV_BUILD_DIR%\*.pdb" 2>nul
    del /q "%DEV_BUILD_DIR%\youtube_cookies.txt" 2>nul
) else (
    mkdir %DEV_BUILD_DIR%
)

echo Building for Windows x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c DevRelease -r win-x64 -o %DEV_BUILD_DIR%

echo.
echo Build complete! Output: %DEV_BUILD_DIR%
echo Uses the same %%AppData%%\VRCVideoCacher data as an installed copy.
pause
