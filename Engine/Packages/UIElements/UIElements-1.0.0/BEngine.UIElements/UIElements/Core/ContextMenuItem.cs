using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public sealed record ContextMenuItem(string Name, Action? Action, bool Enabled, bool IsChecked, bool Separator);
