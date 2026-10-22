using System.Globalization;

namespace BEngine;

internal static class TextElementUtility
{
    internal static int PreviousBoundary(string text, int index) =>
        StringInfo.ParseCombiningCharacters(text).LastOrDefault(boundary => boundary < index);

    internal static int NextBoundary(string text, int index) =>
        StringInfo.ParseCombiningCharacters(text).FirstOrDefault(boundary => boundary > index, text.Length);
}
