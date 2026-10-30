using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

/// <summary>
/// Accepts only the SDK's documented eligible-body IL stripping while retaining
/// the exact canonical metadata consumed by native AOT and the AB loader.
/// </summary>
internal static class FrozenScriptStripVerifier
{
    internal const string Command = "--verify-sdk-stripped-images";

    internal static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != Command) return false;
        if (args.Length != 2) throw new ArgumentException(Command + " <frozen-sdk-output-list>");
        var records = File.ReadAllLines(args[1]).Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(Parse).ToArray();
        if (records.Length == 0) throw new InvalidDataException("The SDK returned no frozen script outputs to verify.");
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in records)
        {
            if (!outputs.Add(Path.GetFullPath(image.Output)))
                throw new InvalidDataException("The SDK returned duplicate frozen script outputs: " + image.Output);
            Verify(image);
        }
        return true;
    }

    private sealed record Image(string Source, string Output, string TokenFile,
        bool IlStripped, string Sha256, Guid Mvid);

    private static Image Parse(string line)
    {
        // A vertical bar cannot occur in a Windows path. This is a generated
        // MSBuild item list, so do not interpret shell quoting or escapes.
        var fields = line.Split('|');
        if (fields.Length != 6 || fields[0].Length == 0 || fields[1].Length == 0 ||
            fields[4].Length != 64 || fields[4].Any(static c => !Uri.IsHexDigit(c)) ||
            !Guid.TryParse(fields[5], out var mvid) || mvid == Guid.Empty)
            throw new InvalidDataException("Invalid frozen script SDK output record.");
        return new(fields[0], fields[1], fields[2],
            bool.TryParse(fields[3], out var stripped) && stripped, fields[4], mvid);
    }

    private static void Verify(Image image)
    {
        var original = File.ReadAllBytes(image.Source);
        if (!Hash(original).Equals(image.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The canonical Web script changed after native compilation: " + image.Source);
        using var originalPe = new PEReader(new MemoryStream(original, writable: false));
        var metadata = originalPe.GetMetadataReader();
        if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != image.Mvid)
            throw new InvalidDataException("The canonical Web script MVID changed after native compilation: " + image.Source);

        var output = File.ReadAllBytes(image.Output);
        if (original.AsSpan().SequenceEqual(output)) return;
        using var outputPe = new PEReader(new MemoryStream(output, writable: false));
        if (!originalPe.GetMetadata().GetContent().AsSpan().SequenceEqual(outputPe.GetMetadata().GetContent().AsSpan()))
            throw new InvalidDataException("SDK IL stripping changed canonical Web script metadata: " + image.Output);

        if (!image.IlStripped || string.IsNullOrWhiteSpace(image.TokenFile) || !File.Exists(image.TokenFile))
        {
            Restore(image, "the SDK did not provide eligible-method stripping metadata");
            return;
        }

        var lines = File.ReadAllLines(image.TokenFile);
        if (lines.Length < 2 || string.IsNullOrWhiteSpace(lines[0]) ||
            !Guid.TryParse(lines[1], out var tokenMvid) || tokenMvid != image.Mvid)
            throw new InvalidDataException("SDK eligible-method tokens have a different canonical MVID: " + image.TokenFile);
        if (!Path.IsPathFullyQualified(lines[0]) || !File.Exists(lines[0]))
        {
            Restore(image, "the SDK's eligible-method source image is unavailable");
            return;
        }
        if (!Hash(File.ReadAllBytes(lines[0])).Equals(image.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SDK eligible-method tokens refer to different canonical Web script bytes: " + image.TokenFile);

        var eligible = new HashSet<int>();
        foreach (var line in lines.Skip(2))
        {
            if (!int.TryParse(line, System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out var token) ||
                ((uint)token >> 24) != 6 || (token & 0x00ffffff) == 0 ||
                (token & 0x00ffffff) > metadata.MethodDefinitions.Count || !eligible.Add(token))
                throw new InvalidDataException("Invalid or duplicate SDK eligible-method token: " + image.TokenFile);
        }

        // The compiler excludes unsupported interpreter bridges, generic
        // implementations, deoptimization and synchronized methods itself.
        // The SDK additionally retains bodies shared with any unselected token.
        var uses = new Dictionary<int, int>();
        foreach (var handle in metadata.MethodDefinitions)
        {
            var rva = metadata.GetMethodDefinition(handle).RelativeVirtualAddress;
            uses[rva] = uses.GetValueOrDefault(rva) + 1;
        }
        foreach (var token in eligible)
        {
            var handle = MetadataTokens.MethodDefinitionHandle(token & 0x00ffffff);
            var rva = metadata.GetMethodDefinition(handle).RelativeVirtualAddress;
            if (rva == 0) throw new InvalidDataException("An SDK eligible-method token has no IL body: " + image.TokenFile);
            uses[rva]--;
        }

        // Reproduce only ILStrip.TrimIndividualMethods' in-place byte changes.
        // A full-file comparison also protects resources, attributes, initial
        // field data, PE headers and every unselected/shared IL method body.
        var expected = (byte[])original.Clone();
        foreach (var (rva, remainingUsers) in uses)
        {
            if (remainingUsers != 0 || rva == 0) continue;
            var sectionIndex = originalPe.PEHeaders.GetContainingSectionIndex(rva);
            if (sectionIndex < 0) throw new InvalidDataException("SDK eligible-method RVA has no PE section: " + image.TokenFile);
            var section = originalPe.PEHeaders.SectionHeaders[sectionIndex];
            var offset = checked(rva - section.VirtualAddress + section.PointerToRawData);
            var bodySize = originalPe.GetMethodBody(rva).Size;
            var format = original[offset] & 3;
            if (format is not (2 or 3)) throw new InvalidDataException("Unsupported SDK eligible-method body format: " + image.TokenFile);
            var preservedHeader = format == 2 ? 1 : 4;
            if (format == 2) expected[offset] = 2;
            Array.Clear(expected, checked(offset + preservedHeader), checked(bodySize - preservedHeader));
        }
        if (!expected.AsSpan().SequenceEqual(output))
            throw new InvalidDataException("SDK IL stripping changed bytes outside its canonical eligible method bodies: " + image.Output);
        Console.WriteLine("BEngine Web: verified SDK eligible-method IL stripping without changing canonical metadata: " + Path.GetFileName(image.Output));
    }

    private static string Hash(byte[] image) => Convert.ToHexString(SHA256.HashData(image));

    private static void Restore(Image image, string reason)
    {
        File.Copy(image.Source, image.Output, overwrite: true);
        if (!Hash(File.ReadAllBytes(image.Output)).Equals(image.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unable to restore the complete canonical Web script: " + image.Output);
        Console.WriteLine("BEngine Web: keeping the complete canonical script because " + reason + ": " + Path.GetFileName(image.Output));
    }
}
