# Editor service composition regression

Run against an exported engine directory (the directory containing `BEngine.dll`):

```powershell
./Development/Tools/Test-EditorServiceComposition.ps1 `
    -EngineBinaries ./Engine/BEgine/BEgine-0.1.0
```

To test a newly compiled Core Editor without first replacing the installation,
pass `-EditorAssembly <path-to-BEngine.Editor.dll>`. All build outputs, logs and
results are written under the workspace's `Temp/Engine/Verification` directory,
or an explicitly supplied `-OutputDirectory`.

The probe attaches a real Microsoft DI host, registers contributions after the
host was built, and verifies constructor/factory injection, inherited singleton
identity, merged enumerations, keyed/open generic services, scope lifetimes,
invalidation, detachment and ownership during disposal. It also checks the
existing-host lookup path for managed allocations after warmup. Reflection is
limited to the internal composition entry points; no test friend grant is added.

Ownership contract:

- Host registrations retain priority and their original lifetime and owner.
- Late extension constructors and factories resolve dependencies through the
  combined provider. Enumerations contain host entries followed by late entries.
- Singleton extensions belong to the contribution root; scoped extensions belong
  to the requested scope. A child scope owns a new host scope, not the host root.
- Host instances and externally supplied instances are borrowed. Extension-created
  disposable instances are released once, in reverse creation order.
- Registry invalidation or detachment releases the obsolete contribution root.
  Repeated lookups do not rebuild it. Existing host lookups keep the direct path.

This is an internal Editor contribution layer. It does not replace the main DI
container, change runtime DI APIs, or move composition into the game framework.
