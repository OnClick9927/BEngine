# Host launch and Player test ownership

Core and Platform transport startup inputs without interpreting package protocols.
`HostLaunchEnvironment` in Platform.Contracts encodes a versioned UTF-8 JSON map
as one unpadded base64url `bengineEnvironment` argument. Android resolves optional
portable relative file values inside its private files directory; Web resolves
them against its virtual root. Decoding validates the complete payload before
setting environment variables. Provider inputs retain their names and values.

Testing.Editor composes test filters, coverage, report destinations and arbitrary
content-provider options. Testing runtime writes local/device reports or sends
Web reports using the existing Networking package. It waits for delivery before
requesting quit and cancels an in-flight request when its scene stops. Normal
Players do not execute tests or open report requests. Testing therefore declares
its actual Networking dependency; Core and Platform contain no test-report API,
file polling or endpoint. The old private test query/Android extras are removed;
launchers and hosts must be updated together.

Player report JSON is written explicitly by Testing, preserving the editor wire
schema without constructor-name reflection metadata. This supports trimmed/AOT
Players and avoids an intermediate UTF-16 report plus UTF-8 re-encoding.
Test executables use the project's `Build/TestRunner`; intermediate content and
reports use `Temp/TestRunner`. The AssetBundles validation provider resolves its
content directory from the same normalized package setting as the content build,
including the project-name fallback for a blank setting.
Selected Player tests may live in assemblies loaded later by the game workflow.
Testing waits for every selected ID instead of reporting an empty AOT-scene run.
Discovery retries only after an assembly-load notification or a runtime type-index
generation change (including activation of precompiled domains). It removes the
load observation when execution starts or the scene stops.

The Testing loopback content server handles CORS preflight for GET requests with
custom/cache-control headers. Real browser tests cover this without request
interception, so automation cannot hide a broken cross-origin resource path.

`BEngineDevelopmentHost` selects the Android development manifest (debuggable,
loopback HTTP enabled). This is a platform development capability, independent
of a test package. Content validation requests it as a build property. Native
startup automation uses the generic `IPlayerStartupControl` contract and the
opt-in `BENGINE_PLAYER_AUTO_ADVANCE_STARTUP`; it does not set framework flags.

Run `Test-HostLaunchBoundary.ps1` after loading `SetToolEnvironment.ps1`, supplying
the freshly built Testing runtime/editor dependency closure and Node executable.
The external consumer checks the shared C#/Web wire cases, report completion and
cancellation, a real Edge launcher, and an ADB process fixture. The latter checks
Android command/data plumbing and does not claim a physical-device run. All
profiles, reports, fixture files and build output belong in the verification
directory under the repository Temp folder.
