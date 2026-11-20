using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
namespace BEngine.Serialization;
internal sealed class SourcePrefabBackend : IPrefabResolutionBackend, IPrefabAuthoringBackend
{
    [ModuleInitializer]
    internal static void Register()
    {
        var backend = new SourcePrefabBackend();
        RuntimeServiceContributions.RegisterDefault<IPrefabResolutionBackend>(backend);
        RuntimeServiceContributions.RegisterDefault<IPrefabAuthoringBackend>(backend);
    }
    public PrefabDocumentSnapshot Resolve(PrefabAsset asset) => SourcePrefabResolver.Resolve(asset);
    public PrefabAsset Load(string reference) => SourcePrefabResolver.Load(reference);
    public string Reference(PrefabAsset asset) => SourcePrefabResolver.Reference(asset);
    public void ResolveReferences(SerializedHierarchy objects, PrefabReferenceSet references) => SourcePrefabResolver.ResolveReferences(objects, references);
    public SerializedHierarchy Merge(SerializedHierarchy baseline, SerializedHierarchy current, SerializedHierarchy source) => PrefabMerge.Merge(baseline, current, source);
    public PrefabAsset SaveVariant(GameObject root, PrefabAsset basePrefab, string path, Guid? assetId) => SourcePrefabAuthoring.SaveVariant(root,basePrefab,path,assetId);
}
