using System.Text.Json;
using System.Text.Json.Nodes;
namespace BEngine.Serialization;
internal static class AssetSnapshotComparison
{
    internal static bool Equal<T>(T left, T right) => JsonNode.DeepEquals(JsonSerializer.SerializeToNode(left), JsonSerializer.SerializeToNode(right));
}
