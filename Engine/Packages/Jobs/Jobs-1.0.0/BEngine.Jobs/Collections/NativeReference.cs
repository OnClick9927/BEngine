namespace BEngine.Jobs;

public struct NativeReference<T> : IDisposable, INativeContainer where T : unmanaged
{
    private NativeArray<T> _storage;

    public NativeReference()
    {
        _storage = new NativeArray<T>(1);
    }

    public NativeReference(T value)
    {
        _storage = new NativeArray<T>(1);
        _storage[0] = value;
    }

    public readonly bool isCreated => _storage.isCreated;
    public T Value { readonly get => _storage[0]; set => _storage[0] = value; }
    public void Dispose() => _storage.Dispose();
    readonly INativeContainerState? INativeContainer.ContainerState =>
        ((INativeContainer)_storage).ContainerState;
}
