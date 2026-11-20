# Editor capability boundary

Core owns the editor service composition mechanism, contribution invalidation and
host attachment lifetime. Packages own their editor tools and workflows. Packages
query `EditorServices.GetRequiredService<T>()`, or `GetService<T>()` for an optional
capability, instead of accessing the internal compatibility classes
`EditorServiceAccess` and `EditorHostServices`.

An editor host uses `EditorServiceScope.Attach(IServiceProvider)` during startup
and construction. Dispose the returned scope before disposing the host provider.
The scope supports nested and out-of-order disposal; it does not own host
singletons. Contribution-owned services are released when the host changes,
contributions change, or the scope detaches. Package extensions do not attach a
provider to install one individual feature; use the normal contribution mechanism.

`PathUtility.HasExtension/ChangeExtension` and `FileUtility.AppendAllText` expose
the existing generic operations through their registered platform capabilities.
They preserve the former editor helper semantics, including non-null extension
arguments and UTF-8 append without a BOM. No methods were added to platform
implementation interfaces. The internal editor helper delegates to the same
operations, avoiding duplicate implementations.

The repository's packages no longer access either internal service compatibility
class. Callback isolation and its internal diagnostics are unchanged by this
migration. Other internal APIs and manual friendship grants still require a
separate implementation review; this change does not certify all package
responsibilities.

`Test-EditorCapabilityContracts.ps1` uses an independent binary consumer without
friend access. It covers optional/required resolution, package facade selection,
host identity, nested/out-of-order/double disposal, contribution invalidation,
unload, host ownership, path rules, Unicode append and write failures. Existing
`Test-EditorServiceComposition.ps1` now exercises the public attachment and lookup
APIs while retaining its internal contribution-container ownership tests.
