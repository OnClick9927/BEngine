[CmdletBinding()]
param(
    [string] $SolutionPath,
    [switch] $Check
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SolutionPath)) {
    $SolutionPath = Join-Path $PSScriptRoot 'BEngine.sln'
}

function Get-ProjectBlocks([string] $content) {
    $pattern = '(?ms)^Project\("(?<type>[^\"]+)"\) = "(?<name>[^\"]+)", "(?<path>[^\"]+)", "(?<guid>\{[^}]+\})"\r?\n.*?^EndProject\r?\n'
    foreach ($match in [regex]::Matches($content, $pattern)) {
        [pscustomobject]@{
            Type = $match.Groups['type'].Value
            Name = $match.Groups['name'].Value
            Path = $match.Groups['path'].Value
            Guid = $match.Groups['guid'].Value
            Text = $match.Value
        }
    }
}

function Set-SolutionItems([string] $content, $project, [string[]] $items) {
    $sectionPattern = '(?ms)^\tProjectSection\(SolutionItems\) = preProject\r?\n.*?^\tEndProjectSection\r?\n'
    $section = ''
    if ($items.Count -gt 0) {
        $lines = $items | Sort-Object -Unique | ForEach-Object { "`t`t$_ = $_" }
        $section = "`tProjectSection(SolutionItems) = preProject`r`n" +
            ($lines -join "`r`n") + "`r`n`tEndProjectSection`r`n"
    }

    $updated = [regex]::Replace($project.Text, $sectionPattern, '')
    if ($section.Length -gt 0) {
        $updated = [regex]::Replace($updated, '(?m)^EndProject\r?$', $section + 'EndProject')
    }
    return $content.Replace($project.Text, $updated)
}

function Get-SolutionPath([string] $sourceRoot, [string] $path) {
    $relativePathMethod = [IO.Path].GetMethod(
        'GetRelativePath',
        [Type[]] @([string], [string]))
    if ($null -ne $relativePathMethod) {
        return [IO.Path]::GetRelativePath($sourceRoot, $path).Replace('/', '\')
    }

    $rootUri = [Uri]::new($sourceRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar)
    $pathUri = [Uri]::new($path)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Get-DeterministicSolutionGuid([string] $path) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes("BEngine.SolutionFolder/$path"))
    }
    finally {
        $hasher.Dispose()
    }
    $bytes = [byte[]]::new(16)
    [Array]::Copy($hash, $bytes, 16)
    return '{' + ([Guid]::new($bytes)).ToString().ToUpperInvariant() + '}'
}

function Get-DirectFiles([string] $sourceRoot, [string] $directory) {
    return @(
        Get-ChildItem -LiteralPath $directory -File |
            ForEach-Object { Get-SolutionPath $sourceRoot $_.FullName }
    )
}

function New-SolutionFolderBlock(
    [string] $solutionFolderType,
    [string] $name,
    [string] $path,
    [string] $guid,
    [string[]] $items) {
    $block = "Project(`"$solutionFolderType`") = `"$name`", `"$path`", `"$guid`"`r`n"
    if ($items.Count -gt 0) {
        $lines = $items | Sort-Object -Unique | ForEach-Object { "`t`t$_ = $_" }
        $block += "`tProjectSection(SolutionItems) = preProject`r`n" +
            ($lines -join "`r`n") + "`r`n`tEndProjectSection`r`n"
    }
    return $block + "EndProject`r`n"
}

$solutionPath = [IO.Path]::GetFullPath($SolutionPath)
$sourceRoot = Split-Path -Parent $solutionPath
$original = [IO.File]::ReadAllText($solutionPath)
$projects = @(Get-ProjectBlocks $original)
$nested = @{}
foreach ($match in [regex]::Matches($original,
    '(?m)^\s*(?<child>\{[0-9A-F-]+\}) = (?<parent>\{[0-9A-F-]+\})\s*$')) {
    $nested[$match.Groups['child'].Value] = $match.Groups['parent'].Value
}

$solutionFolderType = '{2150E333-8FDC-42A3-9474-1A3956D46DE8}'
$missingProjectPaths = @($projects | Where-Object {
    $_.Type -ne $solutionFolderType -and
    -not (Test-Path -LiteralPath (Join-Path $sourceRoot $_.Path) -PathType Leaf)
} | ForEach-Object { $_.Path })
if ($missingProjectPaths.Count -gt 0) {
    throw "BEngine.sln contains missing project paths: $($missingProjectPaths -join ', ')"
}

$hubFolders = @($projects | Where-Object {
    $_.Type -eq $solutionFolderType -and $_.Name -eq 'Hub' -and $_.Path -eq 'Hub' -and
    !$nested.ContainsKey($_.Guid)
})
if ($hubFolders.Count -ne 1) {
    throw "Expected one root solution folder 'Hub' in '$solutionPath', found $($hubFolders.Count)."
}
$hubGuid = $hubFolders[0].Guid
$hubProjectsOnDisk = @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Hub') -Filter '*.csproj' -File -Recurse |
    ForEach-Object { Get-SolutionPath $sourceRoot $_.FullName })
$hubProjectsInSolution = @($projects | Where-Object {
    $_.Type -ne $solutionFolderType -and $_.Path.StartsWith('Hub\', [StringComparison]::OrdinalIgnoreCase)
})
$hubProjectSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($path in $hubProjectsOnDisk) { [void] $hubProjectSet.Add($path) }
$hubSolutionProjectSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $hubProjectsInSolution) { [void] $hubSolutionProjectSet.Add($project.Path) }
if (-not $hubProjectSet.SetEquals($hubSolutionProjectSet)) {
    throw "BEngine.sln Hub projects do not match src/Hub. Update the solution project entries."
}
foreach ($project in $hubProjectsInSolution) {
    if (!$nested.ContainsKey($project.Guid) -or $nested[$project.Guid] -ne $hubGuid) {
        throw "Hub project '$($project.Path)' is not nested under the Hub solution folder."
    }
}
if ($projects.Path -contains 'Core\BEngine.Launcher\BEngine.Launcher.csproj') {
    throw 'BEngine.Launcher must live under src/Hub, not src/Core.'
}

$packageLocations = [ordered]@{
    Core = 'Core'
    Animation = 'Packages\Animation'
    Navigation2D = 'Packages\Navigation2D'
    Physics2D = 'Packages\Physics2D'
    PropertyAttributes = 'Packages\PropertyAttributes'
    TiledMap = 'Packages\TiledMap'
    UIElements = 'Packages\UIElements'
}
$packages = @($packageLocations.Keys)
$obsoletePackages = @()
$updatedContent = $original
$packageContainerProjects = @($projects | Where-Object {
    $_.Type -eq $solutionFolderType -and $_.Name -eq 'Packages' -and $_.Path -eq 'Packages' -and
    !$nested.ContainsKey($_.Guid)
})
if ($packageContainerProjects.Count -ne 1) {
    throw "Expected one root solution folder 'Packages' in '$solutionPath', found $($packageContainerProjects.Count)."
}
$packageContainerGuid = $packageContainerProjects[0].Guid

function Test-PackageSolutionFolder($project, [string] $package) {
    if ($project.Type -ne $solutionFolderType -or $project.Name -ne $package -or $project.Path -ne $package) {
        return $false
    }
    if ($package -eq 'Core') { return !$nested.ContainsKey($project.Guid) }
    return $nested.ContainsKey($project.Guid) -and $nested[$project.Guid] -eq $packageContainerGuid
}

$packageRootGuids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $projects | Where-Object {
             ($packages + $obsoletePackages) -contains $_.Name -and
             (Test-PackageSolutionFolder $_ $_.Name)
         }) {
    [void] $packageRootGuids.Add($project.Guid)
}
$resourceRootGuids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$legacyResourceRootGuids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $projects | Where-Object {
             $_.Type -eq $solutionFolderType -and
             $_.Name -in 'Resources', 'Editor', 'EditorResources' -and
             $nested.ContainsKey($_.Guid) -and $packageRootGuids.Contains($nested[$_.Guid])
         }) {
    [void] $resourceRootGuids.Add($project.Guid)
    if ($project.Name -eq 'EditorResources') {
        [void] $legacyResourceRootGuids.Add($project.Guid)
    }
}
$generatedGuids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
do {
    $changed = $false
    foreach ($project in $projects) {
        if ($generatedGuids.Contains($project.Guid) -or !$nested.ContainsKey($project.Guid)) { continue }
        $parentGuid = $nested[$project.Guid]
        if ($resourceRootGuids.Contains($parentGuid) -or $generatedGuids.Contains($parentGuid)) {
            [void] $generatedGuids.Add($project.Guid)
            $changed = $true
        }
    }
} while ($changed)
$oldGeneratedProjects = @($projects | Where-Object {
    $generatedGuids.Contains($_.Guid) -or $legacyResourceRootGuids.Contains($_.Guid)
})
$obsoleteGuids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $projects | Where-Object {
             $_.Type -eq $solutionFolderType -and $obsoletePackages -contains $_.Path
         }) {
    [void] $obsoleteGuids.Add($project.Guid)
}
do {
    $changed = $false
    foreach ($project in $projects) {
        if ($obsoleteGuids.Contains($project.Guid)) { continue }
        if ($nested.ContainsKey($project.Guid) -and $obsoleteGuids.Contains($nested[$project.Guid])) {
            [void] $obsoleteGuids.Add($project.Guid)
            $changed = $true
        }
    }
} while ($changed)
$obsoleteProjects = @($projects | Where-Object { $obsoleteGuids.Contains($_.Guid) })
$oldGeneratedProjects += $obsoleteProjects
$oldGeneratedGuids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $oldGeneratedProjects) {
    [void] $oldGeneratedGuids.Add($project.Guid)
    $updatedContent = $updatedContent.Replace($project.Text, '')
}
$newProjectBlocks = [Collections.Generic.List[string]]::new()
$newMappings = [Collections.Generic.List[string]]::new()

foreach ($package in $packages) {
    $packageLocation = $packageLocations[$package]
    $packageProjects = @($projects | Where-Object {
        Test-PackageSolutionFolder $_ $package
    })
    if ($packageProjects.Count -ne 1) {
        throw "Expected one solution folder '$package' in '$solutionPath', found $($packageProjects.Count)."
    }
    $packageProject = $packageProjects[0]

    $packageItems = @()
    $packageDefinition = Join-Path (Join-Path $sourceRoot $packageLocation) 'package.yaml'
    if (Test-Path -LiteralPath $packageDefinition -PathType Leaf) {
        $packageItems += "$packageLocation\package.yaml"
    }
    $updatedContent = Set-SolutionItems $updatedContent $packageProject $packageItems

    foreach ($folderName in 'Resources', 'Editor') {
        $folderProjects = @($projects | Where-Object {
            $_.Type -eq $solutionFolderType -and $_.Name -eq $folderName -and
            $nested[$_.Guid] -eq $packageProject.Guid
        })
        if ($folderProjects.Count -gt 1) {
            throw "Expected one solution folder '$package/$folderName' in '$solutionPath', found $($folderProjects.Count)."
        }

        $physicalFolder = Join-Path (Join-Path $sourceRoot $packageLocation) $folderName
        $folderItems = @(Get-DirectFiles $sourceRoot $physicalFolder)
        if ($folderProjects.Count -eq 0) {
            $folderGuid = Get-DeterministicSolutionGuid "$package\$folderName"
            $newProjectBlocks.Add((New-SolutionFolderBlock -solutionFolderType $solutionFolderType `
                -name $folderName -path $folderName -guid $folderGuid -items $folderItems))
            $newMappings.Add("`t`t$folderGuid = $($packageProject.Guid)")
            $folderProject = [pscustomobject]@{ Guid = $folderGuid }
        }
        else {
            $folderProject = $folderProjects[0]
            $updatedContent = Set-SolutionItems $updatedContent $folderProject $folderItems
        }

        $directoryGuids = @{}
        $directoryGuids[[IO.Path]::GetFullPath($physicalFolder)] = $folderProject.Guid
        $directories = @(Get-ChildItem -LiteralPath $physicalFolder -Directory -Recurse |
            Sort-Object FullName)
        foreach ($directory in $directories) {
            $directoryPath = [IO.Path]::GetFullPath($directory.FullName)
            $solutionDirectoryPath = Get-SolutionPath $sourceRoot $directoryPath
            $guid = Get-DeterministicSolutionGuid $solutionDirectoryPath
            $parentPath = [IO.Path]::GetFullPath($directory.Parent.FullName)
            $parentGuid = $directoryGuids[$parentPath]
            if ([string]::IsNullOrWhiteSpace($parentGuid)) {
                throw "No solution parent was generated for '$solutionDirectoryPath'."
            }

            $items = @(Get-DirectFiles $sourceRoot $directoryPath)
            $block = New-SolutionFolderBlock -solutionFolderType $solutionFolderType `
                -name $directory.Name -path $solutionDirectoryPath -guid $guid -items $items
            $newProjectBlocks.Add($block)
            $newMappings.Add("`t`t$guid = $parentGuid")
            $directoryGuids[$directoryPath] = $guid
        }
    }
}

if ($newProjectBlocks.Count -gt 0) {
    $projectsText = ($newProjectBlocks -join '')
    $updatedContent = ([regex]::new('(?m)^Global\r?$')).Replace(
        $updatedContent, $projectsText + 'Global', 1)
}

$nestedSectionPattern = '(?ms)^\tGlobalSection\(NestedProjects\) = preSolution\r?\n(?<body>.*?)^\tEndGlobalSection\r?\n'
$nestedSection = [regex]::Match($updatedContent, $nestedSectionPattern)
if (!$nestedSection.Success) {
    throw "NestedProjects was not found in '$solutionPath'."
}
$keptMappings = [Collections.Generic.List[string]]::new()
foreach ($line in ($nestedSection.Groups['body'].Value -split '\r?\n')) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $mapping = [regex]::Match($line, '^\s*(?<child>\{[0-9A-F-]+\}) = ')
    if ($mapping.Success -and $oldGeneratedGuids.Contains($mapping.Groups['child'].Value)) { continue }
    $keptMappings.Add($line)
}
foreach ($mapping in $newMappings) { $keptMappings.Add($mapping) }
$updatedNestedSection = "`tGlobalSection(NestedProjects) = preSolution`r`n" +
    (($keptMappings | Sort-Object -Unique) -join "`r`n") +
    "`r`n`tEndGlobalSection`r`n"
$updatedContent = $updatedContent.Replace($nestedSection.Value, $updatedNestedSection)

if ($updatedContent -eq $original) {
    Write-Output 'BEngine.sln already matches the Hub and package folders on disk.'
    return
}

if ($Check) {
    throw 'BEngine.sln is out of sync. Run src/SyncSolutionLayout.ps1.'
}

[IO.File]::WriteAllText($solutionPath, $updatedContent, [Text.UTF8Encoding]::new($true))
Write-Output 'BEngine.sln synchronized with package Resources and Editor.'
