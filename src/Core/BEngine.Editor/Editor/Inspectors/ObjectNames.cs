using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

public static class ObjectNames
{
    public static string NicifyVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var result = new System.Text.StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (character == '_')
            {
                if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
                continue;
            }
            if (index > 0 && char.IsUpper(character) && result[^1] != ' ') result.Append(' ');
            result.Append(character);
        }
        if (result.Length > 0) result[0] = char.ToUpperInvariant(result[0]);
        return result.ToString();
    }
}
