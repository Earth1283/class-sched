using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ClassSchedule.Core;
using Microsoft.Win32;

namespace ClassSchedule;

public static class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;
    private static string setting = "System";
    private static string accent = "Blue";
    private static string palette = "Google";
    private static bool hooked;

    public static bool IsDark { get; private set; }

    public static event Action? Changed;

    public static void Apply(string themeSetting) => Apply(themeSetting, accent, palette);

    public static void Apply(Settings s) => Apply(s.Theme, s.Accent, s.Palette);

    private static void Apply(string themeSetting, string accentName, string paletteName)
    {
        setting = themeSetting;
        accent = accentName;
        palette = paletteName;
        if (!hooked)
        {
            SystemEvents.UserPreferenceChanged += (_, _) =>
                Application.Current.Dispatcher.Invoke(() => { if (setting == "System") Apply("System"); });
            hooked = true;
        }

        IsDark = setting == "Dark" || (setting == "System" && SystemPrefersDark());
        var uri = new Uri($"/ClassSchedule;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Relative);
        var theme = new ResourceDictionary { Source = uri };
        Tint(theme);
        Application.Current.Resources.MergedDictionaries[0] = theme;

        foreach (Window w in Application.Current.Windows) StyleTitleBar(w);
        Changed?.Invoke();
    }

    private static void Tint(ResourceDictionary theme)
    {
        if (Accents.TryGetValue(accent, out var set) && accent != "Blue")
        {
            var c = IsDark ? set.Dark : set.Light;
            Put(theme, "Brush.Primary", c.Primary);
            Put(theme, "Brush.PrimaryHover", c.Hover);
            Put(theme, "Brush.PrimaryPress", c.Press);
            Put(theme, "Brush.OnPrimary", c.OnPrimary);
            Put(theme, "Brush.Container", c.Container);
            Put(theme, "Brush.OnContainer", c.OnContainer);
            Put(theme, "Brush.Focus", WithAlpha(c.Primary, 0x66));
            Put(theme, "Brush.PrimaryTint", WithAlpha(c.Primary, (byte)(IsDark ? 0x1A : 0x14)));
            Put(theme, "Brush.SnackAction", (IsDark ? set.Light : set.Dark).Primary);
        }

        if (palette == "Google") return;
        foreach (var key in CoursePalette.Keys)
            foreach (var part in new[] { "Bg", "Line", "Dot" })
            {
                var name = $"Course.{key}.{part}";
                if (theme[name] is not SolidColorBrush brush) continue;
                Put(theme, name, palette == "Pastel" ? Mix(brush.Color, Colors.White, IsDark ? 0.28 : 0.42) : Vivid(brush.Color));
            }
    }

    private static void Put(ResourceDictionary theme, string key, Color color) => theme[key] = new SolidColorBrush(color);

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount), (byte)Math.Round(a.G + (b.G - a.G) * amount), (byte)Math.Round(a.B + (b.B - a.B) * amount));

    private static Color Vivid(Color c)
    {
        var (h, s, l) = ToHsl(c);
        return FromHsl(h, Math.Min(1, s * 1.35 + 0.08), IsDark ? Math.Clamp(l, 0.55, 0.7) : Math.Clamp(l * 0.92, 0.3, 0.5));
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        var q2 = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p2 = 2 * l - q2;
        return Color.FromRgb((byte)Math.Round(Channel(p2, q2, h + 1.0 / 3) * 255), (byte)Math.Round(Channel(p2, q2, h) * 255), (byte)Math.Round(Channel(p2, q2, h - 1.0 / 3) * 255));
    }

    private readonly record struct Tone(Color Primary, Color Hover, Color Press, Color OnPrimary, Color Container, Color OnContainer);

    private static Color Hex(string v) => (Color)ColorConverter.ConvertFromString(v);

    private static Tone Tone6(string primary, string hover, string press, string onPrimary, string container, string onContainer) =>
        new(Hex(primary), Hex(hover), Hex(press), Hex(onPrimary), Hex(container), Hex(onContainer));

    private static readonly Dictionary<string, (Tone Light, Tone Dark)> Accents = new()
    {
        ["Blue"] = (Tone6("#0B57D0", "#0A4DB8", "#0842A0", "#FFFFFF", "#C2E7FF", "#001D35"), Tone6("#A8C7FA", "#BCD3FB", "#8FB6F5", "#062E6F", "#004A77", "#C2E7FF")),
        ["Violet"] = (Tone6("#7B1FA2", "#8E24AA", "#6A1B9A", "#FFFFFF", "#F1D4F7", "#3A0A47"), Tone6("#D7A9E8", "#E2BDF0", "#C690DB", "#3A0A47", "#5B1F70", "#F6DDFC")),
        ["Green"] = (Tone6("#1E8E3E", "#26A247", "#17722F", "#FFFFFF", "#C8ECD3", "#06260F"), Tone6("#81C995", "#96D3A7", "#6BB985", "#06260F", "#1E5C32", "#C8ECD3")),
        ["Rose"] = (Tone6("#C2185B", "#D81B60", "#AD1457", "#FFFFFF", "#FBD0E0", "#4A0B24"), Tone6("#F4A6C3", "#F7BAD0", "#EF8EB3", "#4A0B24", "#7A1E48", "#FBD0E0")),
        ["Orange"] = (Tone6("#C75B00", "#E06A00", "#A84D00", "#FFFFFF", "#FFDDB8", "#3B1B00"), Tone6("#FFB870", "#FFC88D", "#F5A458", "#3B1B00", "#7A3F00", "#FFDDB8"))
    };

    public static readonly string[] AccentNames = { "Blue", "Violet", "Green", "Rose", "Orange" };

    /// <summary>Four representative course colors for a palette, as they look in the current theme.</summary>
    public static Color[] PaletteSample(string name)
    {
        var basis = IsDark ? new[] { "#74C0EE", "#6CC49A", "#F5A25D", "#C58AD6" } : new[] { "#039BE5", "#33B679", "#EF6C00", "#8E24AA" };
        return basis.Select(Hex).Select(c => name == "Pastel" ? Mix(c, Colors.White, IsDark ? 0.28 : 0.42) : name == "Vivid" ? Vivid(c) : c).ToArray();
    }

    public static Color AccentColor(string name) =>
        (Accents.TryGetValue(name, out var set) ? (IsDark ? set.Dark : set.Light) : Accents["Blue"].Light).Primary;

    public static void StyleTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    private static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
