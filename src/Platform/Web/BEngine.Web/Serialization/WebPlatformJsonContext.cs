using System.Text.Json.Serialization;

namespace BEngine.Platform;

// Browser transport DTOs only. Application and package diagnostics keep their own serializer context.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WebAccessibilityDocument))]
[JsonSerializable(typeof(WebTextInputRequest))]
[JsonSerializable(typeof(GamepadStateSnapshot[]))]
internal sealed partial class WebPlatformJsonContext : JsonSerializerContext;
