using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Text.RegularExpressions;

namespace COM3D2_DLC_Batcher.Services;

/// <summary>
/// Processes a single DLC's update.lst:
///   - share  -> copy to data\, rewrite as type 0
///   - type 0 -> move to data\  (or intercept launcher exe)
///   - other  -> pass through
/// Returns true if no files were missing.
/// </summary>
public class DlcFlattener
{
    // Session cache: shared across all DLCs in one run so the highest
    // installed exe version is always tracked correctly.
    private double _gameExeVerCache;

    public void ResetSession() => _gameExeVerCache = 0;

    public bool Flatten(
        string workDir,
        string lstPath,
        string dlcName,
        string pathDst,
        string gameRoot,
        string gameLstPath,
        string launcherExe,
        Action<string, string> log,
        Action<string>?        showError = null)
    {
        string[] lines = File.ReadAllLines(lstPath);
        if (lines.Length == 0) { log("    [SKIP] LST empty.", "Gray"); return true; }

        string dataDir   = Path.Combine(workDir, "data");
        string parentDir = Path.GetDirectoryName(workDir)!;
        List<string> newLines  = new List<string>();
        List<string> exeLines  = new List<string>();
        List<string> missing   = new List<string>();
        bool wasModified = false;
        
        Directory.CreateDirectory(dataDir);

        foreach (string rawLine in lines)
        {
            string line  = rawLine.TrimEnd();
            if (string.IsNullOrEmpty(line)) { newLines.Add(line); continue; }

            string[] parts = line.Split(',');
            string type  = parts[0].Trim();

            // ------------------------------------------------------------------
            //  share -> copy to data\ and rewrite as type 0
            // ------------------------------------------------------------------
            if (type == "share")
            {
                if (parts.Length < 3) { newLines.Add(line); continue; }

                string relSrc    = parts[1].Trim().Replace('/', '\\');
                string intDest   = parts[2].Trim().Replace('/', '\\').TrimStart('\\');

                bool useParent = relSrc.StartsWith("..\\", StringComparison.Ordinal);
                string srcPath   = useParent ? relSrc.Substring(3) : relSrc;
                string srcBase   = useParent ? parentDir : workDir;
                string fullSrc   = Path.Combine(srcBase, srcPath);
                string fullDst   = Path.Combine(dataDir, intDest);

                if (File.Exists(fullSrc))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullDst)!);
                    File.Copy(fullSrc, fullDst, overwrite: true);
                    log($"    [FIX-share] {intDest}", "Cyan");
                    string rest = parts.Length > 3 ? "," + string.Join(",", parts.Skip(3)) : "";
                    newLines.Add($"0,0,{intDest}{rest}");
                    wasModified = true;
                }
                else
                {
                    log($"    [MISSING]  {intDest}  (source: {fullSrc})", "Magenta");
                    missing.Add($"SHARE: {intDest} -- Source: {fullSrc}");
                    newLines.Add(line);
                }
                continue;
            }

            // ------------------------------------------------------------------
            //  type 0
            // ------------------------------------------------------------------
            if (type == "0")
            {
                if (parts.Length < 3) { newLines.Add(line); continue; }
                string intPath = parts[2].Trim().Replace('/', '\\');

                // Launcher exe interception: keep line in lst, collect for update block
                if (string.Equals(intPath, launcherExe, StringComparison.OrdinalIgnoreCase))
                {
                    exeLines.Add(line);
                    newLines.Add(line);
                    continue;
                }

                string srcPath = Path.Combine(workDir, intPath);
                string dstPath = Path.Combine(dataDir, intPath);
                bool dstExists = File.Exists(dstPath) || Directory.Exists(dstPath);

                if (Directory.Exists(srcPath))
                {
                    if (!dstExists)
                    {
                        string? dstParent = Path.GetDirectoryName(dstPath);
                        if (!string.IsNullOrEmpty(dstParent)) Directory.CreateDirectory(dstParent);
                        Directory.Move(srcPath, dstPath);
                        log($"    [MOV-DIR]   {intPath}", "White");
                        wasModified = true;
                    }
                }
                else if (File.Exists(srcPath))
                {
                    if (!dstExists)
                    {
                        string? dstParent = Path.GetDirectoryName(dstPath);
                        if (!string.IsNullOrEmpty(dstParent)) Directory.CreateDirectory(dstParent);
                        File.Move(srcPath, dstPath);
                        log($"    [MOV-FILE]  {intPath}", "White");
                        wasModified = true;
                    }
                }
                else if (!dstExists)
                {
                    log($"    [MISSING]  {intPath}", "Magenta");
                    missing.Add($"TYPE0: {intPath}");
                }

                newLines.Add(line);
                continue;
            }

            // Other types: pass through
            newLines.Add(line);
        }

        // ------------------------------------------------------------------
        //  Exe update block
        // ------------------------------------------------------------------
        if (exeLines.Count > 0)
        {
            // DLC version: 0,0,LauncherExe,size,hash,VERSION
            string[] ep     = exeLines[0].Split(',');
            double dlcVer = ep.Length >= 6 &&
                            double.TryParse(ep[5],
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var d)
                            ? d : 0;

            if (dlcVer <= 0)
            {
                log("    [EXE]  DLC version unreadable, skip.", "Orange");
            }
            else
            {
                if (_gameExeVerCache <= 0 && File.Exists(gameLstPath))
                    _gameExeVerCache = ReadExeVerFromLst(gameLstPath, launcherExe);

                if (_gameExeVerCache <= 0)
                    log("    [EXE]  Game version unreadable from lst, skip.", "Orange");
                else if (dlcVer > _gameExeVerCache)
                {
                    string exeSrc = Path.Combine(workDir, launcherExe);
                    if (!File.Exists(exeSrc))
                        exeSrc = Path.Combine(dataDir, launcherExe);

                    if (!File.Exists(exeSrc))
                    {
                        log($"    [EXE]  {launcherExe} not found in DLC, skip.", "Orange");
                    }
                    else
                    {
                        log($"    [EXE]  Updating v{_gameExeVerCache} -> v{dlcVer}", "Yellow");
                        try
                        {
                            File.Copy(exeSrc, Path.Combine(gameRoot, launcherExe), overwrite: true);
                            UpdateExeVerInLst(gameLstPath, launcherExe, dlcVer);
                            log($"    [EXE]  Game update.lst updated ({launcherExe},{dlcVer}).", "Yellow");
                            _gameExeVerCache = dlcVer;
                            wasModified = true;
                        }
                        catch (Exception ex)
                        {
                            log($"    [EXE]  ERROR: cannot replace {launcherExe}: {ex.Message}", "Red");
                            log("    [EXE]  Close the launcher and run again.", "Red");
                            showError?.Invoke(
                                $"Cannot update {launcherExe} (v{_gameExeVerCache} -> v{dlcVer}).\n\n{ex.Message}\n\nClose the launcher and run again.");
                        }
                    }
                }
                // dlcVer <= _gameExeVerCache: no update needed, silent
            }
        }

        // ------------------------------------------------------------------
        //  Atomic LST save
        // ------------------------------------------------------------------
        string tmp = lstPath + ".tmp";
        File.WriteAllLines(tmp, newLines);
        File.Delete(lstPath);
        File.Move(tmp, lstPath);

        // ------------------------------------------------------------------
        //  Missing report
        // ------------------------------------------------------------------
        if (missing.Count > 0)
        {
            string reportPath = Path.Combine(pathDst, $"{dlcName}_MISSING_REPORT.txt");
            File.WriteAllLines(reportPath, missing);
            log($"    [WARN] {missing.Count} missing files -> {dlcName}_MISSING_REPORT.txt", "Red");
            return false;
        }

        if (wasModified)
        {
            log("    [OK]  LST updated with file moves.", "Lime");
        }
        else
        {
            log("    [OK]  LST integrity checked, no changes needed.", "Gray");
        }
        return true;
    }

    // ----------------------------------------------------------------------

    private static double ReadExeVerFromLst(string lstPath, string exeName)
    {
        string escaped = Regex.Escape(exeName);
        string? line = File.ReadLines(lstPath)
            .FirstOrDefault(l => Regex.IsMatch(l, $@"^{escaped},"));
        if (line is null) return 0;
        string[] parts = line.Split(',');
        return parts.Length >= 2 &&
               double.TryParse(parts[1],
                   System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out var v)
               ? v : 0;
    }

    private static void UpdateExeVerInLst(string lstPath, string exeName, double newVer)
    {
        string escaped = Regex.Escape(exeName);
        string[] lines = File.ReadAllLines(lstPath);
        for (int i = 0; i < lines.Length; i++)
            if (Regex.IsMatch(lines[i], $@"^{escaped},"))
                lines[i] = $"{exeName},{newVer}";
        string tmp = lstPath + ".tmp";
        File.WriteAllLines(tmp, lines);
        File.Delete(lstPath);
        File.Move(tmp, lstPath);
    }
}
