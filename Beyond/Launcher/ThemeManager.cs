using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Newtonsoft.Json;

namespace Launcher
{
    // Owns Beyond's app-local palette: the accent and the danger colour. Both are
    // user-customisable (Configurator ColorPickers). Each drives a base Color
    // resource plus derived shades (Dim fill, Light highlight, Contrast text) and,
    // for the accent, the glow BoxShadows. Everything is set into application
    // resources and cascades live via DynamicResource. None of this touches the OS.
    public static class ThemeManager
    {
        public const string DefaultAccentHex = "#00E5FF";
        public const string DefaultDangerHex = "#FF5252";

        private const string AccentKey = "BeyondAccentColor";
        private const string AccentDimKey = "BeyondAccentDimColor";
        private const string AccentLightKey = "BeyondAccentLightColor";
        private const string AccentContrastKey = "BeyondAccentContrastColor";

        private const string DangerKey = "BeyondDangerColor";
        private const string DangerDimKey = "BeyondDangerDimColor";
        private const string DangerContrastKey = "BeyondDangerContrastColor";

        // Glow keys (built from the accent). Names match the App.axaml/MainWindow refs.
        private const string GlowChipKey = "BeyondGlowChip";          // logo chip, resting
        private const string GlowChipStrongKey = "BeyondGlowChipStrong"; // logo chip, hover
        private const string GlowChipSoftKey = "BeyondGlowChipSoft";   // logo chip, pressed
        private const string GlowEmblemKey = "BeyondGlowEmblem";       // About window emblem

        // Derived-shade factors, tuned so cyan reproduces the original hand-picked
        // #003B46 (dim) and #6BF3FF (light) shades.
        private const double DimFactor = 0.26;   // multiply toward black
        private const double LightFactor = 0.42; // blend toward white

        private static readonly Color DarkContrast = Color.Parse("#0F0F11");
        private static readonly Color LightContrast = Color.Parse("#F5F7FA");

        private static Color _accent = Color.Parse(DefaultAccentHex);
        private static Color _danger = Color.Parse(DefaultDangerHex);

        public static Color AccentColor => _accent;
        public static Color DangerColor => _danger;

        private sealed class ThemeConfig
        {
            public string Accent { get; set; } = DefaultAccentHex;
            public string Danger { get; set; } = DefaultDangerHex;
        }

        private static string GetConfigPath() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "theme.json");

        // Loads the saved palette (if any) and applies it. Call once at startup,
        // after the Application exists and before view-models read the colours.
        public static void Initialize()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    ThemeConfig? cfg = JsonConvert.DeserializeObject<ThemeConfig>(File.ReadAllText(path));
                    if (cfg != null)
                    {
                        if (Color.TryParse(cfg.Accent, out Color a)) _accent = a;
                        if (Color.TryParse(cfg.Danger, out Color d)) _danger = d;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ThemeManager load failed: {ex.Message}");
            }

            ApplyAccent(_accent);
            ApplyDanger(_danger);
        }

        public static void SetAccent(Color color)
        {
            if (color == _accent)
            {
                return;
            }

            _accent = color;
            ApplyAccent(color);
            Save();
        }

        public static void SetDanger(Color color)
        {
            if (color == _danger)
            {
                return;
            }

            _danger = color;
            ApplyDanger(color);
            Save();
        }

        public static void ResetAccent() => SetAccent(Color.Parse(DefaultAccentHex));

        public static void ResetDanger() => SetDanger(Color.Parse(DefaultDangerHex));

        private static void ApplyAccent(Color c)
        {
            if (Application.Current is not { } app)
            {
                return;
            }

            app.Resources[AccentKey] = c;
            app.Resources[AccentDimKey] = Darken(c, DimFactor);
            app.Resources[AccentLightKey] = Lighten(c, LightFactor);
            app.Resources[AccentContrastKey] = Contrast(c);

            // Glows are the accent at varying blur/opacity — so every glow matches it.
            app.Resources[GlowChipKey] = Glow(c, 5, 0x4D);
            app.Resources[GlowChipStrongKey] = Glow(c, 9, 0x99);
            app.Resources[GlowChipSoftKey] = Glow(c, 3, 0x33);
            app.Resources[GlowEmblemKey] = Glow(c, 22, 0x55);
        }

        private static void ApplyDanger(Color c)
        {
            if (Application.Current is not { } app)
            {
                return;
            }

            app.Resources[DangerKey] = c;
            app.Resources[DangerDimKey] = Darken(c, DimFactor);
            app.Resources[DangerContrastKey] = Contrast(c);
        }

        private static void Save()
        {
            try
            {
                string path = GetConfigPath();
                string? dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonConvert.SerializeObject(
                    new ThemeConfig { Accent = _accent.ToString(), Danger = _danger.ToString() },
                    Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ThemeManager save failed: {ex.Message}");
            }
        }

        // Darker, same-hue shade for resting button/toggle fills.
        private static Color Darken(Color c, double f) => Color.FromArgb(
            c.A,
            (byte)Math.Round(c.R * f),
            (byte)Math.Round(c.G * f),
            (byte)Math.Round(c.B * f));

        // Lighter, same-hue shade (blend toward white) for hover highlights.
        private static Color Lighten(Color c, double t) => Color.FromArgb(
            c.A,
            (byte)Math.Round(c.R + (255 - c.R) * t),
            (byte)Math.Round(c.G + (255 - c.G) * t),
            (byte)Math.Round(c.B + (255 - c.B) * t));

        // Black-ish vs white-ish text for legibility on a filled colour.
        private static Color Contrast(Color c)
        {
            double luminance = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
            return luminance > 140 ? DarkContrast : LightContrast;
        }

        // A soft outer glow: the colour at the given blur radius and opacity.
        private static BoxShadows Glow(Color c, double blur, byte alpha)
        {
            Color glow = Color.FromArgb(alpha, c.R, c.G, c.B);
            return new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 0, Blur = blur, Spread = 0, Color = glow });
        }
    }
}
