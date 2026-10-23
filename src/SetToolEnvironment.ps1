[CmdletBinding()]
param([string]$InstallationRoot = (Join-Path $PSScriptRoot '..\Engine'))

# Source export shares the installed SDK and native NuGet settings/cache.
# Only tool state, shared caches and disposable files belong to this Hub.
$toolRoot = [IO.Path]::GetFullPath($InstallationRoot)
$toolTemp = Join-Path $toolRoot 'Temp'
$toolCache = Join-Path $toolRoot 'Cache'
$toolDirectories = @{
    BENGINE_HUB_DIRECTORY = $toolRoot
    BENGINE_TOOL_TEMP_ROOT = $toolTemp
    TEMP = (Join-Path $toolTemp 'System')
    TMP = (Join-Path $toolTemp 'System')
    TMPDIR = (Join-Path $toolTemp 'System')
    DOTNET_BUNDLE_EXTRACT_BASE_DIR = (Join-Path $toolCache 'DotNet\Bundles')
    XDG_CACHE_HOME = (Join-Path $toolCache 'Tools\Cache')
    XDG_CONFIG_HOME = (Join-Path $toolRoot 'Preferences\Tools\Config')
    XDG_DATA_HOME = (Join-Path $toolRoot 'Preferences\Tools\Data')
    CODEX_HOME = (Join-Path $toolRoot 'Preferences\Codex')
    ANDROID_USER_HOME = (Join-Path $toolRoot 'Preferences\Tools\Android')
    ANDROID_EMULATOR_HOME = (Join-Path $toolCache 'Android\Emulator')
    ANDROID_AVD_HOME = (Join-Path $toolRoot 'Preferences\Tools\Android\Avd')
    GRADLE_USER_HOME = (Join-Path $toolCache 'Gradle')
    EM_CACHE = (Join-Path $toolCache 'Emscripten\Cache')
}

# Remove only overrides emitted by earlier BEngine versions. User-selected
# NUGET_* paths and custom profile locations retain their native meaning.
$legacyNuGetOverrides = @{
    NUGET_PACKAGES = 'Temp\NuGet\Packages'
    NUGET_HTTP_CACHE_PATH = 'Temp\NuGet\Http'
    NUGET_SCRATCH = 'Temp\NuGet\Scratch'
    NUGET_PLUGINS_CACHE_PATH = 'Temp\NuGet\Plugins'
    DOTNET_CLI_HOME = @('Temp\DotNet\Home', 'Cache\DotNet\Home')
    APPDATA = 'Preferences\Tools\Roaming'
    LOCALAPPDATA = 'Temp\Tools\Local'
}
$toolEnvironmentNames = @($toolDirectories.Keys) + @($legacyNuGetOverrides.Keys) +
    @('BENGINE_TOOL_PROJECT_ROOT', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE')
foreach ($name in $legacyNuGetOverrides.Keys) {
    $value = [Environment]::GetEnvironmentVariable($name, 'Process')
    foreach ($relative in $legacyNuGetOverrides[$name]) {
      $legacy = [IO.Path]::GetFullPath((Join-Path $toolRoot $relative))
      if ($value -and [IO.Path]::IsPathFullyQualified($value) -and
          [string]::Equals([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($value)),
              $legacy, [StringComparison]::OrdinalIgnoreCase)) {
        $replacement = switch ($name) {
            'APPDATA' { [Environment]::GetFolderPath('ApplicationData') }
            'LOCALAPPDATA' { [Environment]::GetFolderPath('LocalApplicationData') }
            default { [NullString]::Value }
        }
        [Environment]::SetEnvironmentVariable($name, $replacement, 'Process')
      }
    }
}
[Environment]::SetEnvironmentVariable('BENGINE_TOOL_PROJECT_ROOT', [NullString]::Value, 'Process')

function Test-ToolCacheContent([string]$Directory) {
    foreach ($child in [IO.Directory]::GetFileSystemEntries($Directory)) {
        if (([IO.File]::GetAttributes($child) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
        if (-not [IO.Directory]::Exists($child) -or (Test-ToolCacheContent $child)) { return $true }
    }
    return $false
}
function Move-ToolCache([string]$Source, [string]$Destination) {
    if (-not [IO.Directory]::Exists($Source)) { return }
    try {
        if (([IO.File]::GetAttributes($Source) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return }
        if (-not (Test-ToolCacheContent $Source)) { return }
        if (-not [IO.Directory]::Exists($Destination)) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination)) | Out-Null
            [IO.Directory]::Move($Source, $Destination)
            return
        }
        if (([IO.File]::GetAttributes($Destination) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return }
        foreach ($child in [IO.Directory]::GetFileSystemEntries($Source)) {
            if (([IO.File]::GetAttributes($child) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            $target = Join-Path $Destination ([IO.Path]::GetFileName($child))
            if ([IO.Directory]::Exists($child)) { Move-ToolCache $child $target }
            elseif (-not [IO.File]::Exists($target) -and -not [IO.Directory]::Exists($target)) {
                [IO.File]::Move($child, $target)
            }
        }
    }
    catch [IO.IOException] { }
    catch [UnauthorizedAccessException] { }
}
foreach ($name in @('DotNet', 'Tools', 'Android', 'Gradle', 'Emscripten')) {
    Move-ToolCache (Join-Path $toolTemp $name) (Join-Path $toolCache $name)
}
# Path.GetTempFileName needs its active directory; unused SDK/platform cache
# locations are deliberately left for their owning tools to create on demand.
[IO.Directory]::CreateDirectory((Join-Path $toolTemp 'System')) | Out-Null
foreach ($entry in $toolDirectories.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
}
[Environment]::SetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT', '1', 'Process')
[Environment]::SetEnvironmentVariable('DOTNET_GENERATE_ASPNET_CERTIFICATE', 'false', 'Process')
