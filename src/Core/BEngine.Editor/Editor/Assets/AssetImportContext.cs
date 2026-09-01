using System.Collections.ObjectModel;

namespace BEngine.Editor;

/// <summary>
/// Describes one Source-to-Artifact import. Importers write through this context so the
/// AssetDatabase can publish the completed artifact atomically.
/// </summary>
public sealed class AssetImportContext : IDisposable
{
    private readonly string _destinationArtifactPath;
    private bool _committed;

    internal AssetImportContext(
        string assetPath,
        string sourcePath,
        string artifactPath,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        ArgumentNullException.ThrowIfNull(settings);

        this.assetPath = assetPath;
        this.sourcePath = Path.GetFullPath(sourcePath);
        _destinationArtifactPath = Path.GetFullPath(artifactPath);
        this.settings = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(settings, StringComparer.Ordinal));
        this.cancellationToken = cancellationToken;

        var directory = Path.GetDirectoryName(_destinationArtifactPath) ??
                        throw new InvalidDataException(
                            $"Artifact path has no directory: {_destinationArtifactPath}");
        Directory.CreateDirectory(directory);
        this.artifactPath = Path.Combine(directory,
            $".{Path.GetFileName(_destinationArtifactPath)}.{Guid.NewGuid():N}.importing");
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

    public FileStream OpenSourceStream()
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.SequentialScan);
    }

    public FileStream OpenArtifactStream()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_committed) throw new InvalidOperationException("The artifact was already committed.");
        return new FileStream(artifactPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, FileOptions.SequentialScan | FileOptions.WriteThrough);
    }

    public void CopySourceToArtifact()
    {
        using var source = OpenSourceStream();
        using var artifact = OpenArtifactStream();
        source.CopyTo(artifact);
        artifact.Flush(flushToDisk: true);
    }

    public void WriteArtifact(ReadOnlySpan<byte> bytes)
    {
        using var artifact = OpenArtifactStream();
        artifact.Write(bytes);
        artifact.Flush(flushToDisk: true);
    }

    internal void Commit()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(artifactPath))
            throw new InvalidOperationException(
                $"Importer did not produce an artifact for '{assetPath}'.");

        if (File.Exists(_destinationArtifactPath))
        {
            try { File.Replace(artifactPath, _destinationArtifactPath, null, ignoreMetadataErrors: true); }
            catch (PlatformNotSupportedException)
            {
                File.Move(artifactPath, _destinationArtifactPath, overwrite: true);
            }
        }
        else
        {
            File.Move(artifactPath, _destinationArtifactPath);
        }
        _committed = true;
    }

    public void Dispose()
    {
        if (_committed || !File.Exists(artifactPath)) return;
        try { File.Delete(artifactPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
