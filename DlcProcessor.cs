using COM3D2_DLC_Batcher.Helpers;
using COM3D2_DLC_Batcher.Models;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace COM3D2_DLC_Batcher.Services;

public record ProgressInfo(int Value, int Max, string Message);

public record ScanResult(
    List<FileInfo> Archives,
    List<DirectoryInfo> Folders,
    long EstimatedBytes,
    int TotalItems);

public class DlcProcessor
{
    private readonly ArchiverService _archiver  = new();
    private readonly DlcFlattener   _flattener = new();

    public string ArchiverDisplayName => _archiver.DisplayName;

    public void ResetSession() => _flattener.ResetSession();

    public Task CheckGameVersionAsync(GameProfile profile, string destPath, Action<string, string> log)
    {
        return Task.Run(() =>
        {
            log("[PHASE 0] Checking game version...", "Yellow");
            var gameRoot    = Path.GetDirectoryName(destPath)!;
            var gameLstPath = Path.Combine(gameRoot, "update.lst");

            if (!File.Exists(gameLstPath))
            {
                throw new InvalidOperationException(
                    $"update.lst not found in '{gameRoot}'.\nPlease select the correct game folder.");
            }

            var escaped    = Regex.Escape(profile.LauncherExe);
            var lstContent = File.ReadAllText(gameLstPath);
            if (!Regex.IsMatch(lstContent, $@"{escaped},[\d\.]+"))
                throw new InvalidOperationException(
                    $"{profile.LauncherExe} missing from update.lst.\nUpdate the game and try again.");

            var verLine = lstContent.Split('\n')
                .FirstOrDefault(l => Regex.IsMatch(l, $@"^{escaped},"))
                ?.Trim() ?? "";
            log($"  OK -- Version: {verLine}", "Lime");
        });
    }

    public Task<ScanResult> ScanSourceAsync(string sourcePath, string destPath, IProgress<ProgressInfo> progress, Action<string, string> log, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            log("[PHASE 1] Scanning source...", "Yellow");
            progress.Report(new(0, 1, "Scanning..."));

            bool sameDir = string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase);

            var existingBases = new HashSet<string>(
                Directory.GetDirectories(destPath).Select(Path.GetFileName)!,
                StringComparer.OrdinalIgnoreCase);

            var archives = Directory.GetFiles(sourcePath)
                .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".7z",  StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .Where(fi => !existingBases.Contains(Path.GetFileNameWithoutExtension(fi.Name)))
                .ToList();

            var folders = sameDir
                ? new List<DirectoryInfo>()
                : Directory.GetDirectories(sourcePath)
                    .Where(d => !existingBases.Contains(Path.GetFileName(d)))
                    .Select(d => new DirectoryInfo(d))
                    .ToList();

            long arcBytes = archives.Sum(a => a.Length);
            long fldBytes = 0;
            for (int fi = 0; fi < folders.Count; fi++)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new(fi, folders.Count, $"Calculating folder sizes... ({fi}/{folders.Count})"));
                fldBytes += DirSize(folders[fi]);
            }

            long estBytes  = (long)(arcBytes * 2.5) + fldBytes;
            int  totalItems = archives.Count + folders.Count;

            log($"  Archives to extract : {archives.Count}  ({FormatHelper.Bytes(arcBytes)} compressed)", "White");
            log($"  Folders to copy     : {folders.Count}  ({FormatHelper.Bytes(fldBytes)})", "White");
            log($"  Estimated space     : {FormatHelper.Bytes(estBytes)}", "Cyan");

            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(destPath)!);
                if (drive.AvailableFreeSpace < estBytes)
                    log($"  WARN: estimated {FormatHelper.Bytes(estBytes)}, available {FormatHelper.Bytes(drive.AvailableFreeSpace)}", "Orange");
                else
                    log($"  Free space: {FormatHelper.Bytes(drive.AvailableFreeSpace)}  -- OK", "Lime");
            }
            catch { log("  Cannot verify free space, proceeding.", "Orange"); }

            return new ScanResult(archives, folders, estBytes, totalItems);
        }, ct);
    }

    public async Task ExtractAndCopyAsync(string destPath, ScanResult scanResult, IProgress<ProgressInfo> progress, Action<string, string> log, CancellationToken ct)
    {
        if (scanResult.TotalItems == 0)
        {
            log("  No new items to extract/copy.", "Orange");
            return;
        }

        if (scanResult.Archives.Count > 0)
        {
            log($"[PHASE 2] Extracting archives (tool: {_archiver.Kind})...", "Yellow");
            int done = 0;
            foreach (var arc in scanResult.Archives)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new(done, scanResult.TotalItems, $"Extracting {done}/{scanResult.Archives.Count}: {arc.Name}"));
                log($"  Extracting: {arc.Name}", "White");
                var outDir = Path.Combine(destPath, arc.Name.Substring(0, arc.Name.Length - arc.Extension.Length));
                try
                {
                    bool ok = await _archiver.ExtractAsync(arc.FullName, outDir, ct);
                    log(ok ? $"    [OK] {arc.Name.Substring(0, arc.Name.Length - arc.Extension.Length)}"
                           : $"    [WARN] {arc.Name}: archiver returned error",
                        ok ? "Lime" : "Orange");
                }
                catch (Exception ex) { log($"    [ERROR] {arc.Name}: {ex.Message}", "Red"); }
                done++;
            }
        }

        if (scanResult.Folders.Count > 0)
        {
            log("[PHASE 2b] Copying folders...", "Yellow");
            
            long totalSize = scanResult.Folders.Sum(d => DirSize(d));
            long copiedSize = 0;

            int done = scanResult.Archives.Count;
            foreach (var fld in scanResult.Folders)
            {
                ct.ThrowIfCancellationRequested();
                log($"  Copying: {fld.Name}", "White");
                try
                {
                    var copyProgress = new Progress<long>(bytes =>
                    {
                        copiedSize += bytes;
                        progress.Report(new((int)(copiedSize / 1024), (int)(totalSize / 1024), $"Copying: {fld.Name} ({FormatHelper.Bytes(copiedSize)} / {FormatHelper.Bytes(totalSize)})"));
                    });
                    await CopyDirAsync(fld.FullName, Path.Combine(destPath, fld.Name), copyProgress, ct);
                    log($"    [OK] {fld.Name}", "Lime");
                }
                catch (Exception ex) { log($"    [ERROR] {fld.Name}: {ex.Message}", "Red"); }
                done++;
            }
        }
    }

    public async Task<(int Created, int Skipped, int Warnings)> ProcessDlcsAsync(GameProfile profile, string destPath, IProgress<ProgressInfo> progress, Action<string, string> log, Action<string>? showError, CancellationToken ct)
    {
        log("[PHASE 3] Flattening DLC...", "Yellow");
        progress.Report(new(0, 1, "Scanning DLC folders..."));

        var gameRoot    = Path.GetDirectoryName(destPath)!;
        var gameLstPath = Path.Combine(gameRoot, "update.lst");

        var destDirs = Directory.GetDirectories(destPath)
            .Select(d => new DirectoryInfo(d))
            .OrderBy(d => d.Name)
            .ToList();

        int created = 0, skipped = 0, warnings = 0;

        for (int di = 0; di < destDirs.Count; di++)
        {
            ct.ThrowIfCancellationRequested();
            var d = destDirs[di];
            progress.Report(new(di, destDirs.Count, $"Flattening: {d.Name}  ({di}/{destDirs.Count})"));

            await Task.Run(() =>
                ProcessDlcFolder(d.FullName, destPath, profile, gameRoot, gameLstPath,
                    log, showError, ref created, ref skipped, ref warnings), ct);
        }

        progress.Report(new(1, 1, "Done."));
        log("\n  SUMMARY", "White");
        log($"  Created : {created}", "Lime");
        log($"  Skipped : {skipped}", "DarkGray");
        log($"  Warnings: {warnings}  (check *_MISSING_REPORT.txt)",
            warnings > 0 ? "Red" : "Lime");

        return (created, skipped, warnings);
    }

    private void ProcessDlcFolder(
        string folderPath, string destPath,
        GameProfile profile,
        string gameRoot, string gameLstPath,
        Action<string, string> log, Action<string>? showError,
        ref int created, ref int skipped, ref int warnings)
    {
        var fName   = Path.GetFileName(folderPath)!;
        var dlcFile = Path.Combine(destPath, $"{profile.DlcPrefix}{fName}.dlc");

        if (File.Exists(dlcFile))
        {
            log($"[SKIP] {profile.DlcPrefix}{fName} already exists.", "DarkGray");
            skipped++;
            return;
        }

        var iniFiles  = Directory.GetFiles(folderPath, "update.ini", SearchOption.AllDirectories);
        var validDirs = new List<string>();
        var validLsts = new List<string>();

        foreach (var ini in iniFiles)
        {
            var content = File.ReadAllText(ini);
            if (!content.Contains(profile.TargetApp, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(profile.ExcludeStr) &&
                content.Contains(profile.ExcludeStr, StringComparison.OrdinalIgnoreCase)) continue;
            var lst = Path.Combine(Path.GetDirectoryName(ini)!, "update.lst");
            if (!File.Exists(lst)) continue;
            validDirs.Add(Path.GetDirectoryName(ini)!);
            validLsts.Add(lst);
        }

        if (validDirs.Count == 0)
        {
            log($"[INFO] Skipping '{fName}': not a valid DLC for this game version.", "Gray");
            return;
        }

        log($"[PROC] Processing: {profile.DlcPrefix}{fName}...", "White");

        bool ok = _flattener.Flatten(
            workDir:     validDirs[0],
            lstPath:     validLsts[0],
            dlcName:     fName,
            pathDst:     destPath,
            gameRoot:    gameRoot,
            gameLstPath: gameLstPath,
            launcherExe: profile.LauncherExe,
            log:         log,
            showError:   showError);

        if (!ok)
        {
            warnings++;
            log($"[SKIP]  {profile.DlcPrefix}{fName}.dlc not created (missing files, check report).", "Red");
            return;
        }

        var relPath = validDirs[0].Substring(destPath.Length).TrimStart('\\').Replace('\\', '/');
        File.WriteAllText(dlcFile, relPath, System.Text.Encoding.UTF8);
        log($"[OK]   {profile.DlcPrefix}{fName}.dlc  ->  {relPath}", "Lime");
        created++;
    }

    private static long DirSize(DirectoryInfo d)
    {
        try { return d.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }

    private static async Task CopyDirAsync(string src, string dst, IProgress<long> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
        {
            ct.ThrowIfCancellationRequested();
            var destFile = Path.Combine(dst, Path.GetFileName(f));
            using (var sourceStream = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            using (var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await sourceStream.CopyToAsync(destStream, 81920, ct);
            }
            progress.Report(new FileInfo(f).Length);
        }
        foreach (var d in Directory.GetDirectories(src))
        {
            ct.ThrowIfCancellationRequested();
            await CopyDirAsync(d, Path.Combine(dst, Path.GetFileName(d)), progress, ct);
        }
    }
}
