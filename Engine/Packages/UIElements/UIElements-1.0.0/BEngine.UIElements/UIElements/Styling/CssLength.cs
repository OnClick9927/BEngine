namespace BEngine.UIElements;

/// <summary>Resolves CSS lengths deterministically, relative to their containing block.</summary>
internal static class CssLength
{
    internal static bool DependsOnContainerOrFont(string expression)
    {
        for (var i = 0; i < expression.Length; i++)
        {
            if (expression[i] == '%') return true;
            if (!char.IsLetter(expression[i])) continue;
            var start = i;
            while (i + 1 < expression.Length && char.IsLetter(expression[i + 1])) i++;
            if (expression.AsSpan(start, i - start + 1).Equals("em", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal static Fix64 Resolve(string expression, Fix64 basis, Fix64 viewportWidth,
        Fix64 viewportHeight, Fix64 fontSize, Fix64 fallback = default) =>
        TryResolve(expression, basis, viewportWidth, viewportHeight, fontSize, out var value) ? value : fallback;

    internal static bool TryResolve(string expression, Fix64 basis, Fix64 viewportWidth,
        Fix64 viewportHeight, Fix64 fontSize, out Fix64 value)
    {
        try
        {
            var parser = new Parser(expression, basis, viewportWidth, viewportHeight, fontSize);
            value = parser.Expression();
            parser.Space();
            return parser.End;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or DivideByZeroException)
        {
            value = Fix64.Zero;
            return false;
        }
    }

    private struct Parser(string text, Fix64 basis, Fix64 vw, Fix64 vh, Fix64 font)
    {
        private int _index = 0;
        internal bool End => _index == text.Length;
        internal void Space() { while (_index < text.Length && char.IsWhiteSpace(text[_index])) _index++; }
        private bool Eat(char token)
        {
            Space();
            if (_index >= text.Length || text[_index] != token) return false;
            _index++;
            return true;
        }
        internal Fix64 Expression()
        {
            var result = Product();
            while (true)
            {
                if (Eat('+')) result += Product();
                else if (Eat('-')) result -= Product();
                else return result;
            }
        }
        private Fix64 Product()
        {
            var result = Term();
            while (true)
            {
                if (Eat('*')) result *= Term();
                else if (Eat('/')) result /= Term();
                else return result;
            }
        }
        private Fix64 Term()
        {
            if (Eat('-')) return -Term();
            if (Eat('+')) return Term();
            if (Eat('(')) { var inner = Expression(); Require(')'); return inner; }
            Space();
            var start = _index;
            while (_index < text.Length && char.IsLetter(text[_index])) _index++;
            if (_index > start)
            {
                var function = text.AsSpan(start, _index - start);
                Require('(');
                var first = Expression();
                var minimum = first; var maximum = first;
                var second = Fix64.Zero; var third = Fix64.Zero; var count = 1;
                while (Eat(','))
                {
                    var value = Expression();
                    count++;
                    if (count == 2) second = value;
                    else if (count == 3) third = value;
                    minimum = Fix64.Min(minimum, value);
                    maximum = Fix64.Max(maximum, value);
                }
                Require(')');
                if (function.Equals("calc", StringComparison.OrdinalIgnoreCase) && count == 1) return first;
                if (function.Equals("min", StringComparison.OrdinalIgnoreCase)) return minimum;
                if (function.Equals("max", StringComparison.OrdinalIgnoreCase)) return maximum;
                if (function.Equals("clamp", StringComparison.OrdinalIgnoreCase) && count == 3)
                    return Fix64.Max(first, Fix64.Min(second, third));
                throw new FormatException();
            }
            while (_index < text.Length && (char.IsAsciiDigit(text[_index]) || text[_index] == '.')) _index++;
            // 'em' is a unit; only consume e when it starts a numeric exponent.
            if (_index < text.Length && text[_index] is 'e' or 'E')
            {
                var exponent = _index + 1;
                if (exponent < text.Length && text[exponent] is '+' or '-') exponent++;
                if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
                {
                    _index = exponent + 1;
                    while (_index < text.Length && char.IsAsciiDigit(text[_index])) _index++;
                }
            }
            if (!Fix64.TryParse(text.AsSpan(start, _index - start), out var number)) throw new FormatException();
            start = _index;
            while (_index < text.Length && (char.IsLetter(text[_index]) || text[_index] == '%')) _index++;
            return ApplyUnit(number, text.AsSpan(start, _index - start));
        }
        private Fix64 ApplyUnit(Fix64 number, ReadOnlySpan<char> unit)
        {
            if (unit.IsEmpty || unit.Equals("px", StringComparison.OrdinalIgnoreCase)) return number;
            // Multiply before dividing so 100% preserves the basis exactly.
            if (unit.SequenceEqual("%")) return number * basis / 100;
            if (unit.Equals("vw", StringComparison.OrdinalIgnoreCase)) return number * vw / 100;
            if (unit.Equals("vh", StringComparison.OrdinalIgnoreCase)) return number * vh / 100;
            if (unit.Equals("vmin", StringComparison.OrdinalIgnoreCase)) return number * Fix64.Min(vw, vh) / 100;
            if (unit.Equals("vmax", StringComparison.OrdinalIgnoreCase)) return number * Fix64.Max(vw, vh) / 100;
            if (unit.Equals("em", StringComparison.OrdinalIgnoreCase)) return number * font;
            if (unit.Equals("rem", StringComparison.OrdinalIgnoreCase)) return number * 16;
            if (unit.Equals("pt", StringComparison.OrdinalIgnoreCase)) return number * 96 / 72;
            if (unit.Equals("pc", StringComparison.OrdinalIgnoreCase)) return number * 16;
            if (unit.Equals("in", StringComparison.OrdinalIgnoreCase)) return number * 96;
            if (unit.Equals("cm", StringComparison.OrdinalIgnoreCase)) return number * 9600 / 254;
            if (unit.Equals("mm", StringComparison.OrdinalIgnoreCase)) return number * 960 / 254;
            throw new FormatException();
        }
        private void Require(char token) { if (!Eat(token)) throw new FormatException(); }
    }
}
