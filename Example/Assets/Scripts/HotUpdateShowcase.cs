using System.Security.Cryptography;
using System.Text;
using BEngine;

namespace Game;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class HotUpdateShowcase : MonoBehaviour
{
    private const string Profile = "v1";
    private const string CodeMarker = "REMOTE-CODE-V1";
    private const string ResourceMarker = "RESOURCE-V1";
    private const string ShaderMarker = "SHADER-V1";
    private SpriteRenderer _renderer = null!;

    public override void Start()
    {
        _renderer = GetComponent<SpriteRenderer>() ??
                    throw new InvalidOperationException("HotUpdateShowcase requires a SpriteRenderer.");
        var release = Resources.Load<TextAsset>("HotUpdate/ReleaseInfo") ??
                      throw new InvalidDataException("The HotUpdate release marker is missing.");
        var shader = Resources.Load<Shader>("HotUpdate/Release") ??
                     throw new InvalidDataException("The HotUpdate Shader is missing.");
        var badge = Resources.Load<Sprite>("HotUpdate/ReleaseBadge") ??
                    throw new InvalidDataException("The HotUpdate badge Sprite is missing.");
        var badgeBytes = Resources.Load<byte[]>("HotUpdate/ReleaseBadge") ??
                         throw new InvalidDataException("The HotUpdate badge bytes are missing.");

        ValidatePayload(release, shader, badgeBytes);
        _renderer.sprite = badge;
        _renderer.size = new Vector2((Fix64)4.8, (Fix64)1.8);
        _renderer.material = new Material(shader) { name = "HotUpdate V1 Material" };

        var proof = CreateProof(release.text, shader.sourceCode, badgeBytes);
        WriteProof(proof);
        Debug.Log(proof);
    }

    public override void Update()
    {
        transform.Rotate((Fix64)8 * Time.deltaTime);
        var pulse = (Fix64)0.88 + (Mathf.Sin(Time.time * (Fix64)1.5) + Fix64.One) * (Fix64)0.06;
        _renderer.color = new Color((Fix64)0.35 * pulse, (Fix64)0.82 * pulse, pulse, Fix64.One);
    }

    public override void Reset() => runInEditMode = false;

    private static void ValidatePayload(TextAsset release, Shader shader, byte[] badgeBytes)
    {
        if (!release.text.Contains(ResourceMarker, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"HotUpdate text mismatch. Expected '{ResourceMarker}'.");
        if (!shader.sourceCode.Contains(ShaderMarker, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"HotUpdate Shader mismatch. Expected '{ShaderMarker}'.");
        if (badgeBytes.Length < 8 || !badgeBytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("The HotUpdate badge is not a PNG payload.");
    }

    private static string CreateProof(string resource, string shader, byte[] image) =>
        $"HOTUPDATE_DEMO_OK|profile={Profile}|code={CodeMarker}|resource={ResourceMarker}|" +
        $"shader={ShaderMarker}|assembly={typeof(HotUpdateShowcase).Assembly.ManifestModule.ModuleVersionId:N}|" +
        $"textSha256={Hash(Encoding.UTF8.GetBytes(resource))}|" +
        $"shaderSha256={Hash(Encoding.UTF8.GetBytes(shader))}|imageSha256={Hash(image)}";

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WriteProof(string proof)
    {
        var configuredPath = Environment.GetEnvironmentVariable("BENGINE_HOTUPDATE_PROOF_PATH");
        if (string.IsNullOrWhiteSpace(configuredPath)) return;
        var path = Path.GetFullPath(configuredPath);
        var directory = Path.GetDirectoryName(path) ??
                        throw new InvalidDataException("The HotUpdate proof path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, proof + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
