using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace COM3D2_DLC_Batcher.Services;

public enum ArchiverKind { SevenZip, WinRAR, Native }

public class ArchiverService
{
    public ArchiverKind Kind { get; }
    public string?      Path { get; }

    private static readonly string[] SevenZipPaths =
    [
        @"C:\Program Files\7-Zip\7z.exe",
        @"C:\Program Files (x86)\7-Zip\7z.exe"
    ];
    private static readonly string[] WinRarPaths =
    [
        @"C:\Program Files\WinRAR\WinRAR.exe",
        @"C:\Program Files (x86)\WinRAR\WinRAR.exe"
    ];

    public ArchiverService()
    {
        var sz = SevenZipPaths.FirstOrDefault(File.Exists);
        if (sz != null) { Kind = ArchiverKind.SevenZip; Path = sz; return; }
        var wr = WinRarPaths.FirstOrDefault(File.Exists);
        if (wr != null) { Kind = ArchiverKind.WinRAR;  Path = wr; return; }
        Kind = ArchiverKind.Native;
    }

    public async Task<bool> ExtractAsync(string archive, string destDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        return Kind switch
        {
            ArchiverKind.SevenZip => await RunProcessAsync(Path!, $"x \"{archive}\" -o\"{destDir}\" -bso0 -bsp0 -y", ct) == 0,
            ArchiverKind.WinRAR  => await RunProcessAsync(Path!, $"x -y \"{archive}\" \"{destDir}\\\"", ct) <= 1,
            _                    => await NativeExtractAsync(archive, destDir, ct)
        };
    }

    private static async Task<int> RunProcessAsync(string exe, string args, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            CreateNoWindow  = true,
            UseShellExecute = false
        })!;
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    private static async Task<bool> NativeExtractAsync(string archive, string destDir, CancellationToken ct)
    {
        var ext = System.IO.Path.GetExtension(archive).ToLowerInvariant();
        if (ext != ".zip")
            throw new NotSupportedException($"{ext} requires 7-Zip or WinRAR (not found).");
        await Task.Run(() => ZipFile.ExtractToDirectory(archive, destDir, overwriteFiles: true), ct);
        return true;
    }

    public string DisplayName => Kind switch
    {
        ArchiverKind.SevenZip => $"7-Zip  ({Path})",
        ArchiverKind.WinRAR  => $"WinRAR ({Path})",
        _                    => "Native (.zip only)"
    };
}
