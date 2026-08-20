
namespace BEngine.Editor;

public static class EditorSnapSettings
{
    public static Fix64 move { get; set; } = Fix64.FromDecimal(0.5m);
    public static Fix64 rotate { get; set; } = (Fix64)15;
    public static Fix64 scale { get; set; } = Fix64.FromDecimal(0.1m);
}
