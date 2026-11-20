[CmdletBinding()]
param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '../../src'),
    [string[]]$AssemblyDirectories = @(),
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceRoot)
$packages = Join-Path $source 'Packages'
$scopes = @('Core')
$identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$packageAssemblies = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$bootstrapRoots = [Collections.Generic.List[string]]::new()
$platformAdapterRoots = [Collections.Generic.List[string]]::new()
foreach ($manifest in Get-ChildItem -LiteralPath $packages -Filter package.yaml -Recurse -File |
    Where-Object { $_.Directory.Parent.FullName -eq $packages }) {
    $text = [IO.File]::ReadAllText($manifest.FullName)
    if ($text -match '(?m)^  bootstrap:\s*true\s*$') { $bootstrapRoots.Add($manifest.Directory.FullName + [IO.Path]::DirectorySeparatorChar) }
    $descriptor = Join-Path $manifest.Directory.FullName 'Editor/tooling.json'
    if ((Test-Path -LiteralPath $descriptor) -and ([IO.File]::ReadAllText($descriptor) -match '"platformAdapter"\s*:\s*true')) {
        $platformAdapterRoots.Add($manifest.Directory.FullName + [IO.Path]::DirectorySeparatorChar)
    }
    foreach ($match in [regex]::Matches($text, '(?m)^id:\s*([^\r\n#]+)')) {
        [void]$identities.Add($match.Groups[1].Value.Trim().Trim('"', "'"))
    }
    foreach ($match in [regex]::Matches($text, '(?m)^  assembly:\s*([^\r\n#]+)')) {
        $name = $match.Groups[1].Value.Trim().Trim('"', "'")
        # Shared namespaces also name historical assemblies. Text matching their
        # prefixes would flag every BEngine namespace; PE references remain exact.
        if ($name -notin 'BEngine', 'BEngine.Editor') { [void]$identities.Add($name) }
        [void]$packageAssemblies.Add($name)
    }
    # Package-specific namespaces are an equally important boundary: the public
    # namespace need not match its assembly name. Shared engine namespaces alone
    # cannot identify an owner and are checked through project/assembly references.
    foreach ($match in [regex]::Matches($text, '(?m)^  rootNamespace:\s*([^\r\n#]+)')) {
        $name = $match.Groups[1].Value.Trim().Trim('"', "'")
        if ($name -notin 'BEngine', 'BEngine.Editor', 'BEngine.Platform') {
            [void]$identities.Add($name)
        }
    }
    $editor = Join-Path $manifest.Directory.FullName 'Editor'
    if (Test-Path -LiteralPath $editor -PathType Container) {
        foreach ($project in Get-ChildItem -LiteralPath $editor -Filter *.csproj -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|BuildTools)[\\/]' }) {
            [xml]$toolingProject = [IO.File]::ReadAllText($project.FullName)
            $declared = @($toolingProject.Project.PropertyGroup.AssemblyName | Where-Object { $_ })
            $name = if ($declared.Count -eq 1) { $declared[0] } else { $project.BaseName }
            [void]$identities.Add($name)
            [void]$packageAssemblies.Add($name)
        }
    }
}
if ($packageAssemblies.Count -eq 0) { throw 'No source package assembly declarations found.' }
# The default workbench is a composition root. Feature packages use shared
# authoring APIs instead of depending on its windows or default host.
$workbenchConsumers = [Collections.Generic.List[string]]::new()
$frameworkConsumers = [Collections.Generic.List[string]]::new()
$platformBuildConsumers = [Collections.Generic.List[string]]::new()
$violations = [Collections.Generic.List[string]]::new()
foreach ($directory in Get-ChildItem -LiteralPath $packages -Directory -Filter 'Platform*') {
    $violations.Add("$([IO.Path]::GetRelativePath($source, $directory.FullName)): platform implementation remains a package.")
}
foreach ($manifest in Get-ChildItem -LiteralPath (Join-Path $source 'Platform') -Filter package.yaml -Recurse -File) {
    $violations.Add("$([IO.Path]::GetRelativePath($source, $manifest.FullName)): platform implementation declares a package.")
}
foreach ($directory in Get-ChildItem -LiteralPath $packages -Directory | Where-Object Name -NE 'Application') {
    foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -File -Recurse |
        Where-Object { $_.Name -eq 'package.yaml' -or $_.Name -like '*.asmdef.yaml' -or $_.Extension -eq '.csproj' }) {
        $text = [IO.File]::ReadAllText($file.FullName)
        if ($text -match 'com\.bengine\.application|BEngine\.Application(?:\.Editor)?(?:[/\\.<\s]|$)') {
            $frameworkConsumers.Add([IO.Path]::GetRelativePath($source, $file.FullName))
        }
    }
}
foreach ($directory in Get-ChildItem -LiteralPath $packages -Directory | Where-Object Name -NE 'EditorWorkbench') {
    foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -File -Recurse |
        Where-Object { $_.Name -eq 'package.yaml' -or $_.Name -like '*.asmdef.yaml' -or $_.Extension -eq '.csproj' }) {
        $text = [IO.File]::ReadAllText($file.FullName)
        if ($text -match 'com\.bengine\.editor-workbench|BEngine\.EditorWorkbench\.(?:Editor|Host)') {
            $workbenchConsumers.Add([IO.Path]::GetRelativePath($source, $file.FullName))
        }
    }
}
# Platform.Build contains concrete publisher implementations. Core and feature
# packages consume only the neutral build contracts from Core Editor; keep a
# direct project/asmdef/manifest reference from silently reappearing.
foreach ($root in @((Join-Path $source 'Core'), $packages)) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File |
        Where-Object { $_.Extension -in '.csproj', '.yaml' -and $_.FullName -notmatch '[\\/](?:bin|obj|artifacts)[\\/]' }) {
        $text = [IO.File]::ReadAllText($file.FullName)
        if ($text -match '(?i)(?:BEngine\.Platform\.Build|Platform[\\/]BEngine\.Platform\.Build)') {
            if (-not @($platformAdapterRoots | Where-Object { $file.FullName.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count) {
                $platformBuildConsumers.Add([IO.Path]::GetRelativePath($source, $file.FullName))
            }
        }
    }
}
$platformBuildConsumers | ForEach-Object { $violations.Add("${_}: Core or feature package directly references the concrete Platform.Build implementation.") }
$pattern = '(?<![A-Za-z0-9_])(?:' + (($identities | Sort-Object | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')(?![A-Za-z0-9_])'
foreach ($file in Get-ChildItem -LiteralPath $packages -Filter *.cs -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|artifacts)[\\/]' }) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $bootstrapImplementation = @($bootstrapRoots | Where-Object { $file.FullName.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
    if (-not $bootstrapImplementation -and $text -match '\bRuntimeServiceAccess\b|\bRuntimeServices\.(?:FileSystem|PathService|PlatformService|ApplicationPaths|Attach|Resolve)\b') {
        $violations.Add("$([IO.Path]::GetRelativePath($source, $file.FullName)): package uses the private runtime service locator instead of the public borrowed-service contract.")
    }
    if (-not $bootstrapImplementation -and $file.FullName -notmatch '[\\/]Packages[\\/]Application[\\/]' -and $text -match '\bBENGINE_AOT_AUTO_') {
        $violations.Add("$([IO.Path]::GetRelativePath($source, $file.FullName)): feature package interprets the application framework's startup protocol.")
    }
}
foreach ($consumer in $workbenchConsumers) { $violations.Add("${consumer}: feature package depends on the default editor workbench.") }
foreach ($consumer in $frameworkConsumers) { $violations.Add("${consumer}: engine feature or tooling host depends on the game development framework.") }
$engineAssemblies = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$restrictedAssemblies = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$packageFriendGrants = @{}
$sourceFiles = 0
foreach ($scope in $scopes) {
    $directory = Join-Path $source $scope
    if (-not (Test-Path -LiteralPath $directory)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.csproj', '.props', '.targets', '.yaml', '.md', '.html', '.json', '.ps1', '.js', '.mjs', '.c', '.h' -and
            $_.FullName -notmatch '[\\/](?:bin|obj|artifacts)[\\/]' }) {
        $sourceFiles++
        $text = [IO.File]::ReadAllText($file.FullName)
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        if ($file.Extension -in '.cs', '.js', '.csproj', '.props', '.targets' -and
            $text -match '\bBENGINE_TEST_RUNNER_|\bbengineRunTests\b|\bbengineTest(?:Result|Coverage|Ids64|ContentUrl|Token|Ids)\b|\bGetTestResult\b|\bBEngineTestPlayer\b|TestAndroidManifest\.xml|/bengine-tests\.json') {
            $violations.Add("${relative}: engine or platform implementation interprets a concrete test-runner protocol.")
        }
        foreach ($match in [regex]::Matches($text, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $line = 1 + [regex]::Matches($text.Substring(0, $match.Index), '\n').Count
            $lineStart = $text.LastIndexOf("`n", [Math]::Max(0, $match.Index - 1)) + 1
            $lineEnd = $text.IndexOf("`n", $match.Index)
            if ($lineEnd -lt 0) { $lineEnd = $text.Length }
            $sourceLine = $text.Substring($lineStart, $lineEnd - $lineStart)
            if ($relative -match '^Core[\\/]BEngine\.Core\.Editor[\\/]EditorServices[\\/]' -and $file.Extension -eq '.cs' -and
                $sourceLine -match '^\s*\[(?:RuntimeTypeAlias\(|assembly:\s*InternalsVisibleTo\()') {
                continue
            }
            $violations.Add("${relative}:$line references concrete package identity '$($match.Value)'.")
        }
        if ($file.Extension -eq '.csproj') {
            [xml]$project = $text
            $declared = @($project.Project.PropertyGroup.AssemblyName | Where-Object { $_ })
            $name = if ($declared.Count -eq 1) { $declared[0] } else { $file.BaseName }
            [void]$engineAssemblies.Add($name)
            # Engine implementations may collaborate internally; feature packages use public contracts.
            [void]$restrictedAssemblies.Add($name)
            foreach ($reference in $project.Project.ItemGroup.ProjectReference) {
                if ($reference.Include -and $reference.Include -match '(?:^|[\\/])Packages[\\/]') {
                    $violations.Add("${relative}: project references a package source project: $($reference.Include)")
                }
            }
        }
    }
}

$checked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($directory in $AssemblyDirectories) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Assembly directory not found: $directory" }
    foreach ($file in Get-ChildItem -LiteralPath $directory -Filter *.dll -File -Recurse) {
        if (-not $engineAssemblies.Contains($file.BaseName)) { continue }
        $stream = [IO.File]::OpenRead($file.FullName)
        try {
            $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
            try {
                if (-not $pe.HasMetadata) { continue }
                $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
                $definition = $reader.GetAssemblyDefinition()
                $owner = $reader.GetString($definition.Name)
                [void]$checked.Add($owner)
                $friends = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                foreach ($handle in $definition.GetCustomAttributes()) {
                    $attribute = $reader.GetCustomAttribute($handle)
                    if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
                    $constructor = $reader.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
                    if ($constructor.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
                    $type = $reader.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$constructor.Parent)
                    if ($reader.GetString($type.Name) -ne 'InternalsVisibleToAttribute' -or
                        $reader.GetString($type.Namespace) -ne 'System.Runtime.CompilerServices') { continue }
                    $blob = $reader.GetBlobReader($attribute.Value)
                    if ($blob.ReadUInt16() -ne 1) { throw "Invalid friend attribute on '$owner'." }
                    $friend = ($blob.ReadSerializedString() -split ',')[0]
                    if ($packageAssemblies.Contains($friend)) { [void]$friends.Add($friend) }
                }
                $packageFriendGrants[$owner] = @($friends | Sort-Object)
                if ($restrictedAssemblies.Contains($owner) -and $friends.Count -and
                    $owner -ne 'BEngine.Core.Editor') {
                    $violations.Add("$($file.FullName): $owner grants a feature package access to engine internals.")
                }
                foreach ($handle in $reader.AssemblyReferences) {
                    $reference = $reader.GetAssemblyReference($handle)
                    $name = $reader.GetString($reference.Name)
                    if ($packageAssemblies.Contains($name)) {
                        $violations.Add("$($file.FullName): $owner has package AssemblyRef '$name'.")
                    }
                }
            } finally { $pe.Dispose() }
        } finally { $stream.Dispose() }
    }
}
if ($AssemblyDirectories.Count -gt 0 -and $checked.Count -eq 0) { throw 'No engine assemblies found in the supplied directories.' }
$result = [ordered]@{
    sourceFiles = $sourceFiles
    packageAssemblies = $packageAssemblies.Count
    checkedAssemblies = @($checked | Sort-Object)
    restrictedAssemblies = @($restrictedAssemblies | Sort-Object)
    workbenchPackageConsumers = @($workbenchConsumers)
    frameworkPackageConsumers = @($frameworkConsumers)
    platformBuildConsumers = @($platformBuildConsumers)
    packageFriendGrants = $packageFriendGrants
    violations = @($violations | Sort-Object -Unique)
}
if ($OutputPath) {
    $full = [IO.Path]::GetFullPath($OutputPath)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full))
    [IO.File]::WriteAllText($full, ($result | ConvertTo-Json -Depth 5))
}
if ($violations.Count) { throw ($result.violations -join "`n") }
Write-Output "PACKAGE_BOUNDARIES_OK|sourceFiles=$sourceFiles|packageAssemblies=$($packageAssemblies.Count)|engineAssemblies=$($checked.Count)"
