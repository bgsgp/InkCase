using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace InkCase.App;

public enum AppTheme
{
    Auto,
    Light,
    Dark,
}

/// <summary>
/// One source of truth for the UI palette. Every brush lives in
/// <see cref="Application.Current"/> resources under a stable key, and the XAML
/// uses DynamicResource — so switching the theme is a matter of replacing those
/// brushes, with no window rebuild and no per-control code.
///
/// Theme selection defaults to Auto: it reads the Windows "app theme" setting
/// and keeps following it (a 2 s poll; the registry has no change notification
/// that survives every Windows build). The user can pin Light or Dark from the
/// toolbar, and the choice is stored in %APPDATA%\InkCase\settings.json.
/// </summary>
public static class ThemeManager
{
    public const string KeyBg = "Bg";
    public const string KeySurface = "Surface";
    public const string KeySurface2 = "Surface2";
    public const string KeyInk = "Ink";
    public const string KeyMuted = "Muted";
    public const string KeyLine = "Line";
    public const string KeyBrand = "Brand";
    public const string KeyBrandSoft = "BrandSoft";
    public const string KeyRed = "Red";
    public const string KeyAmber = "Amber";
    public const string KeyHl = "Hl";
    public const string KeyHover = "Hover";
    public const string KeyEmptyBg = "EmptyBg";
    public const string KeyEvidence = "Evidence";
    public const string KeyEvidenceStrong = "EvidenceStrong";
    public const string KeyCorrect = "Correct";

    private static readonly (string Key, uint Light, uint Dark)[] Palette =
    [
        (KeyBg, 0xEEECE5, 0x14161A),
        (KeySurface, 0xFFFEFB, 0x1B1E23),
        (KeySurface2, 0xFAF8F2, 0x21252B),
        (KeyInk, 0x1B1E23, 0xE9E7E2),
        (KeyMuted, 0x646A72, 0x9CA3AC),
        (KeyLine, 0xE2DCCF, 0x2F353D),
        (KeyBrand, 0x0D5A53, 0x2FBFAB),
        (KeyBrandSoft, 0xE8F0ED, 0x1B3B36),
        (KeyRed, 0xB23A2C, 0xE86A55),
        (KeyAmber, 0xA9803A, 0xD9A441),
        (KeyHl, 0xFFE08A, 0x6D5717),
        (KeyHover, 0xDCEAE5, 0x22403A),
        (KeyEmptyBg, 0xF4F2EC, 0x171A1F),
        (KeyEvidence, 0xFFF1B8, 0x4E4318),
        (KeyEvidenceStrong, 0xFFD55A, 0x7A6320),
        (KeyCorrect, 0x1B7F3B, 0x5FD08A),
    ];

    private static readonly DispatcherTimer Poller = new() { Interval = TimeSpan.FromSeconds(2) };
    private static bool _appliedDark;
    private static bool _initialised;

    static ThemeManager()
    {
        Poller.Tick += (_, _) =>
        {
            if (Mode == AppTheme.Auto && SystemIsDark() != _appliedDark) Apply();
        };
    }

    public static AppTheme Mode { get; private set; } = AppTheme.Auto;

    /// <summary>True when the palette currently in effect is the dark one.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Raised after the palette is swapped, so code-built content can be re-rendered.</summary>
    public static event EventHandler? Changed;

    public static void Initialize()
    {
        if (_initialised) return;
        _initialised = true;
        Mode = LoadMode();
        Apply();
        Poller.Start();
    }

    public static void SetMode(AppTheme mode)
    {
        Mode = mode;
        SaveMode(mode);
        Apply();
    }

    public static AppTheme NextMode(AppTheme mode) =>
        mode switch { AppTheme.Auto => AppTheme.Light, AppTheme.Light => AppTheme.Dark, _ => AppTheme.Auto };

    public static string ModeLabel(AppTheme mode) =>
        mode switch { AppTheme.Light => "浅色", AppTheme.Dark => "深色", _ => "自动" };

    public static void Apply()
    {
        IsDark = IsDarkFor(Mode);
        _appliedDark = IsDark;
        var resources = Application.Current?.Resources;
        if (resources is not null)
        {
            foreach (var (key, light, dark) in Palette)
            {
                var color = FromRgb(IsDark ? dark : light);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                resources[key] = brush;
            }
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsDarkFor(AppTheme mode) => mode switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => SystemIsDark(),
    };

    /// <summary>
    /// Reads the Windows app-theme preference. Missing key (older builds, or a
    /// locked-down registry) means light, which is also Windows' own default.
    /// </summary>
    public static bool SystemIsDark()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                null);
            return value is int i && i == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static Color FromRgb(uint rgb) =>
        Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    // ---------- persistence ----------

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InkCase", "settings.json");

    private sealed record Settings(string Theme);

    private static AppTheme LoadMode()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return AppTheme.Auto;
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
            return Enum.TryParse<AppTheme>(settings?.Theme, true, out var mode) ? mode : AppTheme.Auto;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AppTheme.Auto;
        }
    }

    private static void SaveMode(AppTheme mode)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(mode.ToString())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A missing settings file is not worth interrupting the lesson for.
        }
    }
}
