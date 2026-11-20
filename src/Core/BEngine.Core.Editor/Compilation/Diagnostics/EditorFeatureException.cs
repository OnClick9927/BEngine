namespace BEngine.Editor;

internal sealed class EditorFeatureException(string feature, Exception innerException)
    : Exception($"Editor feature '{feature}' failed: {innerException.Message}", innerException);
