using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

// Background music service for playing ambient tracks from embedded resources
public class AmbientMusicManager
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;
    private readonly MediaPlayer _mediaPlayer = new();
    private readonly Dispatcher _dispatcher;

    private string? _extractedMusicPath;
    private bool _isInitialized;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _isFading;
    private TimeSpan _pausedPosition = TimeSpan.Zero;

    public event Action<double>? VolumeChanged;
    public event Action<bool>? MuteStateChanged;

    public AmbientMusicManager(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        RunOnUIThread(() =>
        {
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
        });
    }

    public bool IsPlaying => _isPlaying;
    public bool IsPaused => _isPaused;

    public double Volume
    {
        get => _settings.BackgroundMusicVolume;
        set
        {
            _settings.BackgroundMusicVolume = Math.Clamp(value, 0.0, 100.0);
            _settings.Save();

            if (!_isFading)
            {
                ApplyCurrentVolume();
            }
            VolumeChanged?.Invoke(_settings.BackgroundMusicVolume);
        }
    }

    public bool IsMuted
    {
        get => _settings.BackgroundMusicMuted;
        set
        {
            _settings.BackgroundMusicMuted = value;
            _settings.Save();

            if (!_isFading)
            {
                ApplyCurrentVolume();
            }
            MuteStateChanged?.Invoke(value);
        }
    }

    public bool IsEnabled
    {
        get => _settings.EnableBackgroundMusic;
        set
        {
            _settings.EnableBackgroundMusic = value;
            _settings.Save();

            if (!value)
            {
                Stop();
            }
            else if (_isInitialized && !_isPlaying)
            {
                _ = FadeInAsync();
            }
        }
    }

    // Extract embedded MP3 to local cache folder if not present, then start playback
    public void InitializeAndStart()
    {
        Task.Run(async () =>
        {
            try
            {
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GameLauncher", "Cache", "Music");

                Directory.CreateDirectory(cacheDir);
                string targetExtractedFile = Path.Combine(cacheDir, "ambient_music.mp3");

                var assembly = Assembly.GetExecutingAssembly();
                string[] resourceNames = assembly.GetManifestResourceNames();
                string? mp3ResourceName = Array.Find(resourceNames, r => r.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrEmpty(mp3ResourceName))
                {
                    _logger.LogWarning("Ambient music resource not found in assembly.");
                    return;
                }

                using (var stream = assembly.GetManifestResourceStream(mp3ResourceName))
                {
                    if (stream != null)
                    {
                        // Extract only if file missing or byte size changed
                        if (!File.Exists(targetExtractedFile) || new FileInfo(targetExtractedFile).Length != stream.Length)
                        {
                            using var destStream = File.Create(targetExtractedFile);
                            await stream.CopyToAsync(destStream);
                        }
                    }
                }

                if (File.Exists(targetExtractedFile))
                {
                    _extractedMusicPath = targetExtractedFile;
                    _isInitialized = true;

                    if (_settings.EnableBackgroundMusic)
                    {
                        RunOnUIThread(() =>
                        {
                            _mediaPlayer.Open(new Uri(_extractedMusicPath, UriKind.Absolute));
                            _mediaPlayer.Volume = 0.0;
                            _mediaPlayer.Play();
                            _isPlaying = true;
                            _isPaused = false;
                        });

                        await FadeInAsync();
                        _logger.LogInfo("Ambient music initialized.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed initializing ambient music: {ex.Message}");
            }
        });
    }

    public async Task FadeInAsync(int durationMs = 1200)
    {
        if (!_settings.EnableBackgroundMusic || string.IsNullOrEmpty(_extractedMusicPath)) return;

        _isFading = true;
        double targetVol = _settings.BackgroundMusicMuted ? 0.0 : (_settings.BackgroundMusicVolume / 100.0);
        int steps = 20;
        int delayPerStep = durationMs / steps;

        RunOnUIThread(() =>
        {
            if (!_isPlaying)
            {
                _mediaPlayer.Open(new Uri(_extractedMusicPath, UriKind.Absolute));
                _mediaPlayer.Position = _pausedPosition;
                _mediaPlayer.Play();
                _isPlaying = true;
                _isPaused = false;
            }
            _mediaPlayer.Volume = 0.0;
        });

        for (int i = 1; i <= steps; i++)
        {
            double stepVol = (targetVol / steps) * i;
            RunOnUIThread(() =>
            {
                if (_isPlaying && !_settings.BackgroundMusicMuted)
                {
                    _mediaPlayer.Volume = stepVol;
                }
            });
            await Task.Delay(delayPerStep);
        }

        _isFading = false;
        ApplyCurrentVolume();
    }

    public async Task FadeOutAsync(int durationMs = 800)
    {
        if (!_isPlaying) return;

        _isFading = true;
        double startVol = _mediaPlayer.Volume;
        int steps = 16;
        int delayPerStep = durationMs / steps;

        for (int i = steps - 1; i >= 0; i--)
        {
            double stepVol = (startVol / steps) * i;
            RunOnUIThread(() =>
            {
                _mediaPlayer.Volume = stepVol;
            });
            await Task.Delay(delayPerStep);
        }

        RunOnUIThread(() =>
        {
            _mediaPlayer.Pause();
            _pausedPosition = _mediaPlayer.Position;
            _isPlaying = false;
            _isPaused = true;
        });
        _isFading = false;
    }

    public void Pause()
    {
        if (!_isPlaying) return;

        RunOnUIThread(() =>
        {
            try
            {
                _pausedPosition = _mediaPlayer.Position;
                _mediaPlayer.Pause();
                _isPlaying = false;
                _isPaused = true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed pausing music: {ex.Message}");
            }
        });
    }

    public void Resume()
    {
        if (!_settings.EnableBackgroundMusic || _isPlaying || string.IsNullOrEmpty(_extractedMusicPath)) return;

        RunOnUIThread(() =>
        {
            try
            {
                _mediaPlayer.Open(new Uri(_extractedMusicPath, UriKind.Absolute));
                _mediaPlayer.Position = _pausedPosition;
                ApplyCurrentVolume();
                _mediaPlayer.Play();
                _isPlaying = true;
                _isPaused = false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed resuming music: {ex.Message}");
            }
        });
    }

    public void TogglePlayPause()
    {
        if (_isPlaying) Pause();
        else Resume();
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    public void Stop()
    {
        RunOnUIThread(() =>
        {
            try
            {
                _mediaPlayer.Stop();
                _isPlaying = false;
                _isPaused = false;
                _pausedPosition = TimeSpan.Zero;
            }
            catch { }
        });
    }

    private void ApplyCurrentVolume()
    {
        RunOnUIThread(() =>
        {
            _mediaPlayer.Volume = _settings.BackgroundMusicMuted ? 0.0 : (_settings.BackgroundMusicVolume / 100.0);
        });
    }

    private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
    {
        // Loop playback seamlessly when track finishes
        RunOnUIThread(() =>
        {
            _mediaPlayer.Position = TimeSpan.Zero;
            _mediaPlayer.Play();
        });
    }

    private void MediaPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
    {
        _logger.LogError($"Playback error: {e.ErrorException.Message}");
        _isPlaying = false;
    }

    private void RunOnUIThread(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.InvokeAsync(action);
    }
}
