namespace BEngine;

public readonly struct BCancellationToken : IEquatable<BCancellationToken>
{
    private readonly BCancellationSource? _source;

    internal BCancellationToken(BCancellationSource source) => _source = source;

    public static BCancellationToken None => default;
    public bool CanBeCanceled => _source is not null;
    public bool IsCancellationRequested => _source?.IsCancellationRequested == true;

    public void ThrowIfCancellationRequested()
    {
        if (IsCancellationRequested) throw new BOperationCanceledException(this);
    }

    public BCancellationRegistration Register(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _source is null ? default : _source.Register(callback);
    }

    public bool Equals(BCancellationToken other) => ReferenceEquals(_source, other._source);
    public override bool Equals(object? value) => value is BCancellationToken other && Equals(other);
    public override int GetHashCode() => _source?.GetHashCode() ?? 0;
    public static bool operator ==(BCancellationToken left, BCancellationToken right) => left.Equals(right);
    public static bool operator !=(BCancellationToken left, BCancellationToken right) => !left.Equals(right);
}
