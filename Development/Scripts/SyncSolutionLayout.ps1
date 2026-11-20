[CmdletBinding()]
param(
    [string] $SolutionPath,
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SolutionPath)) {
    $SolutionPath = Join-Path $PSScriptRoot '../../src/BEngine.sln'
}
$solutionPath = [IO.Path]::GetFullPath($SolutionPath)
$sourceRoot = Split-Path -Parent $solutionPath
$original = [IO.File]::ReadAllText($solutionPath)
$folderType = '{2150E333-8FDC-42A3-9474-1A3956D46DE8}'
$csharpType = '{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}'
$ignoredDirectories = @('.git', '.vs', 'bin', 'obj', 'Temp', 'Library', 'Cache', 'BuildRuntime',
    'node_modules', 'Tests', 'Validation', 'ValidationAssets', 'Verification')

function Get-ProjectBlocks([string] $content) {
    $pattern = '(?ms)^Project\("(?<type>[^"]+)"\) = "(?<name>[^"]+)", "(?<path>[^"]+)", "(?<guid>\{[^}]+\})"\r?\n.*?^EndProject\r?\n'
    foreach ($match in [regex]::Matches($content, $pattern)) {
        [pscustomobject]@{
            Type = $match.Groups['type'].Value
            Name = $match.Groups['name'].Value
            Path = $match.Groups['path'].Value
            Guid = $match.Groups['guid'].Value
        }
    }
}

function Get-SolutionPath([string] $path) {
    if ([IO.Path]::GetFullPath($path).TrimEnd('\', '/').Equals(
        $sourceRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) { return '.' }
    if ($null -ne [IO.Path].GetMethod('GetRelativePath', [Type[]] @([string], [string]))) {
        return [IO.Path]::GetRelativePath($sourceRoot, $path).Replace('/', '\')
    }
    $rootUri = [Uri]::new($sourceRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri([Uri]::new($path)).ToString()).Replace('/', '\')
}

function Get-StableGuid([string] $kind, [string] $path) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $hash = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes("BEngine.Solution$kind/$path")) }
    finally { $hasher.Dispose() }
    $bytes = [byte[]]::new(16)
    [Array]::Copy($hash, $bytes, 16)
    return '{' + ([Guid]::new($bytes)).ToString().ToUpperInvariant() + '}'
}

function Get-OrdinalEntries([object[]] $entries, [string] $property) {
    # Windows PowerShell uses NLS; PowerShell 7 uses ICU. Culture-sensitive
    # sorting orders punctuation differently and would rewrite the solution.
    $sorted = [Collections.Generic.SortedDictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) { $sorted.Add([string] $entry.$property, $entry) }
    return $sorted.Values
}

function Get-SourceFiles([string] $directory) {
    Get-ChildItem -LiteralPath $directory -File -Force |
        Where-Object { $_.Extension -ne '.tmp' -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) }
    foreach ($child in Get-ChildItem -LiteralPath $directory -Directory -Force) {
        if ($ignoredDirectories -contains $child.Name -or
            ($child.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
        Get-SourceFiles $child.FullName
    }
}

# Read only the nesting section: dependency sections also contain GUID = GUID.
$oldProjects = @(Get-ProjectBlocks $original)
$oldByPath = @{}
$oldByGuid = @{}
foreach ($project in $oldProjects) {
    if ($oldByGuid.ContainsKey($project.Guid)) { throw "Duplicate solution GUID: $($project.Guid)" }
    $oldByGuid[$project.Guid] = $project
    if ($project.Type -ne $folderType) {
        if (-not $project.Path.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsupported solution project type: $($project.Path)"
        }
        if ($oldByPath.ContainsKey($project.Path)) { throw "Duplicate project path: $($project.Path)" }
        $oldByPath[$project.Path] = $project
    }
}
$oldNested = @{}
$nesting = [regex]::Match($original, '(?ms)^\tGlobalSection\(NestedProjects\) = preSolution\r?\n(?<body>.*?)^\tEndGlobalSection')
foreach ($match in [regex]::Matches($nesting.Groups['body'].Value, '(?m)^\s*(\{[0-9A-F-]+\}) = (\{[0-9A-F-]+\})\s*$')) {
    $oldNested[$match.Groups[1].Value] = $match.Groups[2].Value
}
$oldFolders = @{}
foreach ($folder in $oldProjects | Where-Object Type -eq $folderType) {
    $names = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $current = $folder
    while ($null -ne $current) {
        if (-not $seen.Add($current.Guid)) { throw "Cyclic solution folder: $($folder.Name)" }
        $names.Insert(0, $current.Name)
        $current = if ($oldNested.ContainsKey($current.Guid)) { $oldByGuid[$oldNested[$current.Guid]] } else { $null }
    }
    $path = $names -join '\'
    if (-not $oldFolders.ContainsKey($path)) { $oldFolders[$path] = $folder.Guid }
}

$files = @(Get-SourceFiles $sourceRoot)
$projects = [Collections.Generic.List[object]]::new()
$projectsByDirectory = @{}
foreach ($file in Get-OrdinalEntries @($files | Where-Object Extension -eq '.csproj') 'FullName') {
    $path = Get-SolutionPath $file.FullName
    $text = [IO.File]::ReadAllText($file.FullName)
    $xml = [xml] $text
    if ($xml.Project.PropertyGroup.IsTestProject -contains 'true') {
        throw "Validation belongs in Temp and a separate solution: $path"
    }
    $roles = @($xml.SelectNodes('//BEngineSolutionRole'))
    if ($roles.Count -gt 1) { throw "Declare BEngineSolutionRole once, unconditionally: $path" }
    $role = if ($roles.Count -eq 0) { 'Default' } else { $roles[0].InnerText.Trim() }
    if ($role -notin 'Default', 'Template', 'Publish', 'Tool') { throw "Unknown BEngineSolutionRole '$role': $path" }
    if ($roles.Count -gt 0 -and ($roles[0].HasAttribute('Condition') -or $roles[0].ParentNode.HasAttribute('Condition') -or
        $roles[0].ParentNode.Name -ne 'PropertyGroup' -or $roles[0].ParentNode.ParentNode.Name -ne 'Project')) {
        throw "BEngineSolutionRole must be unconditional: $path"
    }
    if ($role -eq 'Template') { continue }
    $name = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    $guid = if ($oldByPath.ContainsKey($path)) { $oldByPath[$path].Guid } else { Get-StableGuid 'Project' $path }
    $project = [pscustomobject]@{
        Name = $name; Path = $path; Guid = $guid; Role = $role
        Directory = $file.DirectoryName
        Parent = Get-SolutionPath $file.Directory.Parent.FullName
    }
    $projects.Add($project)
    $projectsByDirectory[$file.DirectoryName] = $true
}
if ($projects.Count -eq 0) { throw 'No source projects were found.' }
if (@($projects | Group-Object Name | Where-Object Count -gt 1).Count -gt 0) {
    throw 'Solution project names must be unique. Mark alternate builds with BEngineSolutionRole=Template.'
}

$folders = @{}
function Ensure-Folder([string] $path) {
    if ($path -eq '.') { $path = 'Solution Items' }
    if ($folders.ContainsKey($path)) { return }
    $parent = Split-Path -Parent $path
    if (-not [string]::IsNullOrEmpty($parent)) { Ensure-Folder $parent }
    $folders[$path] = [pscustomobject]@{
        Name = Split-Path -Leaf $path
        Path = $path
        Parent = $parent
        Guid = if ($oldFolders.ContainsKey($path)) { $oldFolders[$path] } else { Get-StableGuid 'Folder' $path }
        Items = [Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    }
}
function Add-SolutionItem([string] $parent, [string] $path) {
    Ensure-Folder $parent
    if ($parent -eq '.') { $parent = 'Solution Items' }
    [void] $folders[$parent].Items.Add($path)
}

foreach ($project in $projects) { Ensure-Folder $project.Parent }
foreach ($file in Get-OrdinalEntries $files 'FullName') {
    if ($file.FullName -eq $solutionPath -or $file.Extension -in '.sln', '.slnx', '.slnf', '.suo', '.user') { continue }
    # Loaded project nodes already expose their code. Alternate project files
    # remain visible beside the owning project without duplicating its sources.
    $owner = $file.Directory
    while ($null -ne $owner -and $owner.FullName -ne $sourceRoot -and
        -not $projectsByDirectory.ContainsKey($owner.FullName)) { $owner = $owner.Parent }
    if ($null -ne $owner -and $projectsByDirectory.ContainsKey($owner.FullName)) {
        if ($file.Extension -eq '.csproj' -and -not ($projects.Path -contains (Get-SolutionPath $file.FullName))) {
            Add-SolutionItem (Get-SolutionPath $owner.Parent.FullName) (Get-SolutionPath $file.FullName)
        }
        continue
    }
    Add-SolutionItem (Get-SolutionPath $file.DirectoryName) (Get-SolutionPath $file.FullName)
}

$builder = [Text.StringBuilder]::new()
$firstProject = [regex]::Match($original, '(?m)^Project\(')
if (-not $firstProject.Success) { throw 'No Visual Studio solution header was found.' }
[void] $builder.Append($original.Substring(0, $firstProject.Index).Replace("`r`n", "`n").Replace("`n", "`r`n"))
foreach ($folder in Get-OrdinalEntries @($folders.Values) 'Path') {
    [void] $builder.Append("Project(`"$folderType`") = `"$($folder.Name)`", `"$($folder.Path)`", `"$($folder.Guid)`"`r`n")
    if ($folder.Items.Count -gt 0) {
        [void] $builder.Append("`tProjectSection(SolutionItems) = preProject`r`n")
        foreach ($item in $folder.Items) { [void] $builder.Append("`t`t$item = $item`r`n") }
        [void] $builder.Append("`tEndProjectSection`r`n")
    }
    [void] $builder.Append("EndProject`r`n")
}
foreach ($project in $projects) {
    $type = if ($oldByPath.ContainsKey($project.Path)) { $oldByPath[$project.Path].Type } else { $csharpType }
    [void] $builder.Append("Project(`"$type`") = `"$($project.Name)`", `"$($project.Path)`", `"$($project.Guid)`"`r`n")
    [void] $builder.Append("EndProject`r`n")
}

$globalMatch = [regex]::Match($original, '(?ms)^Global\r?\n.*?^EndGlobal\r?\n?')
if (-not $globalMatch.Success) { throw 'The solution Global section was not found.' }
$global = $globalMatch.Value.Replace("`r`n", "`n").Replace("`n", "`r`n")
function Set-GlobalSection([string] $name, [string] $placement, [string[]] $lines) {
    $pattern = '(?ms)^\tGlobalSection\(' + [regex]::Escape($name) + '\) = \w+\r?\n.*?^\tEndGlobalSection\r?\n'
    $section = "`tGlobalSection($name) = $placement`r`n" + ($lines -join "`r`n") + "`r`n`tEndGlobalSection`r`n"
    if ([regex]::IsMatch($script:global, $pattern)) {
        $script:global = [regex]::Replace($script:global, $pattern, [Text.RegularExpressions.MatchEvaluator] { param($match) $section })
    }
    else {
        $script:global = [regex]::Replace($script:global, '(?m)^EndGlobal\r?$',
            [Text.RegularExpressions.MatchEvaluator] { param($match) $section + 'EndGlobal' })
    }
}
$configurationSection = [regex]::Match($global, '(?ms)^\tGlobalSection\(SolutionConfigurationPlatforms\) = preSolution\r?\n(?<body>.*?)^\tEndGlobalSection')
$configurations = @([regex]::Matches($configurationSection.Groups['body'].Value, '(?m)^\s*([^=\r\n]+?)\s*=') |
    ForEach-Object { $_.Groups[1].Value.Trim() })
if ($configurations.Count -eq 0) { throw 'No solution configurations were found.' }
$oldConfigurationSection = [regex]::Match($global, '(?ms)^\tGlobalSection\(ProjectConfigurationPlatforms\) = postSolution\r?\n(?<body>.*?)^\tEndGlobalSection')
$oldActiveConfigurations = @{}
foreach ($match in [regex]::Matches($oldConfigurationSection.Groups['body'].Value, '(?m)^\s*(\{[^}]+\}\.[^=]+\.ActiveCfg) = ([^\r\n]+)')) {
    $oldActiveConfigurations[$match.Groups[1].Value] = $match.Groups[2].Value
}
$configurationLines = [Collections.Generic.List[string]]::new()
foreach ($project in $projects) {
    foreach ($configuration in $configurations) {
        $key = "$($project.Guid).$configuration.ActiveCfg"
        $active = if ($oldActiveConfigurations.ContainsKey($key)) { $oldActiveConfigurations[$key] } else { ($configuration -split '\|')[0] + '|Any CPU' }
        $configurationLines.Add("`t`t$key = $active")
        if ($project.Role -ne 'Publish') { $configurationLines.Add("`t`t$($project.Guid).$configuration.Build.0 = $active") }
    }
}
Set-GlobalSection 'ProjectConfigurationPlatforms' 'postSolution' $configurationLines.ToArray()
$nestedLines = [Collections.Generic.List[string]]::new()
foreach ($item in @($folders.Values) + @($projects)) {
    if ([string]::IsNullOrEmpty($item.Parent)) { continue }
    $nestedLines.Add("`t`t$($item.Guid) = $($folders[$item.Parent].Guid)")
}
$sortedNestedLines = $nestedLines.ToArray()
[Array]::Sort($sortedNestedLines, [StringComparer]::OrdinalIgnoreCase)
Set-GlobalSection 'NestedProjects' 'preSolution' $sortedNestedLines
[void] $builder.Append($global.TrimEnd("`r", "`n") + "`r`n")
$updated = $builder.ToString()
if ($updated -eq $original) {
    Write-Output "BEngine.sln is synchronized ($($projects.Count) projects)."
    return
}
if ($Check) { throw 'BEngine.sln is out of sync. Run Engine/Development/Scripts/SyncSolutionLayout.ps1.' }
[IO.File]::WriteAllText($solutionPath, $updated, [Text.UTF8Encoding]::new($true))
Write-Output "BEngine.sln synchronized: $($projects.Count) projects, $($folders.Count) folders."
