namespace BEngine.Editor;

public static class SceneHandleUtility
{
    public static Vector2 GetHandlePosition(GameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        var minimumX = Fix64.FromRaw(long.MaxValue);
        var minimumY = Fix64.FromRaw(long.MaxValue);
        var maximumX = Fix64.FromRaw(long.MinValue);
        var maximumY = Fix64.FromRaw(long.MinValue);
        var hasVisualBounds = false;
        foreach (var renderer in gameObject.GetComponents<SpriteRenderer>())
        {
            var visual = renderer.ResolveSpriteUnchecked();
            var pivot = renderer.useAtlasPivot && !string.IsNullOrWhiteSpace(renderer.atlas)
                ? visual.Pivot
                : renderer.pivot;
            var center = new Vector2(
                (Fix64.Half - pivot.x) * renderer.size.x,
                (Fix64.Half - pivot.y) * renderer.size.y);
            var half = renderer.size * Fix64.Half;
            Encapsulate(gameObject.transform.TransformPoint(center + new Vector2(-half.x, -half.y)));
            Encapsulate(gameObject.transform.TransformPoint(center + new Vector2(half.x, -half.y)));
            Encapsulate(gameObject.transform.TransformPoint(center + new Vector2(half.x, half.y)));
            Encapsulate(gameObject.transform.TransformPoint(center + new Vector2(-half.x, half.y)));
        }
        return hasVisualBounds
            ? new Vector2((minimumX + maximumX) * Fix64.Half, (minimumY + maximumY) * Fix64.Half)
            : gameObject.transform.position;

        void Encapsulate(Vector2 point)
        {
            hasVisualBounds = true;
            minimumX = Fix64.Min(minimumX, point.x);
            minimumY = Fix64.Min(minimumY, point.y);
            maximumX = Fix64.Max(maximumX, point.x);
            maximumY = Fix64.Max(maximumY, point.y);
        }
    }
}
