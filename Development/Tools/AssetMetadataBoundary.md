# Asset metadata and editor authoring ownership

Core owns the managed-object wire representation and type resolution. Its public
`ManagedAssetSerialization.GetSerializedTypeName` projects the persisted identity
without resolving a type, constructing an asset, restoring its graph, or selecting
a source path. Unrecognized formats and missing identities return null; parser
errors retain the existing public document exception behavior. Header inspection
does not validate payload versions. Legacy omitted format fields keep their default.
The mutable graph/document DTO remains internal.

AssetPipeline owns source-file classification and persistence. Its editor backend
reads bytes through its file service, then asks Core for the stored type identity.
Graphics editor icon lookup uses the same query and the active-domain serialized
type resolver. Neither package accesses the internal managed document model.

EditorServices owns authoring commands and asset callback policy. Scene/menu-asset
creation delegates writes to `ISceneDocumentCodec` / `IManagedAssetDocumentCodec`;
providers remain replaceable and missing providers fail clearly. Transient scenes
are disposed after creation, including failed writes. Existing document formats,
asset GUIDs, importer settings and load registration are retained.

AssetPostprocessor instantiation uses the public RuntimeTypeCache creation API.
Declared callback names, signatures, binding, ordering and error isolation belong
to EditorServices; it does not access Core's private factory/method caches. Method
metadata is shared within each registration pass and is not scanned per frame.
Private constructors and declared-only callback discovery preserve prior behavior.
Domain generation changes replace cached descriptors and callbacks.

`Test-AssetMetadataContracts.ps1` includes a Core-only consumer and authoring
integration using replacement document providers. Its fake editor host uses an
internal test harness, while all document providers and callbacks use public
contracts. No test consumer receives an InternalsVisibleTo grant. Player regression
must be rebuilt for this change because the Core assembly changed; a byte comparison
against an older Player distribution is not sufficient evidence for reusing it.
