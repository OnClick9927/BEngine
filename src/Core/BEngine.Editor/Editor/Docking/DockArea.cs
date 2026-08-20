using System.Diagnostics;
using System.Reflection;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;
using NVector4 = System.Numerics.Vector4;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

internal enum DockArea { Left, Center, Right, Bottom }
