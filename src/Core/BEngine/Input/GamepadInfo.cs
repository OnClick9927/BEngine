namespace BEngine;

/// <summary>Host slot identity and display name. The index stays explicit when other slots disconnect.</summary>
public readonly record struct GamepadInfo(int index, string name, bool connected);
