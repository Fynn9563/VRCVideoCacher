@echo off
REM Development build script - builds to a separate folder without affecting config/cache

set "DEV_BUILD_DIR=DevBuild"

echo Cleaning previous dev build...
if exist %DEV_BUILD_DIR% (
    REM Only remove exe and dll files, preserve Config.json and CachedAssets
    del /q "%DEV_BUILD_DIR%\*.exe" 2>nul
    del /q "%DEV_BUILD_DIR%\*.dll" 2>nul
    del /q "%DEV_BUILD_DIR%\*.pdb" 2>nul
    del /q "%DEV_BUILD_DIR%\*.json" 2>nul
    del /q "%DEV_BUILD_DIR%\*.deps.json" 2>nul
    del /q "%DEV_BUILD_DIR%\*.runtimeconfig.json" 2>nul
) else (
    mkdir %DEV_BUILD_DIR%
)

echo Building for Windows x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r win-x64 -o %DEV_BUILD_DIR%

echo.
echo Build complete! Output: %DEV_BUILD_DIR%
echo Config.json and CachedAssets folder are preserved if they existed.
pause
