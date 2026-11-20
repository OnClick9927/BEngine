using System.Buffers;

namespace BEngine.Editor;

public static class ObjectNames
{
    public static string NicifyVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        char[]? rented = null;
        Span<char> result = name.Length <= 128
            ? stackalloc char[name.Length * 2]
            : rented = ArrayPool<char>.Shared.Rent(checked(name.Length * 2));
        try
        {
            var count = 0;
            for (var index = 0; index < name.Length; index++)
            {
                var character = name[index];
                if (character == '_')
                {
                    if (count > 0 && result[count - 1] != ' ') result[count++] = ' ';
                    continue;
                }
                if (index > 0 && count > 0 && char.IsUpper(character) && result[count - 1] != ' ') result[count++] = ' ';
                result[count++] = character;
            }
            if (count > 0) result[0] = char.ToUpperInvariant(result[0]);
            return new string(result[..count]);
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }
}
