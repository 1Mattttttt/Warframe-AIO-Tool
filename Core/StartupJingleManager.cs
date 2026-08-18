using System;
using System.Threading.Tasks;
using GameLauncher.Logging;

namespace GameLauncher.Core;

/// <summary>
/// Legacy Startup Jingle Manager stub (Console beep replaced with AmbientMusicManager).
/// </summary>
public static class StartupJingleManager
{
    public static Task PlayJingleAsync(bool enableStartupJingle, LoggerService logger)
    {
        // Replaced completely by AmbientMusicManager
        return Task.CompletedTask;
    }
}
