// Core/WarframeCleaner.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public static class WarframeCleaner
{
    public static void CleanWarframeFiles(LoggerService logger, AppSettings? settings = null)
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        logger.LogInfo("=== INICIANDO LIMPEZA OTIMIZADA DE RASTREAMENTO DO WARFRAME ===");

        // 1. Encerrar processos em execução para liberar arquivos bloqueados (incluindo CrashSender)
        KillWarframeProcesses(logger);

        // 2. Limpeza cirúrgica de %LOCALAPPDATA%\Warframe preservando EE.cfg
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string warframeLocalDir = Path.Combine(localAppData, "Warframe");

        if (Directory.Exists(warframeLocalDir))
        {
            CleanWarframeLocalDataDir(warframeLocalDir, logger);
        }

        // 3. Limpeza de diretórios secundários de telemetria e crash dumps
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string tempPath = Path.GetTempPath();

        var secondaryDirs = new List<string>
        {
            Path.Combine(localAppData, "DigitalExtremes"),
            Path.Combine(localAppData, "Digital Extremes"),
            Path.Combine(appData, "Warframe"),
            Path.Combine(appData, "DigitalExtremes"),
            Path.Combine(appData, "Digital Extremes"),
            Path.Combine(tempPath, "Warframe"),
            Path.Combine(tempPath, "DigitalExtremes")
        };

        foreach (var dir in secondaryDirs)
        {
            if (Directory.Exists(dir))
            {
                logger.LogInfo($"Limpando pasta residual de telemetria: {dir}");
                PurgeDirectoryContents(dir, logger);
            }
        }

        // 4. Limpeza de tokens no Registro do Windows (HKCU\Software\Digital Extremes)
        CleanWarframeRegistry(logger);

        // 5. Limpeza de pacotes voláteis H.Misc em Cache.Windows
        CleanCacheWindowsHMisc(logger, settings);

        logger.LogSuccess("✔ Limpeza otimizada concluída: tokens, pacotes H.Misc e vínculos de contas removidos, EE.cfg preservado.");
    }

    private static void KillWarframeProcesses(LoggerService logger)
    {
        string[] processNames = ["Warframe.x64", "Warframe", "Launcher", "RemoteCrashSender", "CrashHandler"];
        foreach (var name in processNames)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var p in procs)
                {
                    logger.LogWarning($"Encerrando processo ativo {p.ProcessName} (PID: {p.Id}) para liberar arquivos...");
                    p.Kill();
                    p.WaitForExit(2000);
                    p.Dispose();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Não foi possível encerrar {name}: {ex.Message}");
            }
        }
    }

    private static void CleanWarframeLocalDataDir(string dirPath, LoggerService logger)
    {
        logger.LogInfo($"Executando limpeza seletiva em: {dirPath}");

        // A. Remover subdiretórios de contas anteriores (pastas com hash MD5 de 32 caracteres e Crashes)
        try
        {
            var subDirs = Directory.GetDirectories(dirPath);
            foreach (var subDir in subDirs)
            {
                string dirName = Path.GetFileName(subDir);

                // Pastas de contas com hashes de 32 caracteres contêm o arquivo binário 'settings' vinculado à conta
                bool isAccountHashDir = dirName.Length == 32 && IsHexString(dirName);
                bool isCrashDir = dirName.Equals("Crashes", StringComparison.OrdinalIgnoreCase);

                if (isAccountHashDir || isCrashDir)
                {
                    logger.LogWarning($"Removendo pasta de vínculo de conta/telemetria: {dirName}");
                    Directory.Delete(subDir, recursive: true);
                }
                else
                {
                    // Limpar conteúdos de outras subpastas caso existam, preservando a estrutura
                    PurgeDirectoryContents(subDir, logger);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Erro ao processar subdiretórios em {dirPath}: {ex.Message}");
        }

        // B. Remover arquivos de logs e relatórios temporários, PRESERVANDO EE.cfg
        try
        {
            var files = Directory.GetFiles(dirPath);
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);

                // PRESERVAÇÃO ESTRITA DO EE.cfg (configurações de tela, vídeo e keybinds)
                if (fileName.Equals("EE.cfg", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogSuccess($"✔ PRESERVADO: Configurações de vídeo e controles em '{fileName}' mantidas intactas.");
                    continue;
                }

                // Excluir arquivos de logs, relatórios, configurações temporárias e dumps (EE.log, Launcher.log, Preprocess.log, Editor.cfg, etc.)
                DeleteFileSafely(file, logger);
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Erro ao limpar arquivos em {dirPath}: {ex.Message}");
        }
    }

    private static void PurgeDirectoryContents(string dirPath, LoggerService logger)
    {
        try
        {
            foreach (var file in Directory.GetFiles(dirPath))
            {
                DeleteFileSafely(file, logger);
            }

            foreach (var subDir in Directory.GetDirectories(dirPath))
            {
                PurgeDirectoryContents(subDir, logger);
                try
                {
                    if (Directory.GetFileSystemEntries(subDir).Length == 0)
                    {
                        Directory.Delete(subDir);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Erro ao esvaziar diretório {dirPath}: {ex.Message}");
        }
    }

    private static void DeleteFileSafely(string filePath, LoggerService logger)
    {
        if (Path.GetExtension(filePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInfo($"Ignorado arquivo executável: {filePath}");
            return;
        }

        if (!File.Exists(filePath)) return;

        try
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
            File.Delete(filePath);
            logger.LogInfo($"Arquivo excluído: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Falha ao excluir {filePath}: {ex.Message}");
        }
    }

    private static void CleanWarframeRegistry(LoggerService logger)
    {
        try
        {
            using var hkcu = Registry.CurrentUser;
            using var deKey = hkcu.OpenSubKey(@"Software\Digital Extremes", writable: true);
            if (deKey != null)
            {
                logger.LogInfo("Purgando chaves de telemetria em HKCU\\Software\\Digital Extremes...");
                hkcu.DeleteSubKeyTree(@"Software\Digital Extremes", throwOnMissingSubKey: false);
                logger.LogSuccess("✔ Chaves de registro da Digital Extremes removidas com sucesso.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Exceção ao limpar registro HKCU: {ex.Message}");
        }

        try
        {
            using var hklm = Registry.LocalMachine;
            using var deHklm = hklm.OpenSubKey(@"SOFTWARE\Digital Extremes", writable: true);
            if (deHklm != null)
            {
                logger.LogInfo("Purgando chaves residuais em HKLM\\SOFTWARE\\Digital Extremes...");
                hklm.DeleteSubKeyTree(@"SOFTWARE\Digital Extremes", throwOnMissingSubKey: false);
                logger.LogSuccess("✔ Chaves de registro HKLM da Digital Extremes removidas.");
            }
        }
        catch { }
    }

    private static void CleanCacheWindowsHMisc(LoggerService logger, AppSettings? settings)
    {
        settings ??= AppSettings.Load();
        var candidateDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(settings?.CacheFolder) && Directory.Exists(settings.CacheFolder))
        {
            candidateDirs.Add(settings.CacheFolder);
        }

        if (!string.IsNullOrWhiteSpace(settings?.WarframeInstallFolder))
        {
            string c1 = Path.Combine(settings.WarframeInstallFolder, "Cache.Windows");
            if (Directory.Exists(c1)) candidateDirs.Add(c1);

            string c2 = Path.Combine(settings.WarframeInstallFolder, "Downloaded", "Cache.Windows");
            if (Directory.Exists(c2)) candidateDirs.Add(c2);

            string c3 = Path.Combine(settings.WarframeInstallFolder, "Public", "Cache.Windows");
            if (Directory.Exists(c3)) candidateDirs.Add(c3);
        }

        // Caminhos padrões conhecidos (Epic Games, Steam, Standalone)
        string[] standardPaths = [
            @"C:\Program Files\Epic Games\Warframe\Downloaded\Cache.Windows",
            @"C:\Program Files (x86)\Epic Games\Warframe\Downloaded\Cache.Windows",
            @"D:\Epic Games\Warframe\Downloaded\Cache.Windows",
            @"E:\Epic Games\Warframe\Downloaded\Cache.Windows",
            @"C:\Program Files (x86)\Steam\steamapps\common\Warframe\Downloaded\Public\Cache.Windows",
            @"C:\Program Files\Steam\steamapps\common\Warframe\Downloaded\Public\Cache.Windows",
            @"D:\SteamLibrary\steamapps\common\Warframe\Downloaded\Public\Cache.Windows",
            @"E:\SteamLibrary\steamapps\common\Warframe\Downloaded\Public\Cache.Windows",
            @"C:\Program Files (x86)\Steam\steamapps\common\Warframe\Cache.Windows",
            @"C:\Program Files\Steam\steamapps\common\Warframe\Cache.Windows",
            @"D:\SteamLibrary\steamapps\common\Warframe\Cache.Windows",
            @"E:\SteamLibrary\steamapps\common\Warframe\Cache.Windows"
        ];

        foreach (var p in standardPaths)
        {
            if (Directory.Exists(p)) candidateDirs.Add(p);
        }

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                string root = drive.RootDirectory.FullName;

                string p1 = Path.Combine(root, "Epic Games", "Warframe", "Downloaded", "Cache.Windows");
                if (Directory.Exists(p1)) candidateDirs.Add(p1);

                string p2 = Path.Combine(root, "SteamLibrary", "steamapps", "common", "Warframe", "Downloaded", "Public", "Cache.Windows");
                if (Directory.Exists(p2)) candidateDirs.Add(p2);
            }
        }
        catch { }

        int totalDeleted = 0;
        foreach (var cacheDir in candidateDirs)
        {
            try
            {
                logger.LogInfo($"Inspecionando Cache.Windows para purga de H.Misc: {cacheDir}");
                var hMiscFiles = Directory.GetFiles(cacheDir, "H.Misc*");

                foreach (var file in hMiscFiles)
                {
                    string fileName = Path.GetFileName(file);
                    logger.LogWarning($"Removendo pacote de cache H.Misc: {fileName}");
                    DeleteFileSafely(file, logger);
                    totalDeleted++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Erro ao limpar arquivos H.Misc em {cacheDir}: {ex.Message}");
            }
        }

        if (totalDeleted > 0)
        {
            logger.LogSuccess($"✔ Limpeza de Cache.Windows concluída: {totalDeleted} arquivo(s) H.Misc removido(s) com sucesso.");
        }
        else
        {
            logger.LogInfo("ℹ Nenhum arquivo H.Misc pendente nos diretórios Cache.Windows detectados.");
        }
    }

    private static bool IsHexString(string test)
    {
        foreach (char c in test)
        {
            bool isHex = (c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }
}
