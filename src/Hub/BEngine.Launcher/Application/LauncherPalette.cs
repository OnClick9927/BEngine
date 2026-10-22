using DrawingColor = System.Drawing.Color;

namespace BEngine.Launcher;

internal sealed record LauncherPalette(
    DrawingColor Window, DrawingColor Sidebar, DrawingColor Panel, DrawingColor Raised,
    DrawingColor Field, DrawingColor Border, DrawingColor Accent, DrawingColor Primary,
    DrawingColor Secondary, DrawingColor Warning, DrawingColor Error, DrawingColor AccentText)
{
    internal static LauncherPalette From(LauncherPersonalization preferences)
    {
        var light = preferences.Theme == "Light";
        var accent = preferences.AccentColor switch
        {
            "Blue" => DrawingColor.FromArgb(36, 105, 196),
            "Purple" => DrawingColor.FromArgb(126, 76, 191),
            "Orange" => DrawingColor.FromArgb(172, 82, 17),
            "Rose" => DrawingColor.FromArgb(188, 57, 99),
            _ => DrawingColor.FromArgb(24, 122, 104)
        };
        var accentText = light ? accent : preferences.AccentColor switch
        {
            "Blue" => DrawingColor.FromArgb(122, 177, 246),
            "Purple" => DrawingColor.FromArgb(193, 156, 241),
            "Orange" => DrawingColor.FromArgb(241, 175, 115),
            "Rose" => DrawingColor.FromArgb(244, 151, 179),
            _ => DrawingColor.FromArgb(103, 207, 187)
        };
        return light
            ? new(DrawingColor.FromArgb(244, 246, 249), DrawingColor.FromArgb(231, 235, 240),
                DrawingColor.FromArgb(248, 249, 251), DrawingColor.FromArgb(254, 254, 254),
                DrawingColor.FromArgb(239, 242, 246), DrawingColor.FromArgb(198, 205, 214),
                accent, DrawingColor.FromArgb(29, 36, 47), DrawingColor.FromArgb(83, 95, 112),
                DrawingColor.FromArgb(147, 87, 15), DrawingColor.FromArgb(187, 43, 46), accentText)
            : new(DrawingColor.FromArgb(30, 32, 35), DrawingColor.FromArgb(24, 26, 29),
                DrawingColor.FromArgb(39, 42, 46), DrawingColor.FromArgb(47, 50, 55),
                DrawingColor.FromArgb(52, 55, 60), DrawingColor.FromArgb(67, 71, 76),
                accent, DrawingColor.FromArgb(236, 238, 240), DrawingColor.FromArgb(166, 172, 178),
                DrawingColor.FromArgb(235, 163, 72), DrawingColor.FromArgb(235, 105, 96), accentText);
    }

    internal DrawingColor[] Colors =>
        [Window, Sidebar, Panel, Raised, Field, Border, Accent, Primary, Secondary, Warning, Error, AccentText];
}
