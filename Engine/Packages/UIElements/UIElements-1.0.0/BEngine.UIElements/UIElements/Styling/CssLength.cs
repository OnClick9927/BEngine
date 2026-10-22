using System.Globalization;

namespace BEngine.UIElements;

/// <summary>Resolves CSS lengths at layout time, relative to their containing block.</summary>
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
    internal static float Resolve(string expression, float basis, float viewportWidth,
        float viewportHeight, float fontSize, float fallback = 0)
    {
        try
        {
            var parser = new Parser(expression, basis, viewportWidth, viewportHeight, fontSize);
            var value = parser.Expression();
            parser.Space();
            return parser.End && float.IsFinite(value) ? value : fallback;
        }
        catch (FormatException) { return fallback; }
    }

    private struct Parser(string text, float basis, float vw, float vh, float font)
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
        internal float Expression()
        {
            var result = Product();
            while (true)
            {
                if (Eat('+')) result += Product();
                else if (Eat('-')) result -= Product();
                else return result;
            }
        }
        private float Product()
        {
            var result = Term();
            while (true)
            {
                if (Eat('*')) result *= Term();
                else if (Eat('/')) result /= Term();
                else return result;
            }
        }
        private float Term()
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
                var second = 0f; var third = 0f; var count = 1;
                while (Eat(','))
                {
                    var value = Expression();
                    count++;
                    if (count == 2) second = value;
                    else if (count == 3) third = value;
                    // Match Enumerable.Min/Max's treatment of NaN, including an
                    // all-NaN maximum. Resolve rejects non-finite final values.
                    if (value < minimum || float.IsNaN(value)) minimum = value;
                    if (value > maximum || float.IsNaN(maximum)) maximum = value;
                }
                Require(')');
                if (function.Equals("calc", StringComparison.OrdinalIgnoreCase) && count == 1) return first;
                if (function.Equals("min", StringComparison.OrdinalIgnoreCase)) return minimum;
                if (function.Equals("max", StringComparison.OrdinalIgnoreCase)) return maximum;
                if (function.Equals("clamp", StringComparison.OrdinalIgnoreCase) && count == 3)
                    return Math.Max(first, Math.Min(second, third));
                throw new FormatException();
            }
            while (_index < text.Length && (char.IsDigit(text[_index]) || text[_index] == '.')) _index++;
            if (!float.TryParse(text.AsSpan(start, _index - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var number)) throw new FormatException();
            start = _index;
            while (_index < text.Length && (char.IsLetter(text[_index]) || text[_index] == '%')) _index++;
            var unit = text.AsSpan(start, _index - start);
            return number * UnitScale(unit);
        }
        private float UnitScale(ReadOnlySpan<char> unit)
        {
            if (unit.IsEmpty || unit.Equals("px", StringComparison.OrdinalIgnoreCase)) return 1;
            if (unit.SequenceEqual("%")) return basis / 100;
            if (unit.Equals("vw", StringComparison.OrdinalIgnoreCase)) return vw / 100;
            if (unit.Equals("vh", StringComparison.OrdinalIgnoreCase)) return vh / 100;
            if (unit.Equals("vmin", StringComparison.OrdinalIgnoreCase)) return Math.Min(vw, vh) / 100;
            if (unit.Equals("vmax", StringComparison.OrdinalIgnoreCase)) return Math.Max(vw, vh) / 100;
            if (unit.Equals("em", StringComparison.OrdinalIgnoreCase)) return font;
            if (unit.Equals("rem", StringComparison.OrdinalIgnoreCase)) return 16;
            if (unit.Equals("pt", StringComparison.OrdinalIgnoreCase)) return 96f / 72;
            if (unit.Equals("pc", StringComparison.OrdinalIgnoreCase)) return 16;
            if (unit.Equals("in", StringComparison.OrdinalIgnoreCase)) return 96;
            if (unit.Equals("cm", StringComparison.OrdinalIgnoreCase)) return 96f / 2.54f;
            if (unit.Equals("mm", StringComparison.OrdinalIgnoreCase)) return 96f / 25.4f;
            throw new FormatException();
        }
        private void Require(char token) { if (!Eat(token)) throw new FormatException(); }
    }
}
