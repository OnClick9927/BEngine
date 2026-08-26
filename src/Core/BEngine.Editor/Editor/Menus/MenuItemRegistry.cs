using System.Reflection;

namespace BEngine.Editor;

internal sealed class MenuItemRegistry
{
    private readonly CachedMenuCommand[] _commands;

    public IReadOnlyList<string> Roots { get; }

    private MenuItemRegistry(CachedMenuCommand[] commands)
    {
        _commands = commands;
        Roots = commands.Select(command => command.Segments[0])
            .Where(root => !root.Equals("CONTEXT", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(root => root, StringComparer.Ordinal)
            .ToArray();
    }

    public static MenuItemRegistry Empty() => new([]);

    public static MenuItemRegistry Discover()
    {
        var attributedMethods = TypeCache.GetMethodsWithAttribute<MenuItemAttribute>()
            .SelectMany(AttributesFor)
            .ToArray();

        var validators = new Dictionary<string, Func<BObject?, bool>>(StringComparer.Ordinal);
        foreach (var item in attributedMethods.Where(item => item.Attribute.isValidateFunction))
        {
            if (!IsValidValidator(item.Method))
            {
                Debug.LogWarning($"MenuItem validator must be static bool with zero parameters or MenuCommand: " +
                                 Describe(item.Method));
                continue;
            }
            validators.TryAdd(item.Attribute.itemName, CreateValidator(item.Method));
        }

        var commands = new List<CachedMenuCommand>();
        foreach (var item in attributedMethods.Where(item => !item.Attribute.isValidateFunction))
        {
            if (!IsValidCommand(item.Method))
            {
                Debug.LogWarning($"MenuItem command must be static void with zero parameters or MenuCommand: " +
                                 Describe(item.Method));
                continue;
            }

            var segments = item.Attribute.itemName.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 2)
            {
                Debug.LogWarning($"MenuItem path must contain a root and command: {item.Attribute.itemName}");
                continue;
            }
            if (commands.Any(command => command.Path == item.Attribute.itemName))
            {
                Debug.LogWarning($"Duplicate MenuItem ignored: {item.Attribute.itemName}");
                continue;
            }

            validators.TryGetValue(item.Attribute.itemName, out var validator);
            commands.Add(new CachedMenuCommand(item.Attribute.itemName, segments, item.Attribute.priority,
                CreateCommand(item.Method), validator));
        }

        foreach (var entry in CreateAssetMenuRegistry.entries)
        {
            var path = $"Assets/Create/{entry.MenuName}";
            if (commands.Any(command => command.Path.Equals(path, StringComparison.Ordinal)))
            {
                Debug.LogWarning($"CreateAssetMenu entry ignored because a MenuItem already owns {path}.");
                continue;
            }
            var captured = entry;
            commands.Add(new CachedMenuCommand(path,
                path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                entry.Order, _ => ProjectAssetCreation.CreateAttributedFromMenu(captured),
                _ => ProjectAssetCreation.CanCreateFromMenu()));
        }

        return new MenuItemRegistry(commands
            .OrderBy(command => command.Priority)
            .ThenBy(command => command.Path, StringComparer.Ordinal)
            .ToArray());
    }

    public bool HasRoot(string root) => _commands.Any(command => command.Segments[0] == root);

    public bool Execute(string itemName)
    {
        var normalized = itemName.Replace('\\', '/').Trim('/');
        var command = _commands.FirstOrDefault(item => item.Path == normalized);
        if (command is null || !Menu.GetEnabled(command.Path) || !Validate(command)) return false;
        return Invoke(command);
    }

    public IReadOnlyList<MenuNode> GetRoot(string root) => GetRoot(root, null);

    public IReadOnlyList<MenuNode> GetRoot(string root, BObject? context) => BuildLevel(
        _commands.Where(command => command.Segments[0] == root).ToArray(), 1, context);

    public void PopulateRoot(GenericMenu menu, string root, BObject? context = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        PopulateNodes(menu, GetRoot(root, context), string.Empty);
    }

    public void PopulatePath(GenericMenu menu, string path, BObject? context = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var segments = path.Replace('\\', '/').Split('/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var commands = _commands.Where(command => command.Segments.Length > segments.Length &&
                                                   command.Segments.AsSpan(0, segments.Length)
                                                       .SequenceEqual(segments)).ToArray();
        if (commands.Length == 0) return;
        PopulateNodes(menu, BuildLevel(commands, segments.Length, context), string.Empty);
    }

    public void PopulateContext(GenericMenu menu, BObject context)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(context);
        var typeNames = GetTypeHierarchy(context.GetType()).Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
        var commands = _commands.Where(command => command.Segments.Length >= 3 &&
                                                   command.Segments[0] == "CONTEXT" &&
                                                   typeNames.Contains(command.Segments[1]))
            .OrderBy(command => command.Priority)
            .ThenBy(command => command.Path, StringComparer.Ordinal)
            .ToArray();
        if (commands.Length == 0) return;
        if (menu.GetItemCount() > 0) menu.AddSeparator(string.Empty);
        int? previousPriority = null;
        foreach (var command in commands)
        {
            if (previousPriority is { } previous && command.Priority - previous >= 11)
                menu.AddSeparator(string.Empty);
            previousPriority = command.Priority;
            var label = string.Join('/', command.Segments.Skip(2));
            if (Menu.GetEnabled(command.Path) && Validate(command, context))
                menu.AddItem(new GUIContent(label), Menu.GetChecked(command.Path), () => Invoke(command, context));
            else
                menu.AddDisabledItem(new GUIContent(label), Menu.GetChecked(command.Path));
        }
    }

    private static IReadOnlyList<MenuNode> BuildLevel(
        IReadOnlyList<CachedMenuCommand> commands,
        int depth,
        BObject? context)
    {
        var entries = commands
            .GroupBy(command => command.Segments[depth], StringComparer.Ordinal)
            .Select(group => new MenuEntry(group.Key, group.Min(command => command.Priority), group.ToArray()))
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        var nodes = new List<MenuNode>();
        foreach (var entry in entries)
        {
            var command = entry.Commands.FirstOrDefault(item => item.Segments.Length == depth + 1);
            var children = entry.Commands.Where(item => item.Segments.Length > depth + 1).ToArray();
            var childNodes = children.Length > 0 ? BuildLevel(children, depth + 1, context) : [];
            nodes.Add(new MenuNode(
                entry.Name,
                entry.Priority,
                command is not null && Menu.GetEnabled(command.Path) && Validate(command, context),
                command is not null && Menu.GetChecked(command.Path),
                command is null ? null : () => Invoke(command, context),
                childNodes));
        }
        return nodes;
    }

    private static void PopulateNodes(GenericMenu menu, IReadOnlyList<MenuNode> nodes, string prefix)
    {
        foreach (var node in nodes)
        {
            var path = string.IsNullOrWhiteSpace(prefix) ? node.Name : $"{prefix}/{node.Name}";
            if (node.Children.Count > 0)
            {
                PopulateNodes(menu, node.Children, path);
                continue;
            }
            if (node.Enabled && node.Execute is not null)
                menu.AddItem(new GUIContent(path), node.Checked, node.Execute.Invoke);
            else
                menu.AddDisabledItem(new GUIContent(path), node.Checked);
        }
    }

    private static bool Invoke(CachedMenuCommand command, BObject? context = null)
    {
        return EditorFeatureGuard.Invoke($"MenuItem {command.Path}", () => command.Execute(context));
    }

    private static bool Validate(CachedMenuCommand command, BObject? context = null)
    {
        if (command.Validate is null) return true;
        EditorFeatureGuard.TryInvoke($"MenuItem validator {command.Path}",
            () => command.Validate(context), false, out var valid);
        return valid;
    }

    private static bool IsValidCommand(MethodInfo method) =>
        method.IsStatic && method.ReturnType == typeof(void) && HasValidParameters(method);

    private static bool IsValidValidator(MethodInfo method) =>
        method.IsStatic && method.ReturnType == typeof(bool) && HasValidParameters(method);

    private static bool HasValidParameters(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return parameters.Length == 0 || parameters is [{ ParameterType: var type }] && type == typeof(MenuCommand);
    }

    private static Action<BObject?> CreateCommand(MethodInfo method)
    {
        if (method.GetParameters().Length == 0)
        {
            var callback = method.CreateDelegate<Action>();
            return _ => callback();
        }
        var callbackWithContext = method.CreateDelegate<Action<global::BEngine.Editor.MenuCommand>>();
        return context => callbackWithContext(new global::BEngine.Editor.MenuCommand(
            context ?? Selection.activeObject));
    }

    private static Func<BObject?, bool> CreateValidator(MethodInfo method)
    {
        if (method.GetParameters().Length == 0)
        {
            var callback = method.CreateDelegate<Func<bool>>();
            return _ => callback();
        }
        var callbackWithContext = method.CreateDelegate<Func<global::BEngine.Editor.MenuCommand, bool>>();
        return context => callbackWithContext(new global::BEngine.Editor.MenuCommand(
            context ?? Selection.activeObject));
    }

    private static IEnumerable<Type> GetTypeHierarchy(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType) yield return current;
    }

    private static string Describe(MethodInfo method) => $"{method.DeclaringType?.FullName}.{method.Name}";

    private static AttributedMethod[] AttributesFor(MethodInfo method)
    {
        var feature = $"MenuItem attributes {Describe(method)}";
        return EditorFeatureGuard.TryInvoke(feature,
            () => method.GetCustomAttributes<MenuItemAttribute>()
                .Select(attribute => new AttributedMethod(method, attribute)).ToArray(), [], out var values)
            ? values : [];
    }

    private readonly record struct AttributedMethod(MethodInfo Method, MenuItemAttribute Attribute);
    private sealed record CachedMenuCommand(
        string Path,
        string[] Segments,
        int Priority,
        Action<BObject?> Execute,
        Func<BObject?, bool>? Validate);
    private readonly record struct MenuEntry(string Name, int Priority, CachedMenuCommand[] Commands);

    internal sealed record MenuNode(
        string Name,
        int Priority,
        bool Enabled,
        bool Checked,
        Action? Execute,
        IReadOnlyList<MenuNode> Children);
}
