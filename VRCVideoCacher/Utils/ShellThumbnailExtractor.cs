using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Extracts video thumbnails using OS-level APIs (no ffmpeg dependency).
/// Windows: IShellItemImageFactory COM API (same thumbnails as Explorer).
/// Linux: Freedesktop thumbnail cache (~/.cache/thumbnails/).
/// </summary>
public static class ShellThumbnailExtractor
{
    /// <summary>
    /// Tries to extract a thumbnail for a video file using OS shell APIs.
    /// Returns the saved thumbnail path, or null if unavailable.
    /// </summary>
    public static string? TryExtract(string videoId, string filePath, string outputDir)
    {
        try
        {
            // Check for existing cached thumbnail (any supported format)
            var existingBmp = Path.Combine(outputDir, $"{videoId}.bmp");
            if (File.Exists(existingBmp)) return existingBmp;

            var existingPng = Path.Combine(outputDir, $"{videoId}.png");
            if (File.Exists(existingPng)) return existingPng;

            if (OperatingSystem.IsWindows())
                return TryExtractWindows(filePath, existingBmp);

            if (OperatingSystem.IsLinux())
                return TryExtractLinux(filePath, existingPng);
        }
        catch { }

        return null;
    }

    #region Windows - IShellItemImageFactory

    private static string? TryExtractWindows(string filePath, string outputPath)
    {
        if (!OperatingSystem.IsWindows()) return null;

        IntPtr hBitmap = IntPtr.Zero;
        object? shellItem = null;

        try
        {
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            int hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iid, out shellItem);
            if (hr != 0 || shellItem is not IShellItemImageFactory factory)
                return null;

            var size = new NativeSize { Width = 256, Height = 256 };
            hr = factory.GetImage(size, 0x0, out hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
                return null;

            SaveHBitmapAsBmp(hBitmap, outputPath);
            return outputPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (shellItem != null)
                Marshal.ReleaseComObject(shellItem);
        }
    }

    private static void SaveHBitmapAsBmp(IntPtr hBitmap, string outputPath)
    {
        GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out var bmp);

        var width = bmp.bmWidth;
        var height = bmp.bmHeight;
        var stride = ((width * 32 + 31) / 32) * 4;
        var imageSize = stride * Math.Abs(height);

        var bi = new BITMAPINFOHEADER
        {
            biSize = 40,
            biWidth = width,
            biHeight = -Math.Abs(height), // negative = top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
            biSizeImage = imageSize
        };

        var pixels = new byte[imageSize];
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            GetDIBits(hdc, hBitmap, 0, (uint)Math.Abs(height), pixels, ref bi, 0);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // BITMAPFILEHEADER (14 bytes)
        var fileSize = 14 + 40 + imageSize;
        bw.Write((ushort)0x4D42);  // 'BM'
        bw.Write(fileSize);
        bw.Write(0);               // reserved
        bw.Write(14 + 40);         // offset to pixel data

        // BITMAPINFOHEADER (40 bytes)
        bw.Write(40);
        bw.Write(width);
        bw.Write(-Math.Abs(height));
        bw.Write((ushort)1);       // planes
        bw.Write((ushort)32);      // bits per pixel
        bw.Write(0);               // compression (BI_RGB)
        bw.Write(imageSize);
        bw.Write(0);               // X pels per meter
        bw.Write(0);               // Y pels per meter
        bw.Write(0);               // colors used
        bw.Write(0);               // colors important

        bw.Write(pixels);
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    private static extern bool GetObject(IntPtr hObject, int nCount, out BITMAP lpObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    #endregion

    #region Linux - Freedesktop thumbnail cache

    private static string? TryExtractLinux(string filePath, string outputPath)
    {
        if (!OperatingSystem.IsLinux()) return null;

        try
        {
            var fileUri = new Uri(filePath).AbsoluteUri;
            var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(fileUri));
            var hashStr = Convert.ToHexStringLower(hashBytes);

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var largePath = Path.Combine(home, ".cache", "thumbnails", "large", $"{hashStr}.png");
            var normalPath = Path.Combine(home, ".cache", "thumbnails", "normal", $"{hashStr}.png");

            // Check if thumbnail already exists in freedesktop cache
            var sourcePath = File.Exists(largePath) ? largePath
                           : File.Exists(normalPath) ? normalPath
                           : null;

            // If not cached, request generation via D-Bus Thumbnailer1
            if (sourcePath == null)
            {
                RequestDbusThumbnail(fileUri);

                // Wait briefly for the thumbnailer to generate it
                for (var i = 0; i < 20; i++)
                {
                    Thread.Sleep(250);
                    if (File.Exists(largePath)) { sourcePath = largePath; break; }
                    if (File.Exists(normalPath)) { sourcePath = normalPath; break; }
                }
            }

            if (sourcePath == null) return null;

            File.Copy(sourcePath, outputPath, true);
            return outputPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Requests thumbnail generation via the freedesktop Thumbnailer1 D-Bus interface.
    /// Uses gdbus CLI to avoid adding a D-Bus library dependency.
    /// </summary>
    private static void RequestDbusThumbnail(string fileUri)
    {
        try
        {
            var mimeType = DetectMimeTypeForUri(fileUri);
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "gdbus",
                    Arguments = $"call --session --dest org.freedesktop.thumbnails.Thumbnailer1 " +
                                $"--object-path /org/freedesktop/thumbnails/Thumbnailer1 " +
                                $"--method org.freedesktop.thumbnails.Thumbnailer1.Queue " +
                                $"\"['{fileUri}']\" \"['{mimeType}']\" \"large\" \"default\" 0",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(10000);
        }
        catch { }
    }

    private static string DetectMimeTypeForUri(string fileUri)
    {
        var lower = fileUri.ToLowerInvariant();
        if (lower.EndsWith(".mp4")) return "video/mp4";
        if (lower.EndsWith(".webm")) return "video/webm";
        if (lower.EndsWith(".mkv")) return "video/x-matroska";
        if (lower.EndsWith(".avi")) return "video/x-msvideo";
        if (lower.EndsWith(".mov")) return "video/quicktime";
        if (lower.EndsWith(".mp3")) return "audio/mpeg";
        if (lower.EndsWith(".flac")) return "audio/flac";
        if (lower.EndsWith(".ogg")) return "audio/ogg";
        if (lower.EndsWith(".wav")) return "audio/wav";
        return "video/mp4";
    }

    #endregion
}
