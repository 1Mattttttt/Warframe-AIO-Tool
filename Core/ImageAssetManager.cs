using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public static class ImageAssetManager
{
    private static readonly Dictionary<string, BitmapImage> ImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SyncLock = new();

    public static BitmapImage? Load(string assetPath, LoggerService? logger = null)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            logger?.LogError("✖ Failed loading launcher logo: Asset path is empty.");
            return null;
        }

        lock (SyncLock)
        {
            if (ImageCache.TryGetValue(assetPath, out var cachedImage))
            {
                return cachedImage;
            }

            try
            {
                string packUriString = assetPath.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)
                    ? assetPath
                    : $"pack://application:,,,/{assetPath.TrimStart('/')}";

                var uri = new Uri(packUriString, UriKind.Absolute);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                ImageCache[assetPath] = bitmap;

                string fileName = Path.GetFileName(assetPath);
                logger?.LogSuccess($"✔ Loaded launcher logo:\n{fileName}");
                return bitmap;
            }
            catch (Exception ex)
            {
                logger?.LogError($"✖ Failed loading launcher logo:\n{assetPath} ({ex.Message})");
                return null;
            }
        }
    }
}
