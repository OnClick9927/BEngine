[CmdletBinding()]
param(
    [string] $SolutionPath = (Join-Path $PSScriptRoot 'BEngine.sln'),
    [switch] $Check
)

$ErrorActionPreference = 'Stop'

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
    return [IO.Path]::GetRelativePath($sourceRoot, $path).Replace('/', '\')
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
$packages = 'Core', 'Animation', 'Navigation2D', 'Physics2D', 'PropertyAttributes', 'TiledMap', 'UIElements'
$obsoletePackages = @()
$updatedContent = $original
$packageRootGuids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $projects | Where-Object {
             $_.Type -eq $solutionFolderType -and !$nested.ContainsKey($_.Guid) -and
             ($packages + $obsoletePackages) -contains $_.Name
         }) {
    [void] $packageRootGuids.Add($project.Guid)
}
$resourceRootGuids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $projects | Where-Object {
             $_.Type -eq $solutionFolderType -and
             $_.Name -in 'Resources', 'EditorResources' -and
             $nested.ContainsKey($_.Guid) -and $packageRootGuids.Contains($nested[$_.Guid])
         }) {
    [void] $resourceRootGuids.Add($project.Guid)
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
$oldGeneratedProjects = @($projects | Where-Object { $generatedGuids.Contains($_.Guid) })
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
    $packageProjects = @($projects | Where-Object {
        $_.Type -eq $solutionFolderType -and $_.Name -eq $package -and $_.Path -eq $package -and
        !$nested.ContainsKey($_.Guid)
    })
    if ($packageProjects.Count -ne 1) {
        throw "Expected one solution folder '$package' in '$solutionPath', found $($packageProjects.Count)."
    }
    $packageProject = $packageProjects[0]

    $packageItems = @()
    $packageDefinition = Join-Path (Join-Path $sourceRoot $package) 'package.yaml'
    if (Test-Path -LiteralPath $packageDefinition -PathType Leaf) {
        $packageItems += "$package\package.yaml"
    }
    $updatedContent = Set-SolutionItems $updatedContent $packageProject $packageItems

    foreach ($folderName in 'Resources', 'EditorResources') {
        $folderProjects = @($projects | Where-Object {
            $_.Type -eq $solutionFolderType -and $_.Name -eq $folderName -and
            $nested[$_.Guid] -eq $packageProject.Guid
        })
        if ($folderProjects.Count -gt 1) {
            throw "Expected one solution folder '$package/$folderName' in '$solutionPath', found $($folderProjects.Count)."
        }

        $physicalFolder = Join-Path (Join-Path $sourceRoot $package) $folderName
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
    Write-Output 'BEngine.sln already matches the package folders on disk.'
    return
}

if ($Check) {
    throw 'BEngine.sln is out of sync. Run src/SyncSolutionLayout.ps1.'
}

[IO.File]::WriteAllText($solutionPath, $updatedContent, [Text.UTF8Encoding]::new($true))
Write-Output 'BEngine.sln synchronized with package Resources and EditorResources.'
