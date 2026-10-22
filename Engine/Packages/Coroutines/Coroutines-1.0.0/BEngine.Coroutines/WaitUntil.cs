using System.Collections;

namespace BEngine;

public sealed class WaitUntil(Func<bool> predicate) : CustomYieldInstruction
{
    private readonly Func<bool> _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    public override bool keepWaiting => !_predicate();
}
