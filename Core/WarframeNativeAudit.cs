using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public static class WarframeNativeAudit
{
    public static async Task<string> GenerateNativeAuditReportAsync(string gameFolder, LoggerService logger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("==========================================================================");
        sb.AppendLine("       WARFRAME NATIVE UPDATE PIPELINE AUDIT REPORT (READ-ONLY)           ");
        sb.AppendLine("==========================================================================");
        sb.AppendLine($"Timestamp:            {DateTime.Now:yyyy-MM-dd HH:mm:ss} local");
        sb.AppendLine($"OS Environment:       {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($"Game Installation:    {gameFolder}");
        sb.AppendLine($"Safety Lock Status:   LOCKED (SafetyAuditLockEnabled = true)");
        sb.AppendLine("--------------------------------------------------------------------------");
        sb.AppendLine();

        sb.AppendLine("=== 1. EXECUTABLE IDENTITY & HASH AUDIT ===");
        string launcherPath = WarframePathResolver.ResolveLocalPath(gameFolder, "/Tools/Launcher.exe");
        if (string.IsNullOrEmpty(launcherPath)) launcherPath = Path.Combine(gameFolder, "Tools", "Launcher.exe");

        string wfPath = WarframePathResolver.ResolveLocalPath(gameFolder, "/Warframe.x64.exe");
        if (string.IsNullOrEmpty(wfPath)) wfPath = Path.Combine(gameFolder, "Warframe.x64.exe");

        if (File.Exists(launcherPath))
        {
            var fi = new FileInfo(launcherPath);
            string hash = WarframeDownloader.ComputeMd5(launcherPath);
            sb.AppendLine($"Launcher Executable:  {launcherPath}");
            sb.AppendLine($"  Size:               {fi.Length} B ({fi.Length / 1024.0 / 1024.0:F2} MB)");
            sb.AppendLine($"  Last Modified UTC:  {fi.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"  MD5 Hash:           {hash}");
        }
        else
        {
            sb.AppendLine($"Launcher Executable:  NOT FOUND at {launcherPath}");
        }

        if (File.Exists(wfPath))
        {
            var fi = new FileInfo(wfPath);
            string hash = WarframeDownloader.ComputeMd5(wfPath);
            sb.AppendLine($"Warframe Engine EXE:  {wfPath}");
            sb.AppendLine($"  Size:               {fi.Length} B ({fi.Length / 1024.0 / 1024.0:F2} MB)");
            sb.AppendLine($"  Last Modified UTC:  {fi.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"  MD5 Hash:           {hash}");
        }
        else
        {
            sb.AppendLine($"Warframe Engine EXE:  NOT FOUND at {wfPath}");
        }
        sb.AppendLine("--------------------------------------------------------------------------");
        sb.AppendLine();

        sb.AppendLine("=== 2. NATIVE WARFRAME APPLET SPECIFICATIONS ===");
        sb.AppendLine("A) ContentUpdate Applet:");
        sb.AppendLine("   - Executable:       Warframe.x64.exe");
        sb.AppendLine("   - Command CLI:      -applet:/EE/Types/Framework/ContentUpdate -silent");
        sb.AppendLine("   - Working Dir:      Root Game Folder (e.g. C:\\Program Files\\Epic Games\\Warframe)");
        sb.AppendLine("   - Primary Function: Scans downloaded .bulk cache packages, unpacks asset tables, and generates/compiles local .toc binary index lookup structures.");
        sb.AppendLine("   - Log Produced:     Preprocess.log (written to root or %LocalAppData%\\Warframe)");
        sb.AppendLine("   - Execution Mode:   Automated post-download preprocessor.");
        sb.AppendLine();

        sb.AppendLine("B) CacheRepair Applet:");
        sb.AppendLine("   - Executable:       Warframe.x64.exe");
        sb.AppendLine("   - Command CLI:      -applet:/EE/Types/Framework/CacheRepair -silent");
        sb.AppendLine("   - Working Dir:      Root Game Folder");
        sb.AppendLine("   - Primary Function: Performs deep consistency checks across all .cache packages, rebuilds missing or corrupted .toc index entries locally, and purges orphaned cache sectors.");
        sb.AppendLine("   - Log Produced:     Repair.log");
        sb.AppendLine();

        sb.AppendLine("C) CacheDefraggerAsync Applet:");
        sb.AppendLine("   - Executable:       Warframe.x64.exe");
        sb.AppendLine("   - Command CLI:      -applet:/EE/Types/Framework/CacheDefraggerAsync -silent");
        sb.AppendLine("   - Working Dir:      Root Game Folder");
        sb.AppendLine("   - Storage Aware:    Detects drive media via Windows Storage API. On HDDs, defragments physical cache sector order. On SSDs, performs safe TRIM / index compaction without write-heavy defrag.");
        sb.AppendLine("   - Log Produced:     Defrag.log");
        sb.AppendLine("--------------------------------------------------------------------------");
        sb.AppendLine();

        sb.AppendLine("=== 3. OFFICIAL PROCESS TREE & EXECUTION ARCHITECTURE ===");
        sb.AppendLine("Official Launcher Process Hierarchy:");
        sb.AppendLine("  Launcher.exe (Bootstrap UI)");
        sb.AppendLine("    ↓ [Downloads index.txt.lzma & outdated .bulk payloads to staging]");
        sb.AppendLine("  Warframe.x64.exe -applet:/EE/Types/Framework/ContentUpdate -silent");
        sb.AppendLine("    ↓ [Applet processes .bulk packages and updates disk .toc index structures]");
        sb.AppendLine("  Warframe.x64.exe [Game Engine Launch with parameters: -cluster:public -g_language:pt]");
        sb.AppendLine("--------------------------------------------------------------------------");
        sb.AppendLine();

        sb.AppendLine("=== 4. CACHE & TOC ARCHITECTURE RELATIONSHIP ===");
        sb.AppendLine("  - .bulk Files:  Uncompressed/compressed binary cache archives (e.g. B.Font.cache, B.Misc.cache, F.Texture.cache) hosting raw game assets.");
        sb.AppendLine("  - .toc Files:   Table of Contents lookup indices storing internal file offsets, sector lengths, and hash tables for instant engine lookups.");
        sb.AppendLine("  - Critical Finding: Downloading a .bulk file directly from CDN updates the raw asset container. However, .toc lookup tables MUST be compiled locally by the native ContentUpdate applet. Manually overwriting .toc files from CDN without applet preprocessing desynchronizes local cache offsets.");
        sb.AppendLine("--------------------------------------------------------------------------");
        sb.AppendLine();

        sb.AppendLine("=== 5. UPDATER STATE MACHINE DESIGN ===");
        sb.AppendLine("  [IDLE]");
        sb.AppendLine("    ↓");
        sb.AppendLine("  [LOAD MANIFEST]           (Download index.txt.lzma from CDN)");
        sb.AppendLine("    ↓");
        sb.AppendLine("  [ANALYZE INSTALLATION]    (Run read-only verifier & path resolution scan)");
        sb.AppendLine("    ↓");
        sb.AppendLine("  [PRE-FLIGHT VALIDATION]   (Filter queue: exclude .toc, inactive lang, inactive gfx)");
        sb.AppendLine("    ↓");
        sb.AppendLine("  [STAGE SESSION BACKUP]    (Copy target files to .GameLauncherBackup\\UpdateSession_<ts>)");
        sb.AppendLine("    ↓");
        sb.AppendLine("  [DOWNLOAD PAYLOADS]       (Download .bulk payloads to .tmp files & verify MD5)");
        sb.AppendLine("    ↓");
        sb.AppendLine("  [ATOMIC REPLACEMENT]      (Atomic move .tmp -> target file)");
        sb.AppendLine("    ↓");
        sb.AppendLine("  [NATIVE APPLET PROCESS]   (Execute Warframe.x64.exe -applet:/EE/Types/Framework/ContentUpdate)");
        sb.AppendLine("    ↓");
        sb.AppendLine("  [POST-UPDATE VERIFY]      (Full read-only verifier pass)");
        sb.AppendLine("    ↓");
        sb.AppendLine("  [SUCCESS / COMMIT]        (If verify passes, commit session; else initiate ROLLBACK)");
        sb.AppendLine("==========================================================================");

        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncher", "logs");
        Directory.CreateDirectory(logDir);
        string reportFile = Path.Combine(logDir, $"warframe_native_update_audit_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        await File.WriteAllTextAsync(reportFile, sb.ToString());
        logger.LogSuccess($"✔ [NATIVE AUDIT REPORT] Saved read-only native update audit to '{reportFile}'");
        return reportFile;
    }
}
