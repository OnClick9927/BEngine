using System.Collections;

namespace BEngine;

public sealed class WaitForSeconds(Fix64 seconds) : YieldInstruction
{
    public Fix64 seconds { get; } = Fix64.Max(Fix64.Zero, seconds);
}
