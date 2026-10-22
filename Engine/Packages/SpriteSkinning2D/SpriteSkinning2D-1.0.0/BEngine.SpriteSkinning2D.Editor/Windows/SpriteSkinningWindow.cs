namespace BEngine.Editor;

[EditorWindowIcon("Icons/Assets/AssetImage.png")]
public sealed class SpriteSkinningWindow : EditorWindow
{
    private readonly EditorScrollView _leftScroll = new(), _rightScroll = new();
    private Vector2 _leftPosition, _rightPosition;
    private SpriteSkinAsset? _asset;
    private int _boneIndex, _vertexIndex, _attachmentIndex;
    private string _message = string.Empty;

    public SpriteSkinningWindow()
    {
        titleContent = new GUIContent("Sprite Skinning 2D", "Icons/Assets/AssetImage.png");
        minSize = new Vector2(820, 480);
    }

    public SpriteSkinAsset? asset => _asset;
    public int selectedBone => _boneIndex;
    public int selectedVertex => _vertexIndex;

    [MenuItem("Window/2D/Sprite Skinning", false, 214)]
    public static void Open() => GetWindow<SpriteSkinningWindow>("Sprite Skinning 2D").Show();

    public static void Open(SpriteSkinAsset asset)
    {
        var window = GetWindow<SpriteSkinningWindow>("Sprite Skinning 2D");
        window.SelectAsset(asset);
        window.Show();
    }

    [OnOpenAsset]
    private static bool OpenAsset(string path)
    {
        if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(SpriteSkinAsset) ||
            AssetDatabase.LoadAssetAtPath<SpriteSkinAsset>(path) is not { } value) return false;
        Open(value);
        return true;
    }

    protected override void OnSelectionChange()
    {
        if (Selection.activeObject is SpriteSkinAsset value) SelectAsset(value);
        else if (Selection.activeGameObject?.GetComponent<SpriteSkin2D>()?.skin is { } skin) SelectAsset(skin);
    }

    protected override void OnEnable()
    {
        Undo.undoRedoPerformed += HandleUndo;
        OnSelectionChange();
    }

    protected override void OnDisable() => Undo.undoRedoPerformed -= HandleUndo;
    private void HandleUndo() { ClampSelection(); Repaint(); SceneView.RepaintAll(); }

    public void SelectAsset(SpriteSkinAsset? value)
    {
        _asset = value;
        _boneIndex = _vertexIndex = _attachmentIndex = 0;
        _message = string.Empty;
        Repaint();
    }

    protected override void OnGUI()
    {
        DrawToolbar();
        if (_asset is not { isDestroyed: false })
        {
            EditorGUILayout.HelpBox("Create or select a Sprite Skin asset.");
            return;
        }
        var width = GUIUtility.currentViewWidth;
        var height = Fix64.Max(260, GUIUtility.currentViewHeight - 58);
        using (GUILayout.Area(new Rect(5, 31, Fix64.Max(260, width * Fix64.Parse("0.43") - 8), height)))
        {
            _leftPosition = _leftScroll.Begin(_leftPosition);
            try { DrawSkeleton(); DrawAttachments(); }
            finally { _leftScroll.End(); }
        }
        using (GUILayout.Area(new Rect(width * Fix64.Parse("0.43") + 3, 31,
                   Fix64.Max(360, width * Fix64.Parse("0.57") - 8), height)))
        {
            _rightPosition = _rightScroll.Begin(_rightPosition);
            try { DrawMesh(); }
            finally { _rightScroll.End(); }
        }
        GUI.Label(new Rect(7, GUIUtility.currentViewHeight - 24, width - 14, 20), _message, EditorStyles.miniLabel);
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.New, "New sprite skin", GUILayout.Width(26)))
                SelectAsset(SpriteSkinAsset.CreateDefault());
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite || _asset is null))
            if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Save, "Save sprite skin", GUILayout.Width(26))) Save();
        var selected = EditorGUILayout.ObjectField("", _asset, typeof(SpriteSkinAsset), false, GUILayout.Width(230)) as SpriteSkinAsset;
        if (!ReferenceEquals(selected, _asset)) SelectAsset(selected);
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite || _asset is null ||
                                           Selection.activeGameObject?.GetComponent<SpriteSkin2D>() is null))
            if (GUILayout.Button("Create / Bind Skeleton", EditorStyles.toolbarButton, GUILayout.Width(145)))
                BindSelectedRenderer();
        GUILayout.EndHorizontal();
    }

    private void DrawSkeleton()
    {
        var skin = _asset!;
        GUILayout.Label("Skeleton", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            if (GUILayout.Button("+ Bone")) AddBone(_boneIndex);
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite || skin.Bones.Count <= 1))
            if (GUILayout.Button("Delete")) RemoveBone(_boneIndex);
        GUILayout.EndHorizontal();
        for (var i = 0; i < skin.Bones.Count; i++)
        {
            var depth = 0;
            for (var ancestor = skin.Bones[i].ParentIndex; ancestor >= 0 && depth < skin.Bones.Count;
                 ancestor = skin.Bones[ancestor].ParentIndex) depth++;
            if (GUILayout.Button((i == _boneIndex ? "* " : string.Empty) + new string(' ', depth * 2) + skin.Bones[i].Name))
                _boneIndex = i;
        }
        var bone = skin.Bones[Math.Clamp(_boneIndex, 0, skin.Bones.Count - 1)];
        EditorGUI.BeginChangeCheck();
        var name = EditorGUILayout.TextField("Name", bone.Name);
        var parents = new[] { "None" }.Concat(skin.Bones.Take(_boneIndex).Select(item => item.Name)).ToArray();
        var parent = EditorGUILayout.Popup("Parent", Math.Clamp(bone.ParentIndex + 1, 0, parents.Length - 1), parents) - 1;
        var position = EditorGUILayout.Vector2Field("Bind position", bone.BindPosition);
        var rotation = Number("Bind rotation", bone.BindRotation);
        var scale = EditorGUILayout.Vector2Field("Bind scale", bone.BindScale);
        var length = Number("Length", bone.Length);
        if (EditorGUI.EndChangeCheck()) Edit("Edit sprite skin bone", () =>
        {
            bone.Name = name;
            bone.ParentIndex = parent;
            bone.BindPosition = position;
            bone.BindRotation = rotation;
            bone.BindScale = scale;
            bone.Length = length;
        });
    }

    private void DrawAttachments()
    {
        var skin = _asset!;
        GUILayout.Space(12);
        GUILayout.Label("Attachments", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
            if (GUILayout.Button("+ Attachment")) AddAttachment("Attachment", "missing:sprite-skin");
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite || skin.Attachments.Count <= 1))
            if (GUILayout.Button("Delete")) RemoveAttachment(_attachmentIndex);
        GUILayout.EndHorizontal();
        var names = skin.Attachments.Select(item => item.Name).ToArray();
        _attachmentIndex = EditorGUILayout.Popup("Active", Math.Clamp(_attachmentIndex, 0, names.Length - 1), names);
        var attachment = skin.Attachments[_attachmentIndex];
        EditorGUI.BeginChangeCheck();
        var name = EditorGUILayout.TextField("Name", attachment.Name);
        var texture = EditorGUILayout.TextField("Texture", attachment.Texture);
        var uv = RectValue("UV", attachment.Uv);
        var tint = EditorGUILayout.ColorField("Tint", attachment.Tint);
        if (EditorGUI.EndChangeCheck()) Edit("Edit sprite skin attachment", () =>
        {
            attachment.Name = name;
            attachment.Texture = texture;
            attachment.Uv = uv;
            attachment.Tint = tint;
        });
    }

    private void DrawMesh()
    {
        var skin = _asset!;
        GUILayout.Label("Mesh and weights", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            if (GUILayout.Button("Quad")) ResetQuad();
            if (GUILayout.Button("+ Vertex")) AddVertex(skin.Vertices[_vertexIndex].Position, skin.Vertices[_vertexIndex].Uv);
            if (GUILayout.Button("Triangulate")) Triangulate();
            if (GUILayout.Button("Auto Weights")) AutoWeights();
            if (GUILayout.Button("Trace Alpha")) TraceAlpha();
        }
        GUILayout.EndHorizontal();
        for (var i = 0; i < skin.Vertices.Count; i++)
            if (GUILayout.Button((i == _vertexIndex ? "* " : string.Empty) + $"Vertex {i}: {skin.Vertices[i].Position}"))
                _vertexIndex = i;
        var vertex = skin.Vertices[Math.Clamp(_vertexIndex, 0, skin.Vertices.Count - 1)];
        EditorGUI.BeginChangeCheck();
        var position = EditorGUILayout.Vector2Field("Position", vertex.Position);
        var uv = EditorGUILayout.Vector2Field("UV", vertex.Uv);
        if (EditorGUI.EndChangeCheck()) Edit("Move sprite skin vertex", () =>
        {
            vertex.Position = position;
            vertex.Uv = uv;
        });
        GUILayout.Label("Bone weights", EditorStyles.boldLabel);
        for (var boneIndex = 0; boneIndex < skin.Bones.Count; boneIndex++)
        {
            var existing = vertex.Weights.FirstOrDefault(item => item.BoneIndex == boneIndex)?.Weight ?? Fix64.Zero;
            var changed = (Fix64)EditorGUILayout.Slider(skin.Bones[boneIndex].Name, (float)existing, 0, 1);
            if (changed != existing) SetWeight(_vertexIndex, boneIndex, changed);
        }
        GUILayout.Label($"{skin.Triangles.Count / 3} triangles; {skin.Vertices.Count} vertices", EditorStyles.miniLabel);
    }

    public bool TraceAlpha(byte threshold = 1)
    {
        if (_asset is null) return false;
        try
        {
            var attachment = _asset.Attachments[_attachmentIndex];
            var texture = BObject.FindObjectsByType<Texture>().FirstOrDefault(value => value.assetPath == attachment.Texture)
                          ?? BAsset.Load<Texture>(attachment.Texture);
            if (texture is null) throw new InvalidDataException("Choose a readable texture for the selected attachment.");
            var pixels = texture.GetPixels();
            var uv = attachment.Uv;
            var left = Math.Clamp((int)(uv.x * texture.width), 0, texture.width - 1);
            var top = Math.Clamp((int)(uv.y * texture.height), 0, texture.height - 1);
            var width = Math.Clamp((int)(uv.width * texture.width), 1, texture.width - left);
            var height = Math.Clamp((int)(uv.height * texture.height), 1, texture.height - top);
            var rgba = new byte[checked(width * height * 4)];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    rgba[(y * width + x) * 4 + 3] = (byte)Math.Clamp((int)(pixels[(top + y) * texture.width + left + x].a * 255), 0, 255);
            return TraceAlpha(width, height, rgba, new Vector2(Fix64.Half, Fix64.Half), texture.pixelsPerUnit, threshold);
        }
        catch (Exception error) { _message = error.Message; return false; }
    }

    public bool TraceAlpha(int width, int height, ReadOnlySpan<byte> rgba, Vector2 pivot, Fix64 pixelsPerUnit, byte threshold = 1)
    {
        if (_asset is null) return false;
        var mesh = SpriteSkinAlphaMesh.Build(width, height, rgba, pivot, pixelsPerUnit, threshold, _boneIndex);
        return Edit("Trace sprite skin alpha", () => { _asset.Vertices = mesh.Vertices; _asset.Triangles = mesh.Triangles; _vertexIndex = 0; });
    }

    public bool Save()
    {
        EditorAssetWritePolicy.EnsureCanWrite("Save sprite skin");
        if (_asset is null) return false;
        try
        {
            _asset.Validate();
            if (string.IsNullOrWhiteSpace(_asset.assetPath))
            {
                var folder = ProjectWindowUtil.GetActiveFolderPath() ?? "Assets";
                var path = AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/', '\\') + "/New Sprite Skin.spriteskin.yaml");
                AssetDatabase.CreateAsset(_asset, path);
            }
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            _message = "Saved " + _asset.assetPath;
            return true;
        }
        catch (Exception error) { _message = error.Message; return false; }
    }

    public bool AddBone(int parentIndex)
    {
        if (_asset is null) return false;
        parentIndex = Math.Clamp(parentIndex, -1, _asset.Bones.Count - 1);
        return Edit("Add sprite skin bone", () =>
        {
            var parent = parentIndex >= 0 ? _asset.Bones[parentIndex] : null;
            var position = parent is null ? Vector2.zero : BoneTip(parent);
            _asset.Bones.Add(new SpriteSkinBone
            {
                Name = Unique("Bone", _asset.Bones.Select(item => item.Name)),
                ParentIndex = parentIndex,
                BindPosition = position,
                BindRotation = parent?.BindRotation ?? Fix64.Zero,
                BindScale = Vector2.one,
                Length = parent?.Length ?? Fix64.One
            });
            _boneIndex = _asset.Bones.Count - 1;
        });
    }

    public bool RemoveBone(int index)
    {
        if (_asset is null || _asset.Bones.Count <= 1 || (uint)index >= (uint)_asset.Bones.Count) return false;
        return Edit("Remove sprite skin bone", () =>
        {
            var replacement = _asset.Bones[index].ParentIndex;
            _asset.Bones.RemoveAt(index);
            foreach (var bone in _asset.Bones)
            {
                if (bone.ParentIndex == index) bone.ParentIndex = replacement;
                else if (bone.ParentIndex > index) bone.ParentIndex--;
            }
            foreach (var vertex in _asset.Vertices)
            {
                foreach (var weight in vertex.Weights)
                {
                    if (weight.BoneIndex == index) weight.BoneIndex = replacement >= 0 ? replacement : 0;
                    else if (weight.BoneIndex > index) weight.BoneIndex--;
                }
                Normalize(vertex);
            }
            _boneIndex = Math.Min(index, _asset.Bones.Count - 1);
        });
    }

    public bool ReparentBone(int index, int parentIndex) =>
        _asset is not null && (uint)index < (uint)_asset.Bones.Count && parentIndex >= -1 && parentIndex < index &&
        Edit("Reparent sprite skin bone", () => _asset.Bones[index].ParentIndex = parentIndex);

    public bool AddVertex(Vector2 position, Vector2 uv)
    {
        if (_asset is null) return false;
        return Edit("Add sprite skin vertex", () =>
        {
            _asset.Vertices.Add(new SpriteSkinVertex
            {
                Position = position, Uv = uv,
                Weights = [new SpriteSkinWeight { BoneIndex = Math.Clamp(_boneIndex, 0, _asset.Bones.Count - 1) }]
            });
            _vertexIndex = _asset.Vertices.Count - 1;
        });
    }

    public bool SetVertex(int index, Vector2 position, Vector2 uv) =>
        _asset is not null && (uint)index < (uint)_asset.Vertices.Count &&
        Edit("Edit sprite skin vertex", () =>
        {
            _asset.Vertices[index].Position = position;
            _asset.Vertices[index].Uv = uv;
        });

    public bool SetWeight(int vertexIndex, int boneIndex, Fix64 weight)
    {
        if (_asset is null || (uint)vertexIndex >= (uint)_asset.Vertices.Count ||
            (uint)boneIndex >= (uint)_asset.Bones.Count) return false;
        return Edit("Paint sprite skin weight", () => ApplyWeight(
            _asset.Vertices[vertexIndex], boneIndex, weight));
    }

    public bool AutoWeights()
    {
        if (_asset is null) return false;
        return Edit("Generate sprite skin weights", () =>
        {
            foreach (var vertex in _asset.Vertices)
            {
                var candidates = _asset.Bones.Select((bone, index) => new
                    {
                        Index = index,
                        Distance = DistanceToSegment(vertex.Position, bone.BindPosition, BoneTip(bone))
                    })
                    .OrderBy(item => item.Distance).Take(Math.Min(4, _asset.Bones.Count)).ToArray();
                if (candidates[0].Distance <= Fix64.Parse("0.0001"))
                    vertex.Weights = [new SpriteSkinWeight { BoneIndex = candidates[0].Index }];
                else
                {
                    vertex.Weights = candidates.Select(item => new SpriteSkinWeight
                    {
                        BoneIndex = item.Index,
                        Weight = Fix64.One / Fix64.Max(Fix64.Parse("0.001"), item.Distance)
                    }).ToList();
                    Normalize(vertex);
                }
            }
        });
    }

    public bool Triangulate()
    {
        if (_asset is null || _asset.Vertices.Count < 3) return false;
        return Edit("Triangulate sprite skin mesh", () => _asset.Triangles = EarClip(
            _asset.Vertices.Select(item => item.Position).ToArray()));
    }

    public bool ResetQuad()
    {
        if (_asset is null) return false;
        return Edit("Reset sprite skin quad", () =>
        {
            var half = Fix64.Half;
            _asset.Vertices =
            [
                Vertex(new(-half, -half), new(0, 1)), Vertex(new(half, -half), new(1, 1)),
                Vertex(new(half, half), new(1, 0)), Vertex(new(-half, half), new(0, 0))
            ];
            _asset.Triangles = [0, 1, 2, 0, 2, 3];
            _vertexIndex = 0;
        });
    }

    public bool AddAttachment(string name, string texture)
    {
        if (_asset is null) return false;
        return Edit("Add sprite skin attachment", () =>
        {
            _asset.Attachments.Add(new SpriteSkinAttachment
            {
                Name = Unique(name, _asset.Attachments.Select(item => item.Name)),
                Texture = string.IsNullOrWhiteSpace(texture) ? "missing:sprite-skin" : texture,
                Uv = new Rect(0, 0, 1, 1)
            });
            _attachmentIndex = _asset.Attachments.Count - 1;
        });
    }

    public bool RenameAttachment(int index, string name) =>
        _asset is not null && (uint)index < (uint)_asset.Attachments.Count &&
        Edit("Rename sprite skin attachment", () => _asset.Attachments[index].Name = name);

    public bool RemoveAttachment(int index)
    {
        if (_asset is null || _asset.Attachments.Count <= 1 || (uint)index >= (uint)_asset.Attachments.Count) return false;
        return Edit("Remove sprite skin attachment", () =>
        {
            _asset.Attachments.RemoveAt(index);
            _attachmentIndex = Math.Min(index, _asset.Attachments.Count - 1);
        });
    }

    public bool BindSelectedRenderer()
    {
        EditorAssetWritePolicy.EnsureCanWrite("Create sprite skin skeleton");
        if (_asset is null || Selection.activeGameObject?.GetComponent<SpriteSkin2D>() is not { } renderer ||
            renderer.gameObject.scene is not { } scene) return false;
        _asset.Validate();
        var transforms = new Transform[_asset.Bones.Count];
        var skeleton = scene.CreateGameObject(_asset.name + " Skeleton");
        skeleton.transform.SetParent(renderer.transform, false);
        for (var index = 0; index < _asset.Bones.Count; index++)
        {
            var definition = _asset.Bones[index];
            var owner = scene.CreateGameObject(definition.Name);
            var parent = definition.ParentIndex >= 0 ? transforms[definition.ParentIndex] : skeleton.transform;
            owner.transform.SetParent(parent, false);
            owner.transform.position = renderer.transform.TransformPoint(definition.BindPosition);
            owner.transform.rotation = renderer.transform.rotation + definition.BindRotation;
            var bone = owner.AddComponent<Bone2D>();
            bone.length = definition.Length;
            transforms[index] = owner.transform;
        }
        Undo.RegisterCreatedObjectUndo(skeleton, "Create sprite skin skeleton");
        Undo.RecordObject(renderer, "Bind sprite skin skeleton");
        renderer.skin = _asset;
        renderer.rootBone = skeleton.transform;
        renderer.bones = transforms;
        renderer.attachment = _asset.Attachments[0].Name;
        EditorUtility.SetDirty(renderer);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorApplication.RepaintHierarchyWindow();
        _message = $"Created and bound {_asset.Bones.Count} bones.";
        return true;
    }

    internal static void ApplyWeight(SpriteSkinVertex vertex, int boneIndex, Fix64 weight)
    {
        weight = Fix64.Clamp(weight, Fix64.Zero, Fix64.One);
        var existing = vertex.Weights.FirstOrDefault(item => item.BoneIndex == boneIndex);
        if (existing is null && weight > 0) vertex.Weights.Add(new SpriteSkinWeight { BoneIndex = boneIndex, Weight = weight });
        else if (existing is not null) existing.Weight = weight;
        vertex.Weights.RemoveAll(item => item.Weight <= Fix64.Epsilon);
        if (vertex.Weights.Count == 0) vertex.Weights.Add(new SpriteSkinWeight { BoneIndex = boneIndex });
        Normalize(vertex);
    }

    private bool Edit(string label, Action action)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        if (_asset is null) return false;
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.RecordObject(_asset, label);
        try
        {
            action();
            _asset.Validate();
            EditorUtility.SetDirty(_asset);
            _message = string.Empty;
            ClampSelection();
            SceneView.RepaintAll();
            Repaint();
            return true;
        }
        catch (Exception error)
        {
            Undo.RevertAllDownToGroup(group);
            _message = error.Message;
            ClampSelection();
            return false;
        }
    }

    private void ClampSelection()
    {
        if (_asset is null) return;
        _boneIndex = Math.Clamp(_boneIndex, 0, Math.Max(0, _asset.Bones.Count - 1));
        _vertexIndex = Math.Clamp(_vertexIndex, 0, Math.Max(0, _asset.Vertices.Count - 1));
        _attachmentIndex = Math.Clamp(_attachmentIndex, 0, Math.Max(0, _asset.Attachments.Count - 1));
    }

    private SpriteSkinVertex Vertex(Vector2 position, Vector2 uv) => new()
    {
        Position = position,
        Uv = uv,
        Weights = [new SpriteSkinWeight { BoneIndex = Math.Clamp(_boneIndex, 0, _asset!.Bones.Count - 1) }]
    };

    private static void Normalize(SpriteSkinVertex vertex)
    {
        vertex.Weights = vertex.Weights.Where(item => item.Weight > Fix64.Epsilon)
            .GroupBy(item => item.BoneIndex)
            .Select(group => new SpriteSkinWeight
            {
                BoneIndex = group.Key,
                Weight = group.Aggregate(Fix64.Zero, (sum, item) => sum + item.Weight)
            }).OrderByDescending(item => item.Weight).Take(4).ToList();
        if (vertex.Weights.Count == 0) vertex.Weights.Add(new SpriteSkinWeight());
        var total = vertex.Weights.Aggregate(Fix64.Zero, (sum, item) => sum + item.Weight);
        foreach (var item in vertex.Weights) item.Weight /= total;
    }

    private static Vector2 BoneTip(SpriteSkinBone bone)
    {
        var radians = bone.BindRotation * Fix64.Deg2Rad;
        return bone.BindPosition + new Vector2(Fix64.Cos(radians), Fix64.Sin(radians)) * bone.Length;
    }

    private static Fix64 DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var denominator = segment.sqrMagnitude;
        if (denominator <= Fix64.Epsilon) return Vector2.Distance(point, start);
        var time = Fix64.Clamp(Vector2.Dot(point - start, segment) / denominator, Fix64.Zero, Fix64.One);
        return Vector2.Distance(point, start + segment * time);
    }

    private static List<int> EarClip(IReadOnlyList<Vector2> points)
    {
        if (points.Count < 3) throw new InvalidDataException("A polygon needs at least three vertices.");
        var signedArea = Fix64.Zero;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            signedArea += points[index].x * next.y - next.x * points[index].y;
        }
        if (Fix64.Abs(signedArea) <= Fix64.Epsilon) throw new InvalidDataException("The vertex outline has zero area.");
        var indices = Enumerable.Range(0, points.Count).ToList();
        if (signedArea < 0) indices.Reverse();
        var triangles = new List<int>((points.Count - 2) * 3);
        var guard = points.Count * points.Count;
        while (indices.Count > 3 && guard-- > 0)
        {
            var clipped = false;
            for (var cursor = 0; cursor < indices.Count; cursor++)
            {
                var a = indices[(cursor + indices.Count - 1) % indices.Count];
                var b = indices[cursor];
                var c = indices[(cursor + 1) % indices.Count];
                if (Cross(points[a], points[b], points[c]) <= Fix64.Epsilon) continue;
                if (indices.Any(candidate => candidate != a && candidate != b && candidate != c &&
                                             InsideTriangle(points[candidate], points[a], points[b], points[c]))) continue;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                indices.RemoveAt(cursor);
                clipped = true;
                break;
            }
            if (!clipped) throw new InvalidDataException("The outline self-intersects or cannot be triangulated.");
        }
        if (indices.Count != 3) throw new InvalidDataException("The outline could not be triangulated.");
        triangles.AddRange(indices);
        return triangles;
    }

    private static Fix64 Cross(Vector2 a, Vector2 b, Vector2 c) =>
        (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    private static bool InsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c) =>
        Cross(a, b, point) >= 0 && Cross(b, c, point) >= 0 && Cross(c, a, point) >= 0;

    private static string Unique(string prefix, IEnumerable<string> existing)
    {
        prefix = string.IsNullOrWhiteSpace(prefix) ? "Item" : prefix.Trim();
        var names = existing.ToHashSet(StringComparer.Ordinal);
        var result = prefix;
        for (var suffix = 2; names.Contains(result); suffix++) result = prefix + " " + suffix;
        return result;
    }

    private static Fix64 Number(string label, Fix64 value) =>
        (Fix64)EditorGUILayout.FloatField(label, (float)value);

    private static Rect RectValue(string label, Rect value)
    {
        GUILayout.Label(label, EditorStyles.miniLabel);
        GUILayout.BeginHorizontal();
        var x = (Fix64)EditorGUILayout.FloatField("X", (float)value.x);
        var y = (Fix64)EditorGUILayout.FloatField("Y", (float)value.y);
        var width = (Fix64)EditorGUILayout.FloatField("W", (float)value.width);
        var height = (Fix64)EditorGUILayout.FloatField("H", (float)value.height);
        GUILayout.EndHorizontal();
        return new Rect(x, y, width, height);
    }
}
