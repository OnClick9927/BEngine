namespace BEngine.Editor;

internal sealed record ProfilerCounterDetail(
    string Category,
    string Name,
    string DisplayValue,
    string Description,
    ProfilerCounterValueKind Kind,
    double NumericValue = 0,
    long IntegerValue = 0,
    bool BooleanValue = false,
    string TextValue = "")
{
    internal string Key => $"{Category}\u001f{Name}";
}
