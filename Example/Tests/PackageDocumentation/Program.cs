using System.Reflection;
using BEngine.Animation;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Navigation2D;
using BEngine.Physics2D;
using BEngine.PropertyAttributes;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Rendering.Rhi.Vulkan;
using BEngine.TiledMap;
using BEngine.UIElements;

namespace BEngine.ExampleTests.PackageDocumentation;

internal static class Program
{
    private const int Width = 1100;
    private const int Height = 640;
    private static readonly GpuCanvasRect FullClip = new(0, 0, Width, Height);

    private static readonly PackageShot[] Shots =
    [
        new("Core", "Core",
            ["Core Background", "Interactive Core Sprite", "Companion Sprite", "Example Camera 2D"],
            ["Transform", "Sprite Renderer", "Core Runtime Example", "Camera 2D"],
            BuildCore),
        new("Animation", "Packages/Animation",
            ["Animation Background", "Bounce Start Guide", "Animated Sprite", "Animation Camera 2D"],
            ["Sprite Renderer", "Animator", "Runtime Controller", "Animation Event"],
            BuildAnimation),
        new("Navigation2D", "Packages/Navigation2D",
            ["Navigation Surface", "Carving Obstacle", "Navigation Agent", "Navigation Camera 2D"],
            ["Navigation Surface 2D", "Navigation Obstacle 2D", "Navigation Agent 2D", "Bake / Clear"],
            BuildNavigation),
        new("Physics2D", "Packages/Physics2D",
            ["Physics Arena", "Dynamic Body", "Trigger Zone", "Physics Camera 2D"],
            ["Rigidbody 2D", "Box Collider 2D", "Circle Collider 2D", "Queries / Contacts"],
            BuildPhysics),
        new("PropertyAttributes", "Packages/PropertyAttributes",
            ["Inspector Background", "Attributes Showcase", "Progress Track", "Inspector Camera 2D"],
            ["Conditional Fields", "Range + Progress", "Path Pickers", "Custom Property Drawer"],
            BuildPropertyAttributes),
        new("TiledMap", "Packages/TiledMap",
            ["Atlas Tilemap", "Palette Preview", "Painted Cells", "Tilemap Camera 2D"],
            ["Tilemap", "Tilemap Renderer", "Tile Palette", "Sort + Camera Culling"],
            BuildTiledMap),
        new("UIElements", "Packages/UIElements",
            ["Runtime World", "HUD Document", "Controls Gallery", "UI Camera 2D"],
            ["UI Document", "UXML Source Asset", "Scale With Screen Size", "UI Sorting Layer"],
            BuildUIElements)
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = FindRepositoryRoot();
            ValidateRuntimeTypeCache();
            if (args.Contains("--generate", StringComparer.OrdinalIgnoreCase))
                GenerateScreenshots(root);
            ValidateDocumentation(root);
            Console.WriteLine($"PACKAGE_DOCUMENTATION_OK|packages={Shots.Length},sections,images,skills");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PACKAGE_DOCUMENTATION_FAILED|{exception}");
            return 1;
        }
    }

    private static void ValidateRuntimeTypeCache()
    {
        RuntimeTypeCache.Warmup();
        var scene = NewScene("Package Cache");
        var animation = scene.CreateGameObject("Animation");
        animation.AddComponent<Animator>();
        var navigation = scene.CreateGameObject("Navigation");
        navigation.AddComponent<NavigationSurface2D>();
        navigation.AddComponent<NavigationAgent2D>();
        var physics = scene.CreateGameObject("Physics");
        physics.AddComponent<Rigidbody2D>();
        physics.AddComponent<BoxCollider2D>();
        var tiledMap = scene.CreateGameObject("TiledMap");
        tiledMap.AddComponent<Tilemap>();
        tiledMap.AddComponent<TilemapRenderer>();
        var ui = scene.CreateGameObject("UIElements");
        ui.AddComponent<UIDocument>();
        var attributes = scene.CreateGameObject("Property Attributes");
        attributes.AddComponent<DocumentationAttributeShowcase>();

        var expected = new[]
        {
            typeof(Camera2D), typeof(Animator), typeof(NavigationSurface2D), typeof(NavigationAgent2D),
            typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(Tilemap), typeof(TilemapRenderer),
            typeof(UIDocument), typeof(DocumentationAttributeShowcase)
        };
        var cached = RuntimeTypeCache.GetAllTypes().ToHashSet();
        Require(expected.All(cached.Contains),
            "RuntimeTypeCache did not retain every loaded package component type.");
    }

    private static void GenerateScreenshots(string root)
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form
        {
            Text = "BEngine Package Documentation Capture",
            ClientSize = new Size(Width, Height),
            ShowInTaskbar = false,
            TopMost = true,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(60, 60)
        };
        form.Show();

        Resources.RegisterResourceRoot(Path.Combine(root, "src", "Core"));
        foreach (var shot in Shots.Where(item => item.SourceDirectory.StartsWith(
                     "Packages/", StringComparison.Ordinal)))
            Resources.RegisterResourceRoot(Path.Combine(root, "src", shot.SourceDirectory));

        var factory = new GraphicsDeviceFactory();
        factory.RegisterProvider(new VulkanGraphicsDeviceProvider(
            form.Handle, GetModuleHandle(null), Width, Height, vsync: false));
        using var device = (IGraphicsPresentationDevice)factory.CreateDevice(GraphicsBackendDefaults.Default);
        using var sceneRenderer = new PortableSceneRenderer(device);
        var resolverType = typeof(GUI).Assembly.GetType(
            "BEngine.Editor.EditorGpuCanvasResourceResolver", throwOnError: true)!;
        var resolver = (IGpuCanvasResourceResolver)resolverType
            .GetProperty("Shared", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        using var canvas = new GpuCanvasRenderer(device, resourceResolver: resolver);

        foreach (var shot in Shots)
        {
            var scene = shot.BuildScene();
            var camera = scene.QueryComponents<Camera2D>().Single();
            var sceneViewport = new GraphicsRect(218, 74, 616, 514);

            device.BeginFrame(Width, Height);
            device.Clear(GraphicsClearFlags.Color, new System.Numerics.Vector4(0.08f, 0.09f, 0.1f, 1));
            canvas.Render(BuildChrome(), Width, Height);
            sceneRenderer.RenderViewport([scene], scene, RenderCamera.From(camera), sceneViewport,
                initializeColor: true, drawGrid: true, drawUi: true, drawExtensions: true);
            canvas.Render(BuildLabels(shot), Width, Height);
            device.Present();
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(140);

            var destination = Path.Combine(root, "src", shot.SourceDirectory,
                "Editor", "Doc", "images", "overview.png");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var capture = CaptureClient(form);
            capture.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
            Require(capture.Width == Width && capture.Height == Height,
                $"{shot.Name} capture has an unexpected size.");
            Console.WriteLine($"PACKAGE_DOCUMENTATION_IMAGE|{shot.Name}|{destination}");
        }
        form.Close();
    }

    private static GpuCanvasCommand[] BuildChrome()
    {
        var commands = new List<GpuCanvasCommand>
        {
            Solid(0, 0, Width, Height, 31, 34, 38),
            Solid(0, 0, Width, 36, 40, 43, 47),
            Solid(0, 36, Width, 38, 34, 37, 41),
            Solid(0, 74, 218, 514, 43, 46, 50),
            Solid(834, 74, 266, 514, 43, 46, 50),
            Solid(0, 588, Width, 52, 35, 38, 42),
            Solid(217, 74, 1, 514, 20, 22, 25),
            Solid(834, 74, 1, 514, 20, 22, 25),
            Solid(0, 587, Width, 1, 20, 22, 25),
            Solid(1082, 50, 8, 8, 44, 194, 174)
        };
        return commands.ToArray();
    }

    private static GpuCanvasCommand[] BuildLabels(PackageShot shot)
    {
        var commands = new List<GpuCanvasCommand>
        {
            Text(14, 7, 310, 24, "BEngine 2D Package Guide", 17, 235, 238, 240),
            Text(230, 43, 200, 23, "Scene", 15, 232, 235, 238),
            Text(14, 82, 190, 24, "Hierarchy", 15, 232, 235, 238),
            Text(850, 82, 235, 24, "Inspector", 15, 232, 235, 238),
            Text(14, 600, 620, 24, $"{shot.Name} / complete authored scene / edit-mode inspectable", 14,
                178, 190, 198),
            Text(851, 43, 230, 23, shot.Name, 15, 44, 194, 174)
        };

        for (var index = 0; index < shot.Hierarchy.Length; index++)
        {
            var y = 118 + index * 34;
            if (index == 1) commands.Add(Solid(8, y - 4, 202, 29, 49, 86, 101));
            commands.Add(Text(20 + (index == 0 ? 0 : 12), y, 184, 22,
                index == 0 ? shot.Hierarchy[index] : $"> {shot.Hierarchy[index]}", 13,
                index == 1 ? (byte)238 : (byte)198,
                index == 1 ? (byte)243 : (byte)205,
                index == 1 ? (byte)244 : (byte)210));
        }

        for (var index = 0; index < shot.Inspector.Length; index++)
        {
            var y = 128 + index * 72;
            commands.Add(Solid(846, y - 8, 242, 56, 37, 40, 44));
            commands.Add(Solid(846, y - 8, 3, 56,
                index == 0 ? (byte)44 : (byte)76,
                index == 0 ? (byte)194 : (byte)82,
                index == 0 ? (byte)174 : (byte)88));
            commands.Add(Text(860, y, 214, 22, shot.Inspector[index], 13, 224, 228, 231));
            commands.Add(Text(860, y + 23, 214, 18,
                index == shot.Inspector.Length - 1 ? "Ready to extend" : "Serialized in scene",
                11, 138, 149, 156));
        }

        commands.Add(Solid(230, 84, 152, 27, 32, 35, 39, 225));
        commands.Add(Text(240, 88, 136, 20, "2D  |  Gizmos On", 12, 200, 207, 212));
        commands.Add(Solid(646, 548, 174, 27, 32, 35, 39, 225));
        commands.Add(Text(657, 552, 154, 20, "Camera  |  Layer 2", 12, 200, 207, 212));
        return commands.ToArray();
    }

    private static Scene BuildCore()
    {
        var scene = NewScene("Core");
        AddSprite(scene, "Core Background", Vector2.zero, V(12, 8), Hex("15212B"), -20);
        AddSprite(scene, "Interactive Core Sprite", V(-1, 0.4), V(1.5, 1.5),
            Hex("28B8A8"), 10);
        AddSprite(scene, "Companion Sprite", V(2.5, -0.5), V(0.9, 0.9),
            Hex("357CC9"), 8);
        AddSprite(scene, "Render Order Strip", V(0, -2.6), V(8, 0.25),
            Hex("E09A32"), 1);
        return scene;
    }

    private static Scene BuildAnimation()
    {
        var scene = NewScene("Animation");
        AddSprite(scene, "Animation Background", Vector2.zero, V(12, 8), Hex("151C28"), -20);
        for (var index = -3; index <= 3; index++)
            AddSprite(scene, $"Curve {index}", V(index * 1.1, 0.22 * index * index - 1.3),
                V(0.22, 0.22), Hex("E2A646"), 2);
        var actor = AddSprite(scene, "Animated Sprite", V(0, 1.8), V(1.35, 1.35),
            Hex("4D91E8"), 10);
        actor.AddComponent<Animator>();
        return scene;
    }

    private static Scene BuildNavigation()
    {
        var scene = NewScene("Navigation2D");
        var surface = AddSprite(scene, "Navigation Surface", Vector2.zero, V(12, 8),
            Hex("193228"), -20);
        surface.AddComponent<NavigationSurface2D>().buildOnStart = false;
        var obstacle = AddSprite(scene, "Carving Obstacle", Vector2.zero, V(2.5, 2.5),
            Hex("D45D45"), 5);
        obstacle.AddComponent<NavigationObstacle2D>().size = V(2.5, 2.5);
        var agent = AddSprite(scene, "Navigation Agent", V(-4.5, -2.4), V(0.8, 0.8),
            Hex("3C9CE8"), 10);
        agent.AddComponent<NavigationAgent2D>();
        for (var index = 0; index < 6; index++)
            AddSprite(scene, $"Path Point {index}", V(-3.5 + index * 1.45, -1.8 + index * 0.7),
                V(0.18, 0.18), Hex("31C5A9"), 7);
        return scene;
    }

    private static Scene BuildPhysics()
    {
        var scene = NewScene("Physics2D");
        AddSprite(scene, "Physics Arena", Vector2.zero, V(12, 8), Hex("171D27"), -20);
        var floor = AddSprite(scene, "Static Floor", V(0, -2.8), V(10, 0.55),
            Hex("4B5563"), 2);
        floor.AddComponent<BoxCollider2D>().size = V(10, 0.55);
        for (var index = 0; index < 4; index++)
        {
            var body = AddSprite(scene, $"Dynamic Body {index + 1}",
                V(-3 + index * 2, 1.7 - 0.35 * index),
                V(0.9, 0.9), index % 2 == 0 ? Hex("2FC59F") : Hex("E0A13A"), 10);
            body.AddComponent<CircleCollider2D>();
            body.AddComponent<Rigidbody2D>().gravityScale = Fix64.One;
        }
        var trigger = AddSprite(scene, "Trigger Zone", V(3.7, -1), V(2, 2),
            new Color(Fix64.Parse("0.2"), Fix64.Parse("0.55"), Fix64.Parse("0.9"), Fix64.Parse("0.3")), 3);
        trigger.AddComponent<BoxCollider2D>().isTrigger = true;
        return scene;
    }

    private static Scene BuildPropertyAttributes()
    {
        var scene = NewScene("PropertyAttributes");
        AddSprite(scene, "Inspector Background", Vector2.zero, V(12, 8), Hex("161C25"), -20);
        var showcase = AddSprite(scene, "Attributes Showcase", V(-2.7, 0), V(2.2, 5),
            Hex("2C79C7"), 5);
        showcase.AddComponent<DocumentationAttributeShowcase>();
        AddSprite(scene, "Field Group A", V(1.2, 1.7), V(4.3, 1.2),
            Hex("28B8A8"), 4);
        AddSprite(scene, "Field Group B", V(1.2, 0), V(4.3, 1.2),
            Hex("825BC7"), 4);
        AddSprite(scene, "Progress Track", V(1.2, -1.8), V(4.3, 0.65),
            Hex("3A424E"), 3);
        AddSprite(scene, "Progress Value", V(0.3, -1.8), V(2.5, 0.65),
            Hex("E1A142"), 4);
        return scene;
    }

    private static Scene BuildTiledMap()
    {
        var scene = NewScene("TiledMap");
        AddSprite(scene, "Tilemap Background", Vector2.zero, V(12, 8), Hex("131B22"), -20);
        var tilemapObject = scene.CreateGameObject("Atlas Tilemap");
        tilemapObject.AddComponent<Tilemap>();
        tilemapObject.AddComponent<TilemapRenderer>();
        var palette = new[] { Hex("267C72"), Hex("2F91BA"), Hex("D49A3E"), Hex("804FAD") };
        for (var y = -2; y <= 2; y++)
        for (var x = -4; x <= 4; x++)
            AddSprite(scene, $"Cell {x},{y}", V(x * 1.02, y * 1.02),
                V(0.92, 0.92), palette[Math.Abs(x + y * 3) % palette.Length], 2 + y);
        return scene;
    }

    private static Scene BuildUIElements()
    {
        var scene = NewScene("UIElements");
        AddSprite(scene, "Runtime World", Vector2.zero, V(12, 8), Hex("111923"), -20);
        AddSprite(scene, "HUD Panel", V(0, 0), V(8.6, 5.4), Hex("25303B"), 2);
        AddSprite(scene, "Header", V(0, 2), V(7.7, 0.72), Hex("287F77"), 3);
        AddSprite(scene, "Progress", V(-1, -1.6), V(4.6, 0.48), Hex("37BDA4"), 4);
        AddSprite(scene, "Action Button", V(2.9, -1.6), V(1.35, 0.72),
            Hex("D99A3A"), 4);
        var document = scene.CreateGameObject("HUD Document");
        document.layer = SortingLayer.Ui;
        document.AddComponent<UIDocument>().sortingLayer = SortingLayer.Ui;
        return scene;
    }

    private static Scene NewScene(string name)
    {
        var scene = new Scene(name);
        var cameraObject = scene.CreateGameObject($"{name} Camera 2D");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera2D>();
        camera.size = 5;
        camera.cullingMask = SortingLayer.AllMask;
        camera.backgroundColor = Hex("0D1218");
        return scene;
    }

    private static GameObject AddSprite(
        Scene scene, string name, Vector2 position, Vector2 size, Color color, int order)
    {
        var target = scene.CreateGameObject(name);
        target.layer = SortingLayer.Default;
        target.transform.position = position;
        var renderer = target.AddComponent<SpriteRenderer>();
        renderer.size = size;
        renderer.color = color;
        renderer.sortingLayer = SortingLayer.Default;
        renderer.orderInLayer = order;
        return target;
    }

    private static Color Hex(string value) => new(
        Fix64.Parse((Convert.ToInt32(value[..2], 16) / 255d).ToString("0.######",
            System.Globalization.CultureInfo.InvariantCulture)),
        Fix64.Parse((Convert.ToInt32(value[2..4], 16) / 255d).ToString("0.######",
            System.Globalization.CultureInfo.InvariantCulture)),
        Fix64.Parse((Convert.ToInt32(value[4..6], 16) / 255d).ToString("0.######",
            System.Globalization.CultureInfo.InvariantCulture)),
        Fix64.One);

    private static Vector2 V(double x, double y) => new(
        Fix64.Parse(x.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)),
        Fix64.Parse(y.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)));

    private static GpuCanvasCommand Solid(
        float x, float y, float width, float height, byte r, byte g, byte b, byte a = 255) =>
        new(GpuCanvasCommandType.SolidRect, new GpuCanvasRect(x, y, width, height), FullClip,
            new GpuCanvasColor(r, g, b, a));

    private static GpuCanvasCommand Text(
        float x, float y, float width, float height, string text, float size,
        byte r, byte g, byte b) =>
        new(GpuCanvasCommandType.Text, new GpuCanvasRect(x, y, width, height), FullClip,
            new GpuCanvasColor(r, g, b), text, size);

    private static Bitmap CaptureClient(Form form)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            _ = SetWindowPos(form.Handle, new nint(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
            form.BringToFront();
            form.Activate();
            _ = SetForegroundWindow(form.Handle);
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(60);

            var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            using var graphics = Graphics.FromImage(bitmap);
            var origin = form.PointToScreen(Point.Empty);
            graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, form.ClientSize);
            if (HasCaptureMarker(bitmap)) return bitmap;
            bitmap.Dispose();
        }
        throw new InvalidOperationException(
            "The BEngine documentation window could not remain visible long enough to capture.");
    }

    private static bool HasCaptureMarker(Bitmap image)
    {
        var marker = image.GetPixel(1085, 53);
        return Math.Abs(marker.R - 44) <= 3 && Math.Abs(marker.G - 194) <= 3 &&
               Math.Abs(marker.B - 174) <= 3;
    }

    private static void ValidateDocumentation(string root)
    {
        foreach (var shot in Shots)
        {
            var resources = Path.Combine(root, "src", shot.SourceDirectory, "Editor");
            var htmlPath = Path.Combine(resources, "Doc", "index.html");
            var readmePath = Path.Combine(resources, "Readme.md");
            var imagePath = Path.Combine(resources, "Doc", "images", "overview.png");
            var skillPath = Directory.EnumerateFiles(Path.Combine(resources, "Skills"), "SKILL.md",
                SearchOption.AllDirectories).Single();
            var html = File.ReadAllText(htmlPath);
            var readme = File.ReadAllText(readmePath);
            var skill = File.ReadAllText(skillPath);

            foreach (var section in new[] { "快速开始", "组件", "API", "扩展" })
                Require(html.Contains(section, StringComparison.OrdinalIgnoreCase),
                    $"{shot.Name} documentation is missing section '{section}'.");
            Require(html.Contains("images/overview.png", StringComparison.OrdinalIgnoreCase) &&
                    html.Contains("<figcaption", StringComparison.OrdinalIgnoreCase),
                $"{shot.Name} documentation is missing its explained overview image.");
            Require(readme.Length >= 1600 && readme.Contains("快速开始", StringComparison.Ordinal),
                $"{shot.Name} Readme is not a detailed quick-start guide.");
            foreach (var operation in new[]
                     {
                         (Name: "Package Manager", Terms: new[] { "Package Manager" }),
                         (Name: "Project", Terms: new[] { "Project" }),
                         (Name: "Hierarchy", Terms: new[] { "Hierarchy" }),
                         (Name: "Scene", Terms: new[] { "Scene" }),
                         (Name: "Game", Terms: new[] { "Game" }),
                         (Name: "Inspector", Terms: new[] { "Inspector" }),
                         (Name: "Console", Terms: new[] { "Console" }),
                         (Name: "Play", Terms: new[] { "Play" }),
                         (Name: "Stop", Terms: new[] { "Stop" }),
                         (Name: "Extend", Terms: new[] { "Extend", "Extension", "扩展" })
                     })
                Require(operation.Terms.Any(term => skill.Contains(term, StringComparison.OrdinalIgnoreCase)),
                    $"{shot.Name} Skill is missing editor operation '{operation.Name}'.");
            Require(skill.Length >= 4500, $"{shot.Name} Skill is too small to cover editor operations.");
            Require(File.Exists(imagePath), $"{shot.Name} overview image is missing.");
            using var image = new Bitmap(imagePath);
            Require(image.Width == Width && image.Height == Height,
                $"{shot.Name} overview image must be {Width}x{Height}.");
            Require(HasCaptureMarker(image),
                $"{shot.Name} overview image was not captured from the BEngine render window.");
            var sampledColors = new HashSet<int>();
            for (var y = 0; y < image.Height; y += 32)
            for (var x = 0; x < image.Width; x += 32)
                sampledColors.Add(image.GetPixel(x, y).ToArgb());
            Require(sampledColors.Count >= 12,
                $"{shot.Name} overview image appears blank or does not show an authored scene.");

            var releasedResources = shot.SourceDirectory.Equals("Core", StringComparison.Ordinal)
                ? Path.Combine(root, "Output", "BEgine", "Editor")
                : Path.Combine(root, "Output", "Packages", shot.Name, "Editor");
            var relativeSkill = Path.GetRelativePath(resources, skillPath);
            foreach (var relativePath in new[]
                     {
                         "Doc/index.html", "Doc/images/overview.png", "Readme.md", relativeSkill
                     })
            {
                var source = Path.Combine(resources,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                var released = Path.Combine(releasedResources,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                Require(File.Exists(released),
                    $"{shot.Name} release output is missing {relativePath}.");
                Require(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(released)),
                    $"{shot.Name} release output differs from source {relativePath}.");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the BEngine repository root.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    private sealed record PackageShot(
        string Name,
        string SourceDirectory,
        string[] Hierarchy,
        string[] Inspector,
        Func<Scene> BuildScene);

    private sealed class DocumentationAttributeShowcase : MonoBehaviour
    {
        [Title("Inspector Showcase")]
        [Required]
        public string displayName = "BEngine Hero";

        [Range(0, 100)]
        [ProgressBar(0, 100, "Completion")]
        public Fix64 completion = 68;

        [ShowIf(nameof(showAdvanced))]
        public bool showAdvanced = true;
    }
}
