using System;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using GameLauncher.Models;

namespace GameLauncher.Core;

public record ThemeProfile(
    Color BaseColor,
    Color GlowColor,
    Color AccentColor,
    Color ButtonBackground,
    Color ButtonHover,
    Color ButtonBorder,
    Color PrimaryGradientStart,
    Color PrimaryGradientEnd,
    Color PrimaryForeground,
    Color CardBackground,
    Color CardBorder,
    Color ConsoleBackground,
    Color ConsoleBorder,
    Color ConsoleAlternatingBackground,
    Color TimestampColor
);

public static class LauncherThemeManager
{
    // Epic Games Profile: Pure dark graphite / black gaming launcher atmosphere
    public static readonly ThemeProfile EpicTheme = new(
        BaseColor: (Color)ColorConverter.ConvertFromString("#0C0C12"),
        GlowColor: (Color)ColorConverter.ConvertFromString("#22222E"),
        AccentColor: (Color)ColorConverter.ConvertFromString("#D6D6E0"),
        ButtonBackground: (Color)ColorConverter.ConvertFromString("#16161D"),
        ButtonHover: (Color)ColorConverter.ConvertFromString("#262632"),
        ButtonBorder: (Color)ColorConverter.ConvertFromString("#78788C"),
        PrimaryGradientStart: (Color)ColorConverter.ConvertFromString("#D6D6E0"),
        PrimaryGradientEnd: (Color)ColorConverter.ConvertFromString("#9EA0B0"),
        PrimaryForeground: (Color)ColorConverter.ConvertFromString("#0C0C12"),
        CardBackground: (Color)ColorConverter.ConvertFromString("#14141C"),
        CardBorder: (Color)ColorConverter.ConvertFromString("#262636"),
        ConsoleBackground: (Color)ColorConverter.ConvertFromString("#0E0E14"),
        ConsoleBorder: (Color)ColorConverter.ConvertFromString("#222230"),
        ConsoleAlternatingBackground: (Color)ColorConverter.ConvertFromString("#13131A"),
        TimestampColor: (Color)ColorConverter.ConvertFromString("#5C5E70")
    );

    // Steam Profile: Deep dark blue Steam-inspired environment
    public static readonly ThemeProfile SteamTheme = new(
        BaseColor: (Color)ColorConverter.ConvertFromString("#0A121E"),
        GlowColor: (Color)ColorConverter.ConvertFromString("#1B4B73"),
        AccentColor: (Color)ColorConverter.ConvertFromString("#66C0F4"),
        ButtonBackground: (Color)ColorConverter.ConvertFromString("#111C2D"),
        ButtonHover: (Color)ColorConverter.ConvertFromString("#162F4A"),
        ButtonBorder: (Color)ColorConverter.ConvertFromString("#66C0F4"),
        PrimaryGradientStart: (Color)ColorConverter.ConvertFromString("#66C0F4"),
        PrimaryGradientEnd: (Color)ColorConverter.ConvertFromString("#1B4B73"),
        PrimaryForeground: (Color)ColorConverter.ConvertFromString("#050B14"),
        CardBackground: (Color)ColorConverter.ConvertFromString("#0F1826"),
        CardBorder: (Color)ColorConverter.ConvertFromString("#1A2D44"),
        ConsoleBackground: (Color)ColorConverter.ConvertFromString("#080E17"),
        ConsoleBorder: (Color)ColorConverter.ConvertFromString("#16283D"),
        ConsoleAlternatingBackground: (Color)ColorConverter.ConvertFromString("#0C1421"),
        TimestampColor: (Color)ColorConverter.ConvertFromString("#415A77")
    );

    // Standalone Warframe Profile: Dark blue + Warframe Gold accents
    public static readonly ThemeProfile StandaloneTheme = new(
        BaseColor: (Color)ColorConverter.ConvertFromString("#0F151C"),
        GlowColor: (Color)ColorConverter.ConvertFromString("#5C4713"),
        AccentColor: (Color)ColorConverter.ConvertFromString("#F5D77F"),
        ButtonBackground: (Color)ColorConverter.ConvertFromString("#182027"),
        ButtonHover: (Color)ColorConverter.ConvertFromString("#27323A"),
        ButtonBorder: (Color)ColorConverter.ConvertFromString("#F5D77F"),
        PrimaryGradientStart: (Color)ColorConverter.ConvertFromString("#F5D77F"),
        PrimaryGradientEnd: (Color)ColorConverter.ConvertFromString("#C9A227"),
        PrimaryForeground: (Color)ColorConverter.ConvertFromString("#0F151C"),
        CardBackground: (Color)ColorConverter.ConvertFromString("#131B24"),
        CardBorder: (Color)ColorConverter.ConvertFromString("#2A3845"),
        ConsoleBackground: (Color)ColorConverter.ConvertFromString("#0C1117"),
        ConsoleBorder: (Color)ColorConverter.ConvertFromString("#202D38"),
        ConsoleAlternatingBackground: (Color)ColorConverter.ConvertFromString("#101720"),
        TimestampColor: (Color)ColorConverter.ConvertFromString("#6E5A2A")
    );

    // OwHelper Active Warning Override Profile: Deep red warning atmosphere
    public static readonly ThemeProfile OwHelperActiveTheme = new(
        BaseColor: (Color)ColorConverter.ConvertFromString("#180B0B"),
        GlowColor: (Color)ColorConverter.ConvertFromString("#5A1111"),
        AccentColor: (Color)ColorConverter.ConvertFromString("#F38BA8"),
        ButtonBackground: (Color)ColorConverter.ConvertFromString("#2A1010"),
        ButtonHover: (Color)ColorConverter.ConvertFromString("#421515"),
        ButtonBorder: (Color)ColorConverter.ConvertFromString("#F38BA8"),
        PrimaryGradientStart: (Color)ColorConverter.ConvertFromString("#F38BA8"),
        PrimaryGradientEnd: (Color)ColorConverter.ConvertFromString("#E64553"),
        PrimaryForeground: (Color)ColorConverter.ConvertFromString("#11111B"),
        CardBackground: (Color)ColorConverter.ConvertFromString("#201010"),
        CardBorder: (Color)ColorConverter.ConvertFromString("#441B1B"),
        ConsoleBackground: (Color)ColorConverter.ConvertFromString("#140909"),
        ConsoleBorder: (Color)ColorConverter.ConvertFromString("#3D1818"),
        ConsoleAlternatingBackground: (Color)ColorConverter.ConvertFromString("#1C0D0D"),
        TimestampColor: (Color)ColorConverter.ConvertFromString("#7A4B4B")
    );

    public static ThemeProfile GetTheme(LauncherType launcherType, bool isOwHelperActive, bool isOwHelperInjected = false)
    {
        // Priority 1: OwHelper Injected protection theme
        // Priority 2: OwHelper Active warning theme
        if (isOwHelperInjected || isOwHelperActive)
        {
            return OwHelperActiveTheme;
        }

        // Priority 3: Standard Launcher themes
        return launcherType switch
        {
            LauncherType.Steam => SteamTheme,
            LauncherType.Standalone => StandaloneTheme,
            _ => EpicTheme
        };
    }
}
