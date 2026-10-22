namespace BEngine;

public readonly struct BCancellationRegistration : IDisposable, IEquatable<BCancellationRegistration>
{
    private readonly BCancellationSource.RegistrationNode? _node;

    internal BCancellationRegistration(BCancellationSource.RegistrationNode node) => _node = node;

    public void Dispose() => _node?.Dispose();
    public bool Equals(BCancellationRegistration other) => ReferenceEquals(_node, other._node);
    public override bool Equals(object? value) => value is BCancellationRegistration other && Equals(other);
    public override int GetHashCode() => _node?.GetHashCode() ?? 0;
    public static bool operator ==(BCancellationRegistration left, BCancellationRegistration right) =>
        left.Equals(right);
    public static bool operator !=(BCancellationRegistration left, BCancellationRegistration right) =>
        !left.Equals(right);
}
