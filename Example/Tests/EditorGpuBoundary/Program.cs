using System.Reflection;
using System.Reflection.Emit;
using BEngine.Editor;
using BEngine.Rendering.Rhi;

namespace BEngine.ExampleTests.EditorGpuBoundary;

internal static class Program
{
    private static readonly OpCode[] OneByteOpCodes = BuildOpCodeTable(twoByte: false);
    private static readonly OpCode[] TwoByteOpCodes = BuildOpCodeTable(twoByte: true);

    private static int Main()
    {
        try
        {
            var assembly = typeof(EditorWindow).Assembly;
            var references = assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();
            Require(!references.Contains("BEngine.UIElements", StringComparer.OrdinalIgnoreCase),
                "BEngine.Editor directly references the optional UIElements package.");

            var editorAssembly = Assembly.Load("BEngine.Editor");
            var nativeWindow = editorAssembly.GetType("BEngine.Editor.ImGuiNativeWindow", true)!;
            var dock = editorAssembly.GetType("BEngine.Editor.ImGuiDockWorkspace", true)!;
            Require(nativeWindow.GetProperty("backend")?.PropertyType == typeof(GraphicsBackend),
                "Native editor window does not expose its RHI graphics backend.");
            Require(nativeWindow.GetEvent("gui", BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic) is not null, "Native GPU window is missing its OnGUI event path.");
            Require(dock.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic) is not null, "Dock workspace is not drawn through IMGUI.");
            Require(typeof(Event).Assembly.GetType("BEngine.UIElements.VisualElement", false) is null,
                "BEngine runtime still embeds the optional UIElements package.");

            VerifyEditorWindowTypeBoundary(editorAssembly, nativeWindow);
            VerifyNativeEditorHosts(editorAssembly, nativeWindow);
            VerifyNativeWindowConstructionBoundary(editorAssembly, nativeWindow);
            VerifyNativeWindowGraphicsApi(nativeWindow);
            VerifyEditorWindowHostRouting(editorAssembly);

            Console.WriteLine(
                "EDITOR_GPU_BOUNDARY_OK|platform-dialog-isolation,no-uielements,gpu-imgui,rhi-window,gpu-dock," +
                "docked-editor-windows,native-float,native-float-popups,single-thread-hosting," +
                "vulkan-no-api-window");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_GPU_BOUNDARY_FAILED|{exception}");
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyEditorWindowTypeBoundary(Assembly editorAssembly, Type nativeWindow)
    {
        var editorWindow = typeof(EditorWindow);
        var nativeFloatingHost = editorAssembly.GetType("BEngine.Editor.NativeFloatingEditorWindow", true)!;
        var ownedTypes = editorAssembly.GetTypes().Where(type =>
            type != nativeFloatingHost && (editorWindow.IsAssignableFrom(type) ||
            type.Name.Contains("EditorWindow", StringComparison.Ordinal)) ||
            type.Name.Contains("DockWorkspace", StringComparison.Ordinal) ||
            type.Name.Contains("DockPanel", StringComparison.Ordinal));

        foreach (var type in ownedTypes)
        {
            AssertAllowedType(type, type.BaseType, "base type", nativeWindow);
            foreach (var implemented in type.GetInterfaces())
                AssertAllowedType(type, implemented, "interface", nativeWindow);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static |
                                                  BindingFlags.Public | BindingFlags.NonPublic |
                                                  BindingFlags.DeclaredOnly))
                AssertAllowedType(type, field.FieldType, $"field {field.Name}", nativeWindow);
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Static |
                                                        BindingFlags.Public | BindingFlags.NonPublic |
                                                        BindingFlags.DeclaredOnly))
            {
                AssertAllowedType(type, property.PropertyType, $"property {property.Name}", nativeWindow);
                foreach (var parameter in property.GetIndexParameters())
                    AssertAllowedType(type, parameter.ParameterType,
                        $"property {property.Name} parameter {parameter.Name}", nativeWindow);
            }
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                   BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.DeclaredOnly))
            {
                AssertAllowedType(type, method.ReturnType, $"method {method.Name} return", nativeWindow);
                foreach (var parameter in method.GetParameters())
                    AssertAllowedType(type, parameter.ParameterType,
                        $"method {method.Name} parameter {parameter.Name}", nativeWindow);
            }
            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Static |
                                                              BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var parameter in constructor.GetParameters())
                AssertAllowedType(type, parameter.ParameterType,
                    $"constructor parameter {parameter.Name}", nativeWindow);
        }
    }

    private static void AssertAllowedType(Type owner, Type? dependency, string member, Type nativeWindow)
    {
        if (dependency is null) return;
        while (dependency.HasElementType) dependency = dependency.GetElementType()!;
        if (dependency.IsGenericType)
        {
            if (!dependency.IsGenericTypeDefinition)
                AssertAllowedType(owner, dependency.GetGenericTypeDefinition(), member, nativeWindow);
            foreach (var argument in dependency.GetGenericArguments())
                AssertAllowedType(owner, argument, member, nativeWindow);
            return;
        }

        var assemblyName = dependency.Assembly.GetName().Name ?? string.Empty;
        var forbidden = dependency == nativeWindow ||
                        assemblyName.Equals("System.Windows.Forms", StringComparison.OrdinalIgnoreCase) ||
                        assemblyName.Equals("Microsoft.WindowsDesktop.App.WindowsForms",
                            StringComparison.OrdinalIgnoreCase) ||
                        dependency.Namespace?.StartsWith("System.Windows.Forms",
                            StringComparison.Ordinal) == true ||
                        assemblyName.StartsWith("Silk.NET.Windowing", StringComparison.OrdinalIgnoreCase) ||
                        assemblyName.Equals("Silk.NET.GLFW", StringComparison.OrdinalIgnoreCase) ||
                        dependency.Namespace?.StartsWith("Silk.NET.Windowing",
                            StringComparison.Ordinal) == true;
        Require(!forbidden,
            $"{owner.FullName} {member} depends on native/system window type {dependency.FullName}.");
    }

    private static void VerifyNativeWindowConstructionBoundary(Assembly editorAssembly, Type nativeWindow)
    {
        var allowedOwners = new HashSet<string>(StringComparer.Ordinal)
        {
            "BEngine.Editor.GpuEditorApplication",
            "BEngine.Editor.GpuStartupProgressWindow",
            "BEngine.Editor.NativeFloatingEditorWindow"
        };
        foreach (var type in editorAssembly.GetTypes())
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                                BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.DeclaredOnly).Cast<MethodBase>()
                     .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static |
                                                  BindingFlags.Public | BindingFlags.NonPublic)))
        {
            if (!ConstructsType(method, nativeWindow)) continue;
            Require(method.IsConstructor && allowedOwners.Contains(type.FullName ?? string.Empty),
                $"{type.FullName}.{method.Name} creates a native editor window outside an approved host.");
        }
    }

    private static void VerifyNativeEditorHosts(Assembly editorAssembly, Type nativeWindow)
    {
        var editorWindow = typeof(EditorWindow);
        var application = editorAssembly.GetType("BEngine.Editor.GpuEditorApplication", true)!;
        var floatingHost = editorAssembly.GetType("BEngine.Editor.NativeFloatingEditorWindow", true)!;
        var nativeFields = application.GetFields(BindingFlags.Instance | BindingFlags.Static |
                                                  BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => ContainsType(field.FieldType, nativeWindow))
            .ToArray();
        Require(nativeFields.Length == 1 && nativeFields[0].Name == "_mainWindow" &&
                nativeFields[0].FieldType == nativeWindow,
            "GpuEditorApplication must directly own only its main native graphics host; " +
            $"found [{string.Join(", ", nativeFields.Select(field => field.Name))}].");
        var floatingNativeFields = floatingHost.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                           BindingFlags.NonPublic)
            .Where(field => field.FieldType == nativeWindow)
            .ToArray();
        Require(floatingNativeFields.Length == 1,
            "Each native floating EditorWindow host must own exactly one ImGuiNativeWindow.");
        Require(floatingHost.GetMethod("Pump", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "Native floating EditorWindows are not pumped explicitly by the editor owner thread.");
        var layer = editorAssembly.GetType("BEngine.Editor.EditorWindowLayer", true)!;
        Require(floatingHost.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Count(field => field.FieldType == layer) == 1,
            "A native floating host must own exactly one transient EditorWindow layer.");
        Require(floatingHost.GetMethod("ShowTransient", BindingFlags.Instance | BindingFlags.NonPublic) is not null &&
                floatingHost.GetMethod("RemoveTransient", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "Native floating hosts cannot own and dismiss local popup EditorWindows.");
        var transientOwnerField = application.GetField("_nativeTransientOwners",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Require(transientOwnerField is not null &&
                ContainsType(transientOwnerField.FieldType, editorWindow) &&
                ContainsType(transientOwnerField.FieldType, floatingHost),
            "GpuEditorApplication does not retain popup-to-native-host ownership.");
    }

    private static void VerifyNativeWindowGraphicsApi(Type nativeWindow)
    {
        var method = nativeWindow.GetMethod("WindowApiForBackend", BindingFlags.Static | BindingFlags.NonPublic) ??
                     throw new InvalidOperationException("Native editor window does not expose its backend API mapping.");
        var apiProperty = method.ReturnType.GetProperty("API") ??
                          throw new InvalidOperationException("Silk window graphics API has no API discriminator.");
        var vulkan = method.Invoke(null, [GraphicsBackend.Vulkan])!;
        var openGl = method.Invoke(null, [GraphicsBackend.OpenGL])!;
        Require(string.Equals(apiProperty.GetValue(vulkan)?.ToString(), "None", StringComparison.Ordinal),
            "Vulkan editor window creates a competing graphics context instead of an external no-API window.");
        Require(string.Equals(apiProperty.GetValue(openGl)?.ToString(), "OpenGL", StringComparison.Ordinal),
            "OpenGL editor window no longer requests an OpenGL context.");
    }

    private static void VerifyEditorWindowHostRouting(Assembly editorAssembly)
    {
        var application = editorAssembly.GetType("BEngine.Editor.GpuEditorApplication", true)!;
        var layer = editorAssembly.GetType("BEngine.Editor.EditorWindowLayer", true)!;
        var dock = editorAssembly.GetType("BEngine.Editor.ImGuiDockWorkspace", true)!;
        var layerFields = application.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                 BindingFlags.NonPublic)
            .Where(field => field.FieldType == layer)
            .ToArray();
        Require(layerFields.Length == 1,
            "GpuEditorApplication must own exactly one in-process EditorWindowLayer.");
        foreach (var methodName in new[] { "ShowEditorWindow", "CloseEditorWindow" })
        {
            var method = application.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic) ??
                         throw new InvalidOperationException(
                             $"GpuEditorApplication is missing its {methodName} host route.");
            Require(ReferencesType(method, layer),
                $"GpuEditorApplication.{methodName} does not route EditorWindow state through EditorWindowLayer.");
        }
        var show = application.GetMethod("ShowEditorWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Require(ReferencesType(show, dock),
            "GpuEditorApplication.ShowEditorWindow no longer routes Normal windows into the engine dock workspace.");
        var nativeFloating = editorAssembly.GetType("BEngine.Editor.NativeFloatingEditorWindow", true)!;
        var showFloating = application.GetMethod("ShowNativeFloating",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
                           throw new InvalidOperationException(
                               "GpuEditorApplication is missing native floating EditorWindow hosting.");
        Require(ReferencesType(showFloating, nativeFloating),
            "GpuEditorApplication does not route floating Normal/Aux windows through the native host.");
    }

    private static bool ContainsType(Type candidate, Type target)
    {
        while (candidate.HasElementType) candidate = candidate.GetElementType()!;
        if (candidate == target) return true;
        return candidate.IsGenericType && candidate.GetGenericArguments().Any(argument =>
            ContainsType(argument, target));
    }

    private static bool ReferencesType(MethodBase method, Type targetType)
    {
        var body = method.GetMethodBody()?.GetILAsByteArray();
        if (body is null) return false;
        for (var offset = 0; offset < body.Length;)
        {
            var opCode = ReadOpCode(body, ref offset);
            var operandOffset = offset;
            var operandSize = GetOperandSize(opCode.OperandType, body, operandOffset);
            if (operandSize == 4 && opCode.OperandType is OperandType.InlineField or
                    OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok)
            {
                try
                {
                    var token = BitConverter.ToInt32(body, operandOffset);
                    var member = method.Module.ResolveMember(token,
                        method.DeclaringType?.GetGenericArguments(),
                        method is MethodInfo info ? info.GetGenericArguments() : null);
                    if (member is Type type && ContainsType(type, targetType) ||
                        member is FieldInfo field && (field.DeclaringType == targetType ||
                                                      ContainsType(field.FieldType, targetType)) ||
                        member is MethodBase called && called.DeclaringType == targetType)
                        return true;
                }
                catch (ArgumentException)
                {
                    // Ignore metadata tokens for optional generic members that cannot be resolved here.
                }
            }
            offset += operandSize;
        }
        return false;
    }

    private static bool ConstructsType(MethodBase method, Type targetType)
    {
        var body = method.GetMethodBody()?.GetILAsByteArray();
        if (body is null) return false;
        for (var offset = 0; offset < body.Length;)
        {
            var opCode = ReadOpCode(body, ref offset);
            var operandOffset = offset;
            var operandSize = GetOperandSize(opCode.OperandType, body, operandOffset);
            if (opCode == OpCodes.Newobj && operandSize == 4)
            {
                try
                {
                    var token = BitConverter.ToInt32(body, operandOffset);
                    var declaringType = method.Module.ResolveMethod(token,
                        method.DeclaringType?.GetGenericArguments(),
                        method is MethodInfo info ? info.GetGenericArguments() : null)?.DeclaringType;
                    if (declaringType == targetType) return true;
                }
                catch (ArgumentException)
                {
                    // An unresolved optional generic token cannot be the concrete native-window constructor.
                }
            }
            offset += operandSize;
        }
        return false;
    }

    private static OpCode ReadOpCode(byte[] body, ref int offset)
    {
        var first = body[offset++];
        if (first != 0xFE) return OneByteOpCodes[first];
        return TwoByteOpCodes[body[offset++]];
    }

    private static int GetOperandSize(OperandType operandType, byte[] body, int offset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + BitConverter.ToInt32(body, offset) * 4,
        _ => 4
    };

    private static OpCode[] BuildOpCodeTable(bool twoByte)
    {
        var table = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode) continue;
            var value = unchecked((ushort)opCode.Value);
            if (twoByte && (value & 0xFF00) == 0xFE00) table[value & 0xFF] = opCode;
            else if (!twoByte && value < 0x100) table[value] = opCode;
        }
        return table;
    }
}
