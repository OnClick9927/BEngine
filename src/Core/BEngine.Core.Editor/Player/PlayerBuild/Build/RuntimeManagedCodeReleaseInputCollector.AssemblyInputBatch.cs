using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using BEngine.Platform;

namespace BEngine.Editor.Build.ProjectSystem;

public static partial class RuntimeManagedCodeReleaseInputCollector
{
    // One collection owns the complete inspection graph. No paths, assembly
    // metadata or hashes survive to the next build/PlayMode request.
    private sealed class AssemblyInputBatch(FileUtility files, PathUtility paths)
    {
        private readonly Dictionary<string, AssemblyInput> _inputs = new(StringComparer.Ordinal);

        internal AssemblyInput Read(string path)
        {
            path = paths.GetFullPath(path);
            if (_inputs.TryGetValue(path, out var previous)) return previous;

            string name;
            string[] references;
            string hash;
            long size;
            try
            {
                using var stream = files.OpenRead(path);
                size = stream.Length;
                using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
                {
                    if (!pe.HasMetadata)
                        throw new BadImageFormatException("The image does not contain managed metadata.", path);
                    var metadata = pe.GetMetadataReader();
                    if (!metadata.IsAssembly)
                        throw new BadImageFormatException("The image is not a managed assembly.", path);
                    name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
                    references = metadata.AssemblyReferences
                        .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static value => value, StringComparer.Ordinal).ToArray();
                }
                stream.Position = 0;
                hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            }
            catch (BadImageFormatException) { throw; }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                throw new BadImageFormatException($"Could not inspect managed assembly '{path}'.", exception);
            }

            var result = new AssemblyInput(name, references, hash, size);
            _inputs.Add(path, result);
            return result;
        }
    }

    private sealed record AssemblyInput(string Name, string[] References, string Sha256, long Size);
}

