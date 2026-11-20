param([Parameter(Mandatory = $true)][string]$DefinitionPath)
$text = [IO.File]::ReadAllText($DefinitionPath)
function Read-VersionField([string]$Name) {
    $match = [regex]::Match($text, ('(?m)^' + $Name + ':\s*([^\r\n#]+)'))
    $value = $match.Groups[1].Value.Trim().Trim([char]34, [char]39)
    $semantic = [regex]::Match($value, '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$')
    $numeric = $null
    if (-not $match.Success -or -not $semantic.Success -or
        -not [Version]::TryParse(($semantic.Groups[1].Value + '.' + $semantic.Groups[2].Value + '.' + $semantic.Groups[3].Value), [ref]$numeric) -or
        @($semantic.Groups[4].Value.Split('.') | Where-Object { $_ -match '^0\d+$' }).Count -gt 0) {
        throw "Invalid or missing $Name in $DefinitionPath"
    }
    return $value
}
function Compare-ReleaseVersion([string]$Left, [string]$Right) {
    $a = $Left.Split('+')[0].Split('-', 2)
    $b = $Right.Split('+')[0].Split('-', 2)
    $result = ([Version]$a[0]).CompareTo([Version]$b[0])
    if ($result -ne 0) { return $result }
    if ($a.Length -eq 1 -or $b.Length -eq 1) { return ([int]($a.Length -eq 1)).CompareTo([int]($b.Length -eq 1)) }
    $aa = $a[1].Split('.'); $bb = $b[1].Split('.')
    for ($i = 0; $i -lt [Math]::Min($aa.Length, $bb.Length); $i++) {
        $an = $aa[$i] -match '^\d+$'; $bn = $bb[$i] -match '^\d+$'
        if ($an -and $bn) { $result = $aa[$i].Length.CompareTo($bb[$i].Length) }
        elseif ($an -ne $bn) { $result = $(if ($an) { -1 } else { 1 }) }
        else { $result = 0 }
        if ($result -eq 0) { $result = [StringComparer]::Ordinal.Compare($aa[$i], $bb[$i]) }
        if ($result -ne 0) { return $result }
    }
    return $aa.Length.CompareTo($bb.Length)
}
$release = [pscustomobject]@{
    Version = Read-VersionField 'packageVersion'
    MinimumEngineVersion = Read-VersionField 'minimumEngineVersion'
}
$current = & (Join-Path $PSScriptRoot 'GetEngineVersion.ps1')
if ((Compare-ReleaseVersion $current $release.MinimumEngineVersion) -lt 0) {
    throw "Build engine $current is older than minimumEngineVersion $($release.MinimumEngineVersion) in $DefinitionPath"
}
$release
