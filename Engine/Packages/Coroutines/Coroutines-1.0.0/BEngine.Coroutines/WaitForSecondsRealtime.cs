using System.Collections;

namespace BEngine;

public sealed class WaitForSecondsRealtime(Fix64 seconds) : CustomYieldInstruction
{
    private Fix64 _remaining = Fix64.Max(Fix64.Zero, seconds);
    public Fix64 waitTime { get => _remaining; set => _remaining = Fix64.Max(Fix64.Zero, value); }
    public override bool keepWaiting
    {
        get
        {
            _remaining -= Time.unscaledDeltaTime;
            return _remaining > Fix64.Zero;
        }
    }
}
