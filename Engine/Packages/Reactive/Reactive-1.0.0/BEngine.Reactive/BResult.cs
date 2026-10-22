namespace BEngine.Reactive;

public readonly struct BResult : IEquatable<BResult>
{
    private BResult(Exception? exception) => Exception = exception;

    public static BResult Success => default;
    public Exception? Exception { get; }
    public bool IsSuccess => Exception is null;
    public bool IsFailure => Exception is not null;

    public static BResult Failure(Exception exception) =>
        new(exception ?? throw new ArgumentNullException(nameof(exception)));

    public bool Equals(BResult other) => ReferenceEquals(Exception, other.Exception);
    public override bool Equals(object? value) => value is BResult other && Equals(other);
    public override int GetHashCode() => Exception?.GetHashCode() ?? 0;
    public override string ToString() => IsSuccess ? "Success" : $"Failure({Exception})";
}
