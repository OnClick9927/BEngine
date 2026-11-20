using System.Collections.ObjectModel;
using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>
/// Describes one Source-to-Artifact import. Importers write through this context so the
/// AssetDatabase can publish the completed artifact atomically.
/// </summary>
public sealed class AssetImportContext : IDisposable
{
    private readonly string _destinationArtifactPath;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private bool _committed;

    internal AssetImportContext(
        string assetPath,
        string sourcePath,
        string artifactPath,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);

        _fileSystem = fileSystem;
        _pathService = pathService;
        this.assetPath = assetPath;
        this.sourcePath = _pathService.GetFullPath(sourcePath);
        _destinationArtifactPath = _pathService.GetFullPath(artifactPath);
        this.settings = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(settings, StringComparer.Ordinal));
        this.cancellationToken = cancellationToken;

        var directory = _pathService.GetDirectoryName(_destinationArtifactPath) ??
                        throw new InvalidDataException(
                            $"Artifact path has no directory: {_destinationArtifactPath}");
        _fileSystem.CreateDirectory(directory);
        this.artifactPath = _pathService.Combine(directory,
            $".{_pathService.GetFileName(_destinationArtifactPath)}.{Guid.NewGuid():N}.importing");
    }

    public string assetPath { get; }
    public string sourcePath { get; }
    /// <summary>
    /// Staging path for the importer output. The AssetDatabase atomically publishes this file
    /// to the final Artifact path only after <see cref="AssetImporter.Import"/> succeeds.
    /// </summary>
    public string artifactPath { get; }
    public IReadOnlyDictionary<string, string> settings { get; }
    public CancellationToken cancellationToken { get; }

    public Stream OpenSourceStream()
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _fileSystem.OpenFile(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.SequentialScan);
    }

    public Stream OpenArtifactStream()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_committed) throw new InvalidOperationException("The artifact was already committed.");
        return _fileSystem.OpenFile(artifactPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, FileOptions.SequentialScan | FileOptions.WriteThrough);
    }

    public void CopySourceToArtifact()
    {
        using var source = OpenSourceStream();
        using var artifact = OpenArtifactStream();
        source.CopyTo(artifact);
        _fileSystem.Flush(artifact, flushToDisk: true);
    }

    public void WriteArtifact(ReadOnlySpan<byte> bytes)
    {
        using var artifact = OpenArtifactStream();
        artifact.Write(bytes);
        _fileSystem.Flush(artifact, flushToDisk: true);
    }

    public byte[] ReadSourceBytes()
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _fileSystem.ReadAllBytes(sourcePath);
    }

    internal void Commit()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_fileSystem.FileExists(artifactPath))
            throw new InvalidOperationException(
                $"Importer did not produce an artifact for '{assetPath}'.");

        if (_fileSystem.FileExists(_destinationArtifactPath))
        {
            try
            {
                _fileSystem.ReplaceFile(
                    artifactPath, _destinationArtifactPath, null, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                _fileSystem.MoveFile(artifactPath, _destinationArtifactPath, overwrite: true);
            }
        }
        else
        {
            _fileSystem.MoveFile(artifactPath, _destinationArtifactPath);
        }
        _committed = true;
    }

    public void Dispose()
    {
        if (_committed || !_fileSystem.FileExists(artifactPath)) return;
        try { _fileSystem.DeleteFile(artifactPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
