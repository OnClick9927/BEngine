# Player metadata ownership

Core Player owns `PlayerMetadataDocument`, `PlayerMetadataSerializer` and
`PlayerMetadataBinary`: the bounded binary envelope, integrity check, atomic file
publication, common project/runtime settings and host deployment fields. It also
reads and discards the historical build-time package inventory. It does not
interpret a content provider's settings or choose its loading/update policy.

Content providers contribute `PlayerMetadataSection` values with an identity,
version and opaque bytes. IDs 1, 2, 4 and 5 remain reserved for the existing Core
fields and legacy inventory; other IDs belong to the consuming deployment
contract. Sections are local to a document, with no global callback registry.
Serialization borrows caller-owned section memory for the synchronous call.
Deserialize owns a private input copy; Load owns its file buffer. Returned opaque
sections refer to that owned buffer. Output cannot grow beyond the total metadata
limit while it is being written; section and input limits also remain enforced.

The AssetBundles `RuntimeMetadataDocument` and `RuntimeMetadataSerializer` remain
public compatibility APIs. Their facade contributes and validates the historical
content settings section. It preserves the existing section order, format/version
identities and current output bytes. Its settings writer/reader and validation
remain in the package; it no longer implements the common envelope, project or
host field codecs, file publication or checksum. Core has no package reference,
package identity, package type forwarder or content-specific settings branch.

The local Unity 2022.3.62f2c1 XML exposes PlayerSettings.companyName/productName and
BuildPipeline.BuildPlayer in UnityEditor.CoreModule. This supports keeping common
Player configuration and build mechanisms separate from a content-loading domain;
it does not establish Unity's native Player metadata format or implementation.

`Tests/PlayerMetadataOwnership` verifies a standalone Core consumer, provider
sections, buffer ownership, atomic replacement/failure, bounded output and a
package adapter against fixtures generated/validated by the previous real
implementation. Verification records distinguish compatible serialized bytes
from complete game startup. Outputs belong under workspace Temp. The remaining
built-in archive and full Example/Snake Player reviews are separate work.
