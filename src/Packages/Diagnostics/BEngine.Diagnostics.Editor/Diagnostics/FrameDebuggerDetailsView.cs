using BEngine.Editor.Diagnostics;
using BEngine.Rendering.Rhi;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Editor;

/// <summary>Typed, read-only inspector for one captured render event.</summary>
internal sealed class FrameDebuggerDetailsView
{
    private const int RowHeight = 22;
    private Vector2 _scroll;
    private bool _captureExpanded = true;
    private bool _detailsExpanded = true;
    private bool _batchExpanded = true;
    private bool _meshExpanded = true;
    private bool _programExpanded = true;
    private bool _targetExpanded = true;
    private bool _renderStateExpanded = true;
    private bool _texturesExpanded;
    private bool _intsExpanded;
    private bool _floatsExpanded = true;
    private bool _vectorsExpanded;
    private bool _matricesExpanded;
    private bool _errorExpanded = true;

    internal void ResetScroll() => _scroll = Vector2.zero;

    internal void Draw(Rect area, FrameDebugCaptureSnapshot snapshot, FrameDebugEvent item,
        bool executedAtSelectedStep, FrameDebugPreviewSnapshot? preview)
    {
        GUI.Box(area, GUIContent.none, EditorStyles.viewBackground);
        var viewport = new Rect(area.x + 1, area.y + 1,
            Fix64.Max(1, area.width - 2), Fix64.Max(1, area.height - 2));
        var ints = item.State.Ints ?? Array.Empty<FrameDebugIntProperty>();
        var floats = item.State.Floats ?? Array.Empty<FrameDebugFloatProperty>();
        var vectors = item.State.Vectors ?? Array.Empty<FrameDebugVectorProperty>();
        var matrices = item.State.Matrices ?? Array.Empty<FrameDebugMatrixProperty>();
        var contentHeight = 1500 +
                            item.State.Textures.Count * 7 * (RowHeight + 1) +
                            (item.Mesh?.Attributes.Count ?? 0) * 3 * (RowHeight + 1) +
                            (item.State.Program?.Stages.Count ?? 0) * 3 * (RowHeight + 1) +
                            (ints.Count + floats.Count + vectors.Count + matrices.Count * 4) *
                            (RowHeight + 1);
        _scroll = GUI.BeginScrollView(viewport, _scroll,
            new Rect(0, 0, Fix64.Max(1, viewport.width - 12), contentHeight));
        try
        {
            var content = new Rect(7, 5, Fix64.Max(1, viewport.width - 26), contentHeight - 8);
            var y = content.y;
            GUI.Label(new Rect(content.x + 2, y, content.width - 4, 28),
                $"Event #{item.Index + 1}  {item.Name}", EditorStyles.largeLabel);
            y += 31;

            _captureExpanded = Foldout(content, ref y, _captureExpanded, "Capture");
            if (_captureExpanded)
            {
                ReadOnlyText(content, ref y, "Target", snapshot.TargetName);
                ReadOnlyEnum(content, ref y, "Backend", snapshot.Backend);
                ReadOnlyText(content, ref y, "Captured At",
                    snapshot.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"));
                ReadOnlyText(content, ref y, "Capture ID", snapshot.CaptureId.ToString());
                ReadOnlyInt(content, ref y, "Event", item.Index + 1);
                ReadOnlyInt(content, ref y, "Total Events", snapshot.Events.Count);
                if (preview is not null)
                {
                    ReadOnlyEnum(content, ref y, "Output Status", preview.Status);
                    ReadOnlyInt(content, ref y, "Output Event Count", preview.EventCount);
                }
            }

            _detailsExpanded = Foldout(content, ref y, _detailsExpanded, "Details");
            if (_detailsExpanded)
            {
                ReadOnlyText(content, ref y, "Event Name", item.Name);
                ReadOnlyEnum(content, ref y, "Event Type", item.Kind);
                ReadOnlyToggle(content, ref y, "Capture Succeeded", item.Executed);
                ReadOnlyToggle(content, ref y, "Included In Output", executedAtSelectedStep);
                if (preview?.Image is { } previewImage)
                {
                    ReadOnlyInt(content, ref y, "Output Width", previewImage.Width);
                    ReadOnlyInt(content, ref y, "Output Height", previewImage.Height);
                    ReadOnlyEnum(content, ref y, "Output Format", previewImage.Format);
                }
                ReadOnlyVector(content, ref y, "Viewport", ToVector(item.State.Viewport));
                ReadOnlyToggle(content, ref y, "Scissor Enabled", item.State.Scissor is not null);
                if (item.State.Scissor is { } scissor)
                    ReadOnlyVector(content, ref y, "Scissor", ToVector(scissor));
                if (item.ClearFlags != GraphicsClearFlags.None)
                {
                    ReadOnlyEnum(content, ref y, "Clear Flags", item.ClearFlags);
                    ReadOnlyColor(content, ref y, "Clear Color", ToColor(item.ClearColor));
                }
            }

            _batchExpanded = Foldout(content, ref y, _batchExpanded, "Draw Call / Batch");
            if (_batchExpanded)
            {
                ObjectOrText(content, ref y, "Source", ResolveSource(item), item.Marker.SourceName,
                    allowSceneObjects: true);
                if (item.Marker.SourceInstanceId is { } instanceId)
                    ReadOnlyInt(content, ref y, "Source Instance ID", instanceId);
                ObjectOrText(content, ref y, "Material", ResolveGuid(item.Marker.Material),
                    item.Marker.Material?.ToString("N"), allowSceneObjects: false);
                if (item.Marker.Material is { } materialGuid)
                    ReadOnlyText(content, ref y, "Material GUID", materialGuid.ToString("N"));
                ObjectOrText(content, ref y, "Shader", ResolveAsset(item.Marker.Shader),
                    item.Marker.Shader, allowSceneObjects: false);
                ObjectOrText(content, ref y, "Atlas", ResolveAsset(item.Marker.Atlas),
                    item.Marker.Atlas, allowSceneObjects: false);
                ReadOnlyText(content, ref y, "Group", item.Marker.Group);
                ReadOnlyText(content, ref y, "Batch", item.Marker.BatchName);
                ReadOnlyText(content, ref y, "Mesh", item.MeshLabel);
                ReadOnlyEnum(content, ref y, "Topology", item.Topology);
                ReadOnlyInt(content, ref y, "First Vertex", item.FirstVertex);
                ReadOnlyInt(content, ref y, "Vertices", item.VertexCount);
                ReadOnlyInt(content, ref y, "Triangles", item.TriangleCount);
                ReadOnlyInt(content, ref y, "Lines", item.LineCount);
                ReadOnlyInt(content, ref y, "Draw Calls",
                    item.Kind == FrameDebugEventKind.Draw ? 1 : 0);
                ReadOnlyInt(content, ref y, "Instances",
                    item.Kind == FrameDebugEventKind.Draw ? 1 : 0);
                ReadOnlyToggle(content, ref y, "Indexed", false);
            }

            _meshExpanded = Foldout(content, ref y, _meshExpanded,
                item.Mesh is null ? "Mesh (0)" : "Mesh", item.Mesh is not null);
            if (_meshExpanded && item.Mesh is { } mesh)
            {
                ReadOnlyText(content, ref y, "Name", mesh.Label);
                ReadOnlyInt(content, ref y, "Available Vertices", mesh.AvailableVertexCount);
                ReadOnlyEnum(content, ref y, "Buffer Usage", mesh.Usage);
                ReadOnlyInt(content, ref y, "Vertex Stride", mesh.StrideBytes);
                ReadOnlyInt(content, ref y, "Attributes", mesh.Attributes.Count);
                for (var index = 0; index < mesh.Attributes.Count; index++)
                {
                    var attribute = mesh.Attributes[index];
                    ReadOnlyInt(content, ref y, $"Attribute {index} Location", attribute.Location);
                    ReadOnlyInt(content, ref y, $"Attribute {index} Components",
                        attribute.ComponentCount);
                    ReadOnlyInt(content, ref y, $"Attribute {index} Offset", attribute.OffsetBytes);
                }
            }

            _programExpanded = Foldout(content, ref y, _programExpanded,
                item.State.Program is null ? "Shader Program (0)" : "Shader Program",
                item.State.Program is not null);
            if (_programExpanded && item.State.Program is { } program)
            {
                ReadOnlyText(content, ref y, "Program", program.Label);
                ReadOnlyInt(content, ref y, "Stages", program.Stages.Count);
                for (var index = 0; index < program.Stages.Count; index++)
                {
                    var stage = program.Stages[index];
                    ReadOnlyEnum(content, ref y, $"Stage {index}", stage.Stage);
                    ReadOnlyEnum(content, ref y, $"Stage {index} Language", stage.Language);
                    ReadOnlyText(content, ref y, $"Stage {index} Entry Point", stage.EntryPoint);
                }
            }

            _targetExpanded = Foldout(content, ref y, _targetExpanded, "Render Target");
            if (_targetExpanded)
            {
                var target = item.State.RenderTarget;
                ReadOnlyText(content, ref y, "Name", target?.Label ?? item.State.RenderTargetLabel);
                if (target is { } targetState)
                {
                    ReadOnlyInt(content, ref y, "Width", targetState.Width);
                    ReadOnlyInt(content, ref y, "Height", targetState.Height);
                    ReadOnlyOptionalEnum(content, ref y, "Color Format", targetState.ColorFormat);
                    ReadOnlyOptionalEnum(content, ref y, "Depth Format", targetState.DepthFormat);
                    ReadOnlyToggle(content, ref y, "Color Sampled", targetState.ColorSampled);
                    ReadOnlyToggle(content, ref y, "Depth Sampled", targetState.DepthSampled);
                }
            }

            _renderStateExpanded = Foldout(content, ref y, _renderStateExpanded, "Render State");
            if (_renderStateExpanded)
            {
                ReadOnlyText(content, ref y, "Program", item.State.ProgramLabel);
                ReadOnlyToggle(content, ref y, "Depth Test", item.State.DepthState.TestEnabled);
                ReadOnlyToggle(content, ref y, "Depth Write", item.State.DepthState.WriteEnabled);
                ReadOnlyEnum(content, ref y, "Blend Mode", item.State.BlendMode);
                ReadOnlyEnum(content, ref y, "Cull Mode", item.State.RasterizerState.CullMode);
                ReadOnlyToggle(content, ref y, "Depth Bias",
                    item.State.RasterizerState.DepthBiasEnabled);
                if (item.State.RasterizerState.DepthBiasEnabled)
                {
                    ReadOnlyFloat(content, ref y, "Slope Scale", item.State.RasterizerState.SlopeScale);
                    ReadOnlyFloat(content, ref y, "Constant Bias", item.State.RasterizerState.ConstantBias);
                }
            }

            var textureTitle = $"Textures ({item.State.Textures.Count})";
            _texturesExpanded = Foldout(content, ref y, _texturesExpanded, textureTitle,
                item.State.Textures.Count > 0);
            if (_texturesExpanded)
            {
                foreach (var texture in item.State.Textures)
                {
                    ObjectOrText(content, ref y, $"Slot {texture.Slot}", ResolveAsset(texture.Label),
                        texture.Label, allowSceneObjects: false);
                    ReadOnlyVector(content, ref y, "  Size",
                        new Vector4(texture.Width, texture.Height, 0, 0));
                    ReadOnlyEnum(content, ref y, "  Format", texture.Format);
                    ReadOnlyEnum(content, ref y, "  Usage", texture.Usage);
                    ReadOnlyEnum(content, ref y, "  Min Filter", texture.MinFilter);
                    ReadOnlyEnum(content, ref y, "  Mag Filter", texture.MagFilter);
                    ReadOnlyEnum(content, ref y, "  Address Mode", texture.AddressMode);
                }
            }

            _ = Foldout(content, ref y, false, "Keywords (0)", enabled: false);

            _intsExpanded = Foldout(content, ref y, _intsExpanded, $"Ints ({ints.Count})",
                ints.Count > 0);
            if (_intsExpanded)
                foreach (var property in ints)
                    ReadOnlyInt(content, ref y, property.Name, property.Value);

            _floatsExpanded = Foldout(content, ref y, _floatsExpanded,
                $"Floats ({floats.Count})", floats.Count > 0);
            if (_floatsExpanded)
                foreach (var property in floats)
                    ReadOnlyFloat(content, ref y, property.Name, property.Value);

            _vectorsExpanded = Foldout(content, ref y, _vectorsExpanded,
                $"Vectors ({vectors.Count})", vectors.Count > 0);
            if (_vectorsExpanded)
                foreach (var property in vectors)
                    ReadOnlyVector(content, ref y, property.Name, ToVector(property.Value));

            _matricesExpanded = Foldout(content, ref y, _matricesExpanded,
                $"Matrices ({matrices.Count})", matrices.Count > 0);
            if (_matricesExpanded)
                foreach (var property in matrices)
                    ReadOnlyMatrix(content, ref y, property);

            _ = Foldout(content, ref y, false, "Buffers (0)", enabled: false);
            _ = Foldout(content, ref y, false, "Constant Buffers (0)", enabled: false);

            if (!string.IsNullOrWhiteSpace(item.Error))
            {
                _errorExpanded = Foldout(content, ref y, _errorExpanded, "Error");
                if (_errorExpanded)
                    EditorGUI.HelpBox(new Rect(content.x + 8, y, content.width - 16, 58),
                        item.Error, MessageType.Error);
            }
        }
        finally { GUI.EndScrollView(); }
    }

    private static bool Foldout(Rect area, ref Fix64 y, bool expanded, string title,
        bool enabled = true)
    {
        var rect = new Rect(area.x, y, area.width, RowHeight);
        using (new EditorGUI.DisabledScope(!enabled))
            expanded = EditorGUI.Foldout(rect, expanded, title, true, EditorStyles.foldoutHeader);
        y += RowHeight + 2;
        return enabled && expanded;
    }

    private static void ReadOnlyText(Rect area, ref Fix64 y, string label, string? value)
    {
        using (new EditorGUI.DisabledScope(true))
            _ = EditorGUI.TextField(Row(area, ref y), label,
                string.IsNullOrWhiteSpace(value) ? "-" : value);
    }

    private static void ReadOnlyInt(Rect area, ref Fix64 y, string label, int value)
    {
        using (new EditorGUI.DisabledScope(true))
            _ = EditorGUI.IntField(Row(area, ref y), label, value);
    }

    private static void ReadOnlyFloat(Rect area, ref Fix64 y, string label, float value)
    {
        using (new EditorGUI.DisabledScope(true))
            _ = EditorGUI.FloatField(Row(area, ref y), label, value);
    }

    private static void ReadOnlyToggle(Rect area, ref Fix64 y, string label, bool value)
    {
        using (new EditorGUI.DisabledScope(true))
            _ = EditorGUI.Toggle(Row(area, ref y), label, value);
    }

    private static void ReadOnlyEnum(Rect area, ref Fix64 y, string label, Enum value)
    {
        using (new EditorGUI.DisabledScope(true))
            _ = EditorGUI.EnumPopup(Row(area, ref y), label, value);
    }

    private static void ReadOnlyOptionalEnum<TEnum>(
        Rect area,
        ref Fix64 y,
        string label,
        TEnum? value)
        where TEnum : struct, Enum
    {
        if (value is { } enumValue)
        {
            ReadOnlyEnum(area, ref y, label, enumValue);
            return;
        }
        ReadOnlyText(area, ref y, label, "Not exposed by the graphics backend");
    }

    private static void ReadOnlyColor(Rect area, ref Fix64 y, string label, Color value)
    {
        using (new EditorGUI.DisabledScope(true))
            _ = EditorGUI.ColorField(Row(area, ref y), label, value, true, true, false);
    }

    private static void ReadOnlyVector(Rect area, ref Fix64 y, string label, Vector4 value)
    {
        using (new EditorGUI.DisabledScope(true))
            _ = EditorGUI.Vector4Field(Row(area, ref y), label, value);
    }

    private static void ReadOnlyMatrix(Rect area, ref Fix64 y, FrameDebugMatrixProperty property)
    {
        var value = property.Value;
        ReadOnlyVector(area, ref y, $"{property.Name} [0]",
            new Vector4((Fix64)value.M11, (Fix64)value.M12, (Fix64)value.M13, (Fix64)value.M14));
        ReadOnlyVector(area, ref y, $"{property.Name} [1]",
            new Vector4((Fix64)value.M21, (Fix64)value.M22, (Fix64)value.M23, (Fix64)value.M24));
        ReadOnlyVector(area, ref y, $"{property.Name} [2]",
            new Vector4((Fix64)value.M31, (Fix64)value.M32, (Fix64)value.M33, (Fix64)value.M34));
        ReadOnlyVector(area, ref y, $"{property.Name} [3]",
            new Vector4((Fix64)value.M41, (Fix64)value.M42, (Fix64)value.M43, (Fix64)value.M44));
    }

    private static void ObjectOrText(Rect area, ref Fix64 y, string label, BObject? value,
        string? fallback, bool allowSceneObjects)
    {
        if (value is not null)
        {
            _ = EditorGUI.ObjectField(Row(area, ref y), label, value, value.GetType(),
                allowSceneObjects);
            return;
        }
        ReadOnlyText(area, ref y, label, fallback);
    }

    private static Rect Row(Rect area, ref Fix64 y)
    {
        var row = new Rect(area.x + 8, y, Fix64.Max(1, area.width - 16), RowHeight);
        y += RowHeight + 1;
        return row;
    }

    private static Vector4 ToVector(GraphicsRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Color ToColor(NVector4 value) =>
        new((Fix64)value.X, (Fix64)value.Y, (Fix64)value.Z, (Fix64)value.W);

    private static Vector4 ToVector(NVector4 value) =>
        new((Fix64)value.X, (Fix64)value.Y, (Fix64)value.Z, (Fix64)value.W);

    private static BObject? ResolveSource(FrameDebugEvent item) =>
        item.Marker.SourceInstanceId is > 0 and var instanceId
            ? EditorUtility.InstanceIDToObject(instanceId)
            : null;

    private static BObject? ResolveGuid(Guid? guid) => guid is null
        ? null
        : ResolveAsset(guid.Value.ToString("N"));

    private static BObject? ResolveAsset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = AssetDatabase.GUIDToAssetPath(value);
        if (path.Length == 0 && value.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            path = value;
        return path.Length == 0 ? null : AssetDatabase.LoadMainAssetAtPath(path);
    }
}
