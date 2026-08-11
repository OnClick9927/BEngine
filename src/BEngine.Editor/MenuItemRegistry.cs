using System.Reflection;

namespace BEngine.Editor;

internal sealed class MenuItemRegistry
{
    private readonly MenuCommand[] _commands;

    public IReadOnlyList<string> Roots { get; }

    private MenuItemRegistry(MenuCommand[] commands)
    {
        _commands = commands;
        Roots = commands.Select(command => command.Segments[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(root => root, StringComparer.Ordinal)
            .ToArray();
    }

    public static MenuItemRegistry Discover()
    {
        var attributedMethods = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(method => method.GetCustomAttributes<MenuItemAttribute>()
                .Select(attribute => new AttributedMethod(method, attribute)))
            .ToArray();

        var validators = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        foreach (var item in attributedMethods.Where(item => item.Attribute.isValidateFunction))
        {
            if (!IsValidValidator(item.Method))
            {
                Debug.LogWarning($"MenuItem validator must be static bool with zero parameters or MenuCommand: " +
                                 Describe(item.Method));
                continue;
            }
            validators.TryAdd(item.Attribute.itemName, item.Method);
        }

        var commands = new List<MenuCommand>();
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
            commands.Add(new MenuCommand(item.Attribute.itemName, segments, item.Attribute.priority,
                item.Method, validator));
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
        if (command is null || !Validate(command)) return false;
        return Invoke(command);
    }

    public IReadOnlyList<MenuNode> GetRoot(string root) => BuildLevel(
        _commands.Where(command => command.Segments[0] == root).ToArray(), 1, null);

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
            if (Validate(command, context))
                menu.AddItem(new GUIContent(label), false, () => Invoke(command, context));
            else
                menu.AddDisabledItem(new GUIContent(label));
        }
    }

    private static IReadOnlyList<MenuNode> BuildLevel(
        IReadOnlyList<MenuCommand> commands,
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
                command is not null && Validate(command, context),
                command is null ? null : () => Invoke(command, context),
                childNodes));
        }
        return nodes;
    }

    private static bool Invoke(MenuCommand command, BObject? context = null)
    {
        try
        {
            command.Execute.Invoke(null, BuildArguments(command.Execute, context));
            return true;
        }
        catch (Exception exception)
        {
            var cause = exception is TargetInvocationException { InnerException: not null }
                ? exception.InnerException
                : exception;
            Debug.LogError($"MenuItem '{command.Path}' failed: {cause.Message}");
            return false;
        }
    }

    private static bool Validate(MenuCommand command, BObject? context = null)
    {
        if (command.Validate is null) return true;
        try
        {
            return command.Validate.Invoke(null, BuildArguments(command.Validate, context)) is true;
        }
        catch (Exception exception)
        {
            var cause = exception is TargetInvocationException { InnerException: not null }
                ? exception.InnerException
                : exception;
            Debug.LogError($"MenuItem validator '{command.Path}' failed: {cause.Message}");
            return false;
        }
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

    private static object?[]? BuildArguments(MethodInfo method, BObject? context) => method.GetParameters().Length == 0
        ? null
        : [new global::BEngine.Editor.MenuCommand(context ?? Selection.activeObject)];

    private static IEnumerable<Type> GetTypeHierarchy(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType) yield return current;
    }

    private static string Describe(MethodInfo method) => $"{method.DeclaringType?.FullName}.{method.Name}";

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private sealed record AttributedMethod(MethodInfo Method, MenuItemAttribute Attribute);
    private sealed record MenuCommand(
        string Path,
        string[] Segments,
        int Priority,
        MethodInfo Execute,
        MethodInfo? Validate);
    private sealed record MenuEntry(string Name, int Priority, MenuCommand[] Commands);

    internal sealed record MenuNode(
        string Name,
        int Priority,
        bool Enabled,
        Action? Execute,
        IReadOnlyList<MenuNode> Children);
}
