using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BEngine.ExampleTests.EditorIconStyle;

internal static class Program
{
    private const int Scale = 4;
    private static readonly Color Line = Color.FromArgb(236, 190, 193, 198);
    private static readonly Color Dim = Color.FromArgb(218, 112, 116, 122);
    private static readonly Color Blue = Color.FromArgb(255, 91, 158, 207);
    private static readonly Color Green = Color.FromArgb(255, 105, 190, 133);
    private static readonly Color Orange = Color.FromArgb(255, 224, 166, 83);
    private static readonly Color Purple = Color.FromArgb(255, 175, 132, 205);
    private static readonly Color Cyan = Color.FromArgb(255, 92, 190, 198);
    private static readonly Color Red = Color.FromArgb(255, 211, 91, 91);
    private static readonly Color Folder = Color.FromArgb(255, 174, 176, 173);

    private static int Main()
    {
        var root = FindRepositoryRoot();
        var icons = Path.Combine(root, "src", "Core", "Editor", "Icons");
        Directory.CreateDirectory(Path.Combine(icons, "Assets"));
        Directory.CreateDirectory(Path.Combine(icons, "Components"));
        Directory.CreateDirectory(Path.Combine(icons, "Toolbar"));
        Directory.CreateDirectory(Path.Combine(icons, "Windows"));

        foreach (var name in new[] { "Default", "Text", "Script", "Data", "Assembly", "Markup", "Style",
                     "Shader", "Image", "Audio", "Model", "Material", "Scene", "Prefab", "Animation", "Font",
                     "Atlas", "Skin" })
            Save(Path.Combine(icons, "Assets", $"Asset{name}.png"), g => DrawAsset(g, name));
        Save(Path.Combine(icons, "Assets", "FolderClosed.png"), g => DrawFolder(g, false, false));
        Save(Path.Combine(icons, "Assets", "FolderOpen.png"), g => DrawFolder(g, true, false));
        Save(Path.Combine(icons, "Assets", "FolderEmpty.png"), g => DrawFolder(g, false, true));

        foreach (var name in new[] { "Component", "GameObject", "Transform", "Camera", "Script",
                     "SpriteRenderer", "ParticleSystem2D", "Rigidbody2D", "Collider2D", "Animator", "Tilemap",
                     "UIDocument", "Navigation" })
            Save(Path.Combine(icons, "Components", $"{name}.png"), g => DrawComponent(g, name));
        foreach (var name in new[] { "Add", "AddDropdown", "Minus", "New", "Save", "Delete", "Search", "Clear",
                     "Close", "Undo", "Redo", "UndoHistory", "Layout", "Dropdown", "Filter", "Import", "Export",
                     "Duplicate", "Refresh", "Browse",
                     "OpenFolder", "Settings", "Lock", "Unlock", "Visible", "Hidden", "Info", "Warning", "Error",
                     "Check", "Send", "Html", "Container", "Label", "Button", "Field", "Up", "View", "Move",
                     "Rotate", "Scale", "Rect", "Play", "Stop", "Pause", "Step", "FoldoutClosed", "FoldoutOpen",
                     "More" })
            Save(Path.Combine(icons, "Toolbar", $"{name}.png"), g => DrawToolbar(g, name));

        foreach (var name in new[] { "Window", "Scene", "Game", "Hierarchy", "Inspector", "Project", "Console",
                     "PackageManager", "Preferences", "ProjectSettings", "Shortcuts", "EditorStatus", "Codex",
                     "UIBuilder", "HtmlConverter", "SceneCamera" })
            Save(Path.Combine(icons, "Windows", $"{name}.png"), g => DrawWindow(g, name));

        DrawPackageIcon(Path.Combine(root, "src", "Packages", "Animation", "Editor", "Animation.png"), "Animation");
        DrawPackageIcon(Path.Combine(root, "src", "Packages", "Navigation2D", "Editor", "Navigation2D.png"), "Navigation2D");
        DrawPackageIcon(Path.Combine(root, "src", "Packages", "Physics2D", "Editor", "Physics2D.png"), "Physics2D");

        var expected = Directory.EnumerateFiles(icons, "*.png", SearchOption.AllDirectories).ToArray();
        if (expected.Length < 100) throw new InvalidOperationException($"Only {expected.Length} built-in icons were generated.");
        foreach (var path in expected)
        {
            using var image = new Bitmap(path);
            if (Path.GetFileName(path).Equals("BEngine.png", StringComparison.OrdinalIgnoreCase)) continue;
            if (image.Width != 32 || image.Height != 32)
                throw new InvalidDataException($"{path} is {image.Width}x{image.Height}, expected 32x32.");
            if ((image.PixelFormat & PixelFormat.Alpha) == 0 || image.GetPixel(0, 0).A != 0)
                throw new InvalidDataException($"{path} does not have a transparent background.");
        }
        Console.WriteLine($"EDITOR_ICON_STYLE_OK|{expected.Length}|transparent,32px,unity-style");
        return 0;
    }

    private static void Save(string path, Action<Graphics> draw)
    {
        using var large = new Bitmap(32 * Scale, 32 * Scale, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(large))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.ScaleTransform(Scale, Scale);
            draw(graphics);
        }
        using var icon = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(icon))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(large, 0, 0, 32, 32);
        }
        icon.Save(path, ImageFormat.Png);
    }

    private static Pen Pen(Color color, float width = 1.6f) => new(color, width)
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    private static SolidBrush Brush(Color color) => new(color);

    private static void DrawAsset(Graphics g, string kind)
    {
        switch (kind)
        {
            case "Image": DrawTextureAsset(g); return;
            case "Material": DrawMaterialAsset(g); return;
            case "Model": DrawModelAsset(g); return;
            case "Prefab": DrawPrefabAsset(g); return;
            case "Scene": DrawSceneAsset(g); return;
            case "Atlas": DrawAtlasAsset(g); return;
            case "Skin": DrawSkinAsset(g); return;
        }

        var accent = kind switch
        {
            "Script" or "Markup" or "Style" => Cyan,
            "Audio" or "Animation" => Orange,
            "Shader" => Purple,
            "Assembly" => Red,
            _ => Line
        };
        using var line = Pen(Line);
        using var glyph = Pen(accent, 1.8f);
        using var fill = Brush(Color.FromArgb(45, Line));
        var page = new GraphicsPath();
        page.AddLines([new(7, 3), new(20, 3), new(26, 9), new(26, 29), new(7, 29), new(7, 3)]);
        g.FillPath(fill, page); g.DrawPath(line, page); g.DrawLine(line, 20, 3, 20, 9); g.DrawLine(line, 20, 9, 26, 9);
        switch (kind)
        {
            case "Default": case "Text":
                g.DrawLine(glyph, 11, 15, 22, 15); g.DrawLine(glyph, 11, 19, 22, 19); g.DrawLine(glyph, 11, 23, 19, 23); break;
            case "Script":
                g.DrawLines(glyph, [new(14, 14), new(10, 18), new(14, 22)]);
                g.DrawLines(glyph, [new(20, 14), new(24, 18), new(20, 22)]); break;
            case "Data":
                g.DrawEllipse(glyph, 11, 13, 12, 4); g.DrawArc(glyph, 11, 15, 12, 7, 0, 180); g.DrawArc(glyph, 11, 19, 12, 6, 0, 180); break;
            case "Assembly": DrawCube(g, new RectangleF(10, 12, 14, 13), accent); break;
            case "Markup":
                g.DrawLines(glyph, [new(14, 14), new(10, 18), new(14, 22)]); g.DrawLine(glyph, 19, 13, 16, 23);
                g.DrawLines(glyph, [new(21, 14), new(25, 18), new(21, 22)]); break;
            case "Style": g.DrawString("#", new Font("Segoe UI", 10, FontStyle.Bold), Brush(accent), 11, 11); break;
            case "Shader": g.DrawLine(glyph, 11, 23, 22, 12); g.DrawLine(glyph, 12, 15, 20, 23); break;
            case "Audio":
                g.DrawLine(glyph, 14, 15, 14, 23); g.DrawLine(glyph, 14, 15, 22, 13); g.DrawLine(glyph, 22, 13, 22, 21);
                g.DrawEllipse(glyph, 10, 21, 4, 3); g.DrawEllipse(glyph, 18, 19, 4, 3); break;
            case "Animation":
                g.DrawLine(glyph, 11, 14, 11, 24); g.DrawEllipse(glyph, 9, 12, 4, 4); g.DrawEllipse(glyph, 9, 22, 4, 4);
                g.FillPolygon(Brush(accent), [new(17, 13), new(24, 18), new(17, 23)]); break;
            case "Font": g.DrawString("T", new Font("Segoe UI", 11, FontStyle.Bold), Brush(accent), 11, 10); break;
        }
    }

    private static void DrawTextureAsset(Graphics g)
    {
        using var frame = Pen(Line, 1.5f);
        using var sky = Brush(Color.FromArgb(190, 75, 126, 164));
        using var ground = Brush(Color.FromArgb(235, 82, 151, 112));
        using var sun = Brush(Color.FromArgb(255, 240, 194, 92));
        g.FillRectangle(sky, 4, 5, 24, 22);
        g.FillPolygon(ground, [new PointF(4, 25), new PointF(11, 16), new PointF(16, 21),
            new PointF(21, 13), new PointF(28, 21), new PointF(28, 27), new PointF(4, 27)]);
        g.FillEllipse(sun, 20, 8, 4, 4);
        g.DrawRectangle(frame, 4, 5, 24, 22);
    }

    private static void DrawMaterialAsset(Graphics g)
    {
        using var shadow = Brush(Color.FromArgb(110, 0, 0, 0));
        using var sphere = Brush(Color.FromArgb(255, 137, 94, 174));
        using var highlight = Brush(Color.FromArgb(210, 224, 204, 239));
        using var edge = Pen(Color.FromArgb(245, 201, 171, 224), 1.4f);
        g.FillEllipse(shadow, 7, 25, 19, 3);
        g.FillEllipse(sphere, 5, 4, 23, 23);
        g.FillEllipse(highlight, 9, 8, 6, 5);
        g.DrawArc(edge, 6, 5, 21, 21, 25, 300);
    }

    private static void DrawModelAsset(Graphics g)
    {
        using var fill = Brush(Color.FromArgb(72, Green));
        g.FillPolygon(fill, [new PointF(16, 3), new PointF(28, 9), new PointF(16, 16), new PointF(4, 9)]);
        g.FillPolygon(fill, [new PointF(4, 9), new PointF(16, 16), new PointF(16, 29), new PointF(4, 22)]);
        DrawCube(g, new RectangleF(4, 3, 24, 26), Green);
    }

    private static void DrawPrefabAsset(Graphics g)
    {
        using var fill = Brush(Color.FromArgb(90, Blue));
        g.FillPolygon(fill, [new PointF(16, 3), new PointF(28, 9), new PointF(16, 16), new PointF(4, 9)]);
        g.FillPolygon(fill, [new PointF(16, 16), new PointF(28, 9), new PointF(28, 22), new PointF(16, 29)]);
        DrawCube(g, new RectangleF(4, 3, 24, 26), Blue);
    }

    private static void DrawSceneAsset(Graphics g)
    {
        using var frame = Pen(Blue, 1.5f);
        using var horizon = Pen(Color.FromArgb(235, 113, 194, 147), 1.7f);
        using var sun = Brush(Color.FromArgb(255, 239, 181, 73));
        using var background = Brush(Color.FromArgb(50, Blue));
        g.FillRectangle(background, 3, 5, 26, 22);
        g.DrawRectangle(frame, 3, 5, 26, 22);
        g.FillEllipse(sun, 21, 8, 4, 4);
        g.DrawLines(horizon, [new PointF(4, 24), new PointF(11, 16), new PointF(16, 20),
            new PointF(22, 13), new PointF(28, 20)]);
        DrawCube(g, new RectangleF(10, 16, 10, 9), Blue);
    }

    private static void DrawAtlasAsset(Graphics g)
    {
        using var frame = Pen(Line, 1.4f);
        using var blue = Brush(Color.FromArgb(220, Blue));
        using var green = Brush(Color.FromArgb(220, Green));
        using var purple = Brush(Color.FromArgb(220, Purple));
        using var orange = Brush(Color.FromArgb(220, Orange));
        g.DrawRectangle(frame, 3, 3, 26, 26);
        g.FillRectangle(blue, 6, 6, 8, 8);
        g.FillRectangle(green, 18, 6, 8, 8);
        g.FillRectangle(purple, 6, 18, 8, 8);
        g.FillRectangle(orange, 18, 18, 8, 8);
    }

    private static void DrawSkinAsset(Graphics g)
    {
        using var frame = Pen(Line, 1.4f);
        using var panel = Brush(Color.FromArgb(105, Line));
        using var accent = Brush(Color.FromArgb(230, Blue));
        using var secondary = Brush(Color.FromArgb(210, Purple));
        using var muted = Brush(Color.FromArgb(180, Dim));
        g.DrawRectangle(frame, 3, 4, 26, 24);
        g.FillRectangle(panel, 4, 5, 24, 5);
        g.FillEllipse(accent, 6, 6, 3, 3);
        g.FillRectangle(secondary, 6, 14, 8, 5);
        g.FillRectangle(accent, 17, 14, 9, 5);
        g.FillRectangle(muted, 6, 22, 20, 3);
    }

    private static void DrawFolder(Graphics g, bool open, bool empty)
    {
        using var outline = Pen(Color.FromArgb(245, 205, 207, 204), 1.3f);
        using var fill = Brush(empty ? Color.FromArgb(92, Folder) : Color.FromArgb(230, Folder));
        var path = new GraphicsPath();
        if (open)
            path.AddPolygon([new(3, 11), new(13, 11), new(15, 14), new(30, 14), new(26, 27), new(4, 27)]);
        else path.AddPolygon([new(3, 9), new(12, 9), new(15, 13), new(29, 13), new(29, 27), new(3, 27)]);
        g.FillPath(fill, path); g.DrawPath(outline, path);
        if (empty) { using var mark = Pen(Dim, 1.2f); g.DrawLine(mark, 11, 19, 21, 19); }
    }

    private static void DrawComponent(Graphics g, string kind)
    {
        switch (kind)
        {
            case "Transform": DrawAxes(g); break;
            case "Camera": DrawCamera(g); break;
            case "Script": DrawScriptBadge(g); break;
            case "GameObject": DrawCube(g, new RectangleF(5, 6, 22, 20), Blue); break;
            case "SpriteRenderer": DrawTextureAsset(g); break;
            case "ParticleSystem2D": DrawParticles(g); break;
            case "Rigidbody2D": DrawRigidbody(g); break;
            case "Collider2D": DrawCollider(g); break;
            case "Animator": DrawAnimator(g); break;
            case "Tilemap": DrawTilemap(g); break;
            case "UIDocument": DrawUiDocument(g); break;
            case "Navigation": DrawNavigation(g); break;
            default: DrawComponentBadge(g); break;
        }
    }

    private static void DrawToolbar(Graphics g, string kind)
    {
        using var line = Pen(Line, 2f);
        switch (kind)
        {
            case "Add": g.DrawLine(line, 16, 7, 16, 25); g.DrawLine(line, 7, 16, 25, 16); break;
            case "AddDropdown": g.DrawLine(line, 13, 7, 13, 23); g.DrawLine(line, 5, 15, 21, 15); g.FillPolygon(Brush(Line), [new PointF(23, 13), new PointF(30, 13), new PointF(26.5f, 18)]); break;
            case "Minus": g.DrawLine(line, 7, 16, 25, 16); break;
            case "New": g.DrawRectangle(line, 7, 5, 18, 22); g.DrawLine(Pen(Blue, 2), 16, 11, 16, 22); g.DrawLine(Pen(Blue, 2), 11, 16, 21, 16); break;
            case "Save": g.DrawRectangle(line, 6, 5, 20, 22); g.FillRectangle(Brush(Blue), 10, 6, 12, 7); g.DrawRectangle(line, 10, 18, 12, 9); break;
            case "Delete": g.DrawRectangle(line, 10, 10, 12, 16); g.DrawLine(line, 8, 8, 24, 8); g.DrawLine(line, 13, 5, 19, 5); break;
            case "Search": g.DrawEllipse(line, 6, 6, 14, 14); g.DrawLine(line, 18, 18, 26, 26); break;
            case "Clear": g.DrawLine(Pen(Red, 2.2f), 8, 8, 24, 24); g.DrawLine(Pen(Red, 2.2f), 24, 8, 8, 24); break;
            case "Close": g.DrawLine(line, 9, 9, 23, 23); g.DrawLine(line, 23, 9, 9, 23); break;
            case "Undo": DrawUndoArrow(g, false); break;
            case "Redo": DrawUndoArrow(g, true); break;
            case "UndoHistory": DrawUndoHistory(g); break;
            case "Layout": DrawLayout(g); break;
            case "Dropdown": g.FillPolygon(Brush(Line), [new(8, 12), new(24, 12), new(16, 21)]); break;
            case "Filter": g.DrawLines(line, [new(5, 7), new(27, 7), new(19, 16), new(19, 25), new(13, 22), new(13, 16), new(5, 7)]); break;
            case "Import": DrawTransfer(g, false); break;
            case "Export": DrawTransfer(g, true); break;
            case "Duplicate": g.DrawRectangle(Pen(Dim, 1.6f), 5, 8, 15, 18); g.DrawRectangle(line, 11, 5, 16, 19); break;
            case "Refresh": g.DrawArc(line, 6, 6, 20, 20, 35, 285); g.FillPolygon(Brush(Line), [new(23, 5), new(28, 8), new(23, 11)]); break;
            case "Browse": DrawFolderMini(g, 5, 10); g.DrawEllipse(Pen(Blue, 1.8f), 17, 17, 8, 8); g.DrawLine(Pen(Blue, 1.8f), 24, 24, 28, 28); break;
            case "OpenFolder": DrawFolderMini(g, 7, 9); g.DrawLine(Pen(Blue, 1.8f), 12, 18, 20, 18); g.DrawLine(Pen(Blue, 1.8f), 16, 14, 16, 22); break;
            case "Settings": DrawGear(g, 16, 16); break;
            case "Lock": DrawLock(g, false); break;
            case "Unlock": DrawLock(g, true); break;
            case "Visible": DrawEye(g, false); break;
            case "Hidden": DrawEye(g, true); break;
            case "Info": g.DrawEllipse(Pen(Blue, 1.8f), 6, 6, 20, 20); g.DrawLine(Pen(Blue, 2), 16, 14, 16, 22); g.FillEllipse(Brush(Blue), 14.5f, 9, 3, 3); break;
            case "Warning": g.FillPolygon(Brush(Orange), [new(16, 4), new(29, 27), new(3, 27)]); g.DrawLine(Pen(Color.FromArgb(255, 50, 50, 50), 2), 16, 11, 16, 19); g.FillEllipse(Brush(Color.FromArgb(255, 50, 50, 50)), 14.5f, 22, 3, 3); break;
            case "Error": g.FillEllipse(Brush(Red), 5, 5, 22, 22); g.DrawLine(Pen(Color.White, 2), 10, 10, 22, 22); g.DrawLine(Pen(Color.White, 2), 22, 10, 10, 22); break;
            case "Check": g.DrawLines(Pen(Green, 2.4f), [new(6, 16), new(13, 23), new(27, 8)]); break;
            case "Send": g.FillPolygon(Brush(Blue), [new(4, 6), new(28, 16), new(4, 26), new(9, 17)]); break;
            case "Html": g.DrawLines(Pen(Cyan, 2), [new(13, 8), new(5, 16), new(13, 24)]); g.DrawLine(Pen(Cyan, 2), 19, 6, 14, 26); g.DrawLines(Pen(Cyan, 2), [new(21, 8), new(29, 16), new(21, 24)]); break;
            case "Container": g.DrawRectangle(line, 5, 6, 22, 20); g.DrawRectangle(Pen(Blue, 1.5f), 9, 10, 14, 12); break;
            case "Label": g.DrawString("T", new Font("Segoe UI", 14, FontStyle.Bold), Brush(Line), 8, 5); break;
            case "Button": g.DrawRectangle(line, 5, 9, 22, 14); g.DrawLine(Pen(Blue, 1.5f), 11, 16, 21, 16); break;
            case "Field": g.DrawRectangle(line, 4, 9, 24, 14); g.DrawLine(Pen(Blue, 1.5f), 9, 18, 22, 18); g.DrawLine(Pen(Blue, 1.5f), 9, 13, 9, 19); break;
            case "Up": g.DrawLine(line, 16, 26, 16, 7); g.DrawLines(line, [new(8, 15), new(16, 7), new(24, 15)]); break;
            case "View": DrawHand(g); break;
            case "Move": DrawAxes(g); break;
            case "Rotate": g.DrawArc(line, 6, 6, 20, 20, 35, 285); g.FillPolygon(Brush(Line), [new(23, 5), new(28, 8), new(23, 11)]); break;
            case "Scale": g.DrawLine(line, 8, 24, 24, 8); g.DrawRectangle(line, 20, 5, 7, 7); g.DrawRectangle(line, 5, 20, 7, 7); break;
            case "Rect": g.DrawRectangle(line, 7, 8, 18, 16); g.FillRectangle(Brush(Blue), 5, 6, 4, 4); g.FillRectangle(Brush(Blue), 23, 22, 4, 4); break;
            case "Play": g.FillPolygon(Brush(Line), [new(11, 7), new(25, 16), new(11, 25)]); break;
            case "Stop": g.FillRectangle(Brush(Red), 9, 9, 14, 14); break;
            case "Pause": g.FillRectangle(Brush(Line), 9, 7, 5, 18); g.FillRectangle(Brush(Line), 18, 7, 5, 18); break;
            case "Step": g.FillPolygon(Brush(Line), [new(8, 8), new(20, 16), new(8, 24)]); g.FillRectangle(Brush(Line), 22, 8, 3, 16); break;
            case "FoldoutClosed": g.FillPolygon(Brush(Line), [new(11, 7), new(22, 16), new(11, 25)]); break;
            case "FoldoutOpen": g.FillPolygon(Brush(Line), [new(7, 11), new(25, 11), new(16, 22)]); break;
            case "More": g.FillEllipse(Brush(Line), 6, 14, 4, 4); g.FillEllipse(Brush(Line), 14, 14, 4, 4); g.FillEllipse(Brush(Line), 22, 14, 4, 4); break;
        }
    }

    private static void DrawHand(Graphics g)
    {
        using var outline = Pen(Line, 1.7f);
        using var fill = Brush(Color.FromArgb(52, Line));
        var hand = new GraphicsPath();
        hand.AddPolygon([
            new PointF(9, 16), new PointF(9, 10), new PointF(12, 10), new PointF(12, 15),
            new PointF(12, 6), new PointF(15, 6), new PointF(15, 14), new PointF(15, 5),
            new PointF(18, 5), new PointF(18, 14), new PointF(18, 7), new PointF(21, 7),
            new PointF(21, 16), new PointF(24, 13), new PointF(27, 15), new PointF(23, 25),
            new PointF(13, 27), new PointF(7, 19)
        ]);
        g.FillPath(fill, hand);
        g.DrawPath(outline, hand);
    }

    private static void DrawUndoArrow(Graphics g, bool redo)
    {
        using var line = Pen(Line, 2f);
        var direction = redo ? -1 : 1;
        g.DrawArc(line, 7, 8, 19, 17, redo ? 190 : -10, 220);
        var tipX = redo ? 26 : 6;
        g.FillPolygon(Brush(Line),
        [
            new PointF(tipX, 8), new PointF(tipX + direction * 7, 5),
            new PointF(tipX + direction * 5, 13)
        ]);
    }

    private static void DrawUndoHistory(Graphics g)
    {
        using var line = Pen(Line, 1.8f);
        g.DrawArc(line, 6, 6, 21, 21, 40, 285);
        g.FillPolygon(Brush(Line), [new PointF(5, 7), new PointF(11, 5), new PointF(9, 12)]);
        g.DrawLine(line, 16, 10, 16, 17);
        g.DrawLine(line, 16, 17, 21, 20);
    }

    private static void DrawLayout(Graphics g)
    {
        using var frame = Pen(Line, 1.5f);
        using var active = Brush(Color.FromArgb(105, Blue));
        g.FillRectangle(active, 5, 5, 10, 22);
        g.DrawRectangle(frame, 5, 5, 22, 22);
        g.DrawLine(frame, 15, 5, 15, 27);
        g.DrawLine(frame, 15, 16, 27, 16);
    }

    private static void DrawTransfer(Graphics g, bool export)
    {
        using var line = Pen(Line, 1.8f);
        g.DrawRectangle(line, 6, 17, 20, 10);
        var tipY = export ? 5 : 24;
        var tailY = export ? 21 : 8;
        g.DrawLine(line, 16, tailY, 16, tipY);
        g.DrawLines(line, export
            ? [new PointF(10, 11), new PointF(16, 5), new PointF(22, 11)]
            : [new PointF(10, 18), new PointF(16, 24), new PointF(22, 18)]);
    }

    private static void DrawParticles(Graphics g)
    {
        using var orbit = Pen(Dim, 1.2f);
        g.DrawEllipse(orbit, 7, 7, 18, 18);
        g.FillEllipse(Brush(Blue), 13, 13, 6, 6);
        g.FillEllipse(Brush(Orange), 5, 13, 4, 4);
        g.FillEllipse(Brush(Green), 23, 9, 4, 4);
        g.FillEllipse(Brush(Purple), 20, 23, 3, 3);
    }

    private static void DrawRigidbody(Graphics g)
    {
        using var line = Pen(Line, 1.7f);
        g.DrawEllipse(line, 7, 7, 18, 18);
        g.DrawString("R", new Font("Segoe UI", 10, FontStyle.Bold), Brush(Blue), 10, 8);
    }

    private static void DrawCollider(Graphics g)
    {
        g.DrawRectangle(Pen(Blue, 1.8f), 5, 7, 18, 18);
        g.DrawEllipse(Pen(Green, 1.8f), 11, 10, 16, 16);
    }

    private static void DrawAnimator(Graphics g)
    {
        using var line = Pen(Line, 1.5f);
        g.DrawLine(line, 9, 10, 22, 16);
        g.DrawLine(line, 22, 16, 10, 24);
        g.FillEllipse(Brush(Blue), 5, 6, 8, 8);
        g.FillEllipse(Brush(Orange), 19, 12, 8, 8);
        g.FillEllipse(Brush(Green), 6, 20, 8, 8);
    }

    private static void DrawTilemap(Graphics g)
    {
        using var line = Pen(Line, 1.4f);
        g.DrawRectangle(line, 5, 5, 22, 22);
        for (var coordinate = 12; coordinate <= 20; coordinate += 8)
        {
            g.DrawLine(line, coordinate, 5, coordinate, 27);
            g.DrawLine(line, 5, coordinate, 27, coordinate);
        }
        g.FillRectangle(Brush(Color.FromArgb(110, Green)), 13, 13, 7, 7);
    }

    private static void DrawUiDocument(Graphics g)
    {
        using var line = Pen(Line, 1.5f);
        g.DrawRectangle(line, 4, 5, 24, 22);
        g.DrawLine(line, 4, 11, 28, 11);
        g.DrawLine(line, 11, 11, 11, 27);
        g.FillRectangle(Brush(Color.FromArgb(100, Blue)), 14, 15, 10, 4);
    }

    private static void DrawNavigation(Graphics g)
    {
        using var route = Pen(Blue, 2.1f);
        g.DrawBezier(route, 5, 25, 10, 5, 21, 28, 27, 8);
        g.FillEllipse(Brush(Orange), 3, 23, 5, 5);
        g.FillEllipse(Brush(Green), 24, 5, 5, 5);
    }

    private static void DrawWindow(Graphics g, string kind)
    {
        using var frame = Pen(Dim, 1.4f);
        using var line = Pen(Line, 1.7f);
        g.DrawRectangle(frame, 4, 5, 24, 22); g.DrawLine(frame, 4, 10, 28, 10);
        switch (kind)
        {
            case "Hierarchy": g.DrawLine(line, 9, 14, 9, 23); g.DrawLine(line, 9, 17, 14, 17); g.DrawLine(line, 9, 22, 14, 22); g.FillRectangle(Brush(Blue), 15, 14, 8, 5); break;
            case "Inspector": g.DrawRectangle(line, 9, 14, 4, 4); g.DrawRectangle(line, 9, 21, 4, 4); g.DrawLine(line, 16, 16, 24, 16); g.DrawLine(line, 16, 23, 24, 23); break;
            case "Project": DrawFolderMini(g, 8, 14); break;
            case "Console": g.DrawLines(line, [new(9, 15), new(13, 18), new(9, 21)]); g.DrawLine(line, 15, 22, 23, 22); break;
            case "Game": g.DrawEllipse(line, 8, 15, 16, 8); g.DrawLine(line, 12, 19, 16, 19); g.DrawLine(line, 14, 17, 14, 21); g.FillEllipse(Brush(Green), 20, 17, 2, 2); break;
            case "Scene": case "SceneCamera": DrawCube(g, new RectangleF(9, 13, 14, 12), Blue); break;
            case "PackageManager": DrawCube(g, new RectangleF(9, 13, 14, 12), Orange); break;
            case "Preferences": case "ProjectSettings": DrawGear(g, 16, 19); break;
            case "Shortcuts": g.DrawRectangle(line, 8, 14, 16, 10); g.DrawLine(line, 11, 18, 21, 18); break;
            case "EditorStatus": g.DrawLine(line, 8, 22, 13, 17); g.DrawLine(line, 13, 17, 17, 20); g.DrawLine(line, 17, 20, 24, 13); break;
            case "Codex": g.DrawLines(line, [new(12, 14), new(8, 19), new(12, 24)]); g.DrawLines(line, [new(20, 14), new(24, 19), new(20, 24)]); break;
            case "UIBuilder": g.DrawRectangle(line, 8, 13, 16, 12); g.DrawLine(line, 14, 13, 14, 25); g.DrawLine(line, 14, 18, 24, 18); break;
            case "HtmlConverter": g.DrawLines(line, [new(12, 14), new(8, 19), new(12, 24)]); g.DrawLine(line, 15, 24, 19, 14); g.DrawLines(line, [new(21, 14), new(25, 19), new(21, 24)]); break;
            default: g.DrawRectangle(line, 8, 13, 16, 12); break;
        }
    }

    private static void DrawPackageIcon(string path, string kind) => Save(path, g =>
    {
        switch (kind)
        {
            case "Animation": DrawAsset(g, "Animation"); break;
            case "Navigation2D": using (var p = Pen(Blue, 2.4f)) { g.DrawBezier(p, 5, 25, 11, 4, 19, 29, 27, 8); } g.FillEllipse(Brush(Orange), 3, 23, 5, 5); g.FillEllipse(Brush(Green), 24, 5, 5, 5); break;
            case "Physics2D": g.FillEllipse(Brush(Green), 9, 7, 14, 14); using (var p = Pen(Line, 1.8f)) { g.DrawLine(p, 4, 25, 28, 25); } break;
        }
    });

    private static void DrawCube(Graphics g, RectangleF bounds, Color color)
    {
        using var pen = Pen(color, 1.7f);
        var cx = bounds.X + bounds.Width / 2; var top = bounds.Y; var mid = bounds.Y + bounds.Height * .34f;
        var bottom = bounds.Bottom; var left = bounds.Left; var right = bounds.Right;
        g.DrawPolygon(pen, [new PointF(cx, top), new PointF(right, mid),
            new PointF(cx, bottom), new PointF(left, mid)]);
        g.DrawLine(pen, cx, top, cx, bottom); g.DrawLine(pen, left, mid, cx, mid + bounds.Height * .34f); g.DrawLine(pen, right, mid, cx, mid + bounds.Height * .34f);
    }
    private static void DrawAxes(Graphics g)
    {
        using var red = Pen(Red, 2); using var green = Pen(Green, 2); using var blue = Pen(Blue, 2);
        g.DrawLine(red, 16, 17, 27, 17); g.DrawLine(green, 16, 17, 16, 5); g.DrawLine(blue, 16, 17, 8, 25);
        g.FillPolygon(Brush(Red), [new(27, 17), new(23, 14), new(23, 20)]); g.FillPolygon(Brush(Green), [new(16, 5), new(13, 9), new(19, 9)]); g.FillPolygon(Brush(Blue), [new(8, 25), new(8, 20), new(12, 23)]);
    }
    private static void DrawCamera(Graphics g) { using var p = Pen(Line, 1.8f); g.DrawRectangle(p, 5, 10, 17, 14); g.DrawPolygon(p, [new(22, 14), new(29, 10), new(29, 24), new(22, 20)]); g.FillEllipse(Brush(Blue), 11, 15, 5, 5); }
    private static void DrawScriptBadge(Graphics g) { using var p = Pen(Line, 1.5f); g.DrawRectangle(p, 5, 5, 22, 22); g.DrawLines(Pen(Cyan, 2), [new(13, 10), new(8, 16), new(13, 22)]); g.DrawLines(Pen(Cyan, 2), [new(19, 10), new(24, 16), new(19, 22)]); }
    private static void DrawComponentBadge(Graphics g) { using var p = Pen(Line, 1.5f); g.DrawRectangle(p, 5, 5, 22, 22); DrawGear(g, 16, 16); }
    private static void DrawGear(Graphics g, float x, float y) { using var p = Pen(Line, 1.8f); g.DrawEllipse(p, x - 6, y - 6, 12, 12); g.DrawEllipse(p, x - 2, y - 2, 4, 4); for (var a = 0; a < 8; a++) { var r = a * Math.PI / 4; g.DrawLine(p, x + (float)Math.Cos(r) * 7, y + (float)Math.Sin(r) * 7, x + (float)Math.Cos(r) * 9, y + (float)Math.Sin(r) * 9); } }
    private static void DrawFolderMini(Graphics g, float x, float y) { using var p = Pen(Line, 1.5f); using var b = Brush(Color.FromArgb(170, Folder)); var path = new GraphicsPath(); path.AddPolygon([new PointF(x, y + 2), new PointF(x + 7, y + 2), new PointF(x + 9, y + 5), new PointF(x + 16, y + 5), new PointF(x + 16, y + 12), new PointF(x, y + 12)]); g.FillPath(b, path); g.DrawPath(p, path); }
    private static void DrawLock(Graphics g, bool open) { using var p = Pen(Line, 1.8f); g.DrawRectangle(p, 8, 14, 16, 13); if (open) g.DrawArc(p, 13, 5, 10, 14, 160, 220); else g.DrawArc(p, 11, 5, 10, 14, 180, 180); g.FillEllipse(Brush(Blue), 14, 19, 4, 4); }
    private static void DrawEye(Graphics g, bool hidden) { using var p = Pen(Line, 1.8f); g.DrawBezier(p, 3, 16, 9, 6, 23, 6, 29, 16); g.DrawBezier(p, 3, 16, 9, 26, 23, 26, 29, 16); g.FillEllipse(Brush(Blue), 12, 12, 8, 8); if (hidden) g.DrawLine(Pen(Red, 2.2f), 5, 5, 27, 27); }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }
}
