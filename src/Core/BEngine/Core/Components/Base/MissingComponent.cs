namespace BEngine;

public sealed class MissingComponent : Component
{
    private string _originalType = string.Empty;
    private Dictionary<string, string> _serializedFields = [];
    private ThreadGuardedReadOnlyDictionary<string, string> _serializedFieldsView;

    public MissingComponent()
    {
        _serializedFieldsView = new ThreadGuardedReadOnlyDictionary<string, string>(_serializedFields);
    }

    public string originalType
    {
        get { MainThreadGuard.Ensure(); return _originalType; }
        internal set => _originalType = value;
    }

    public IReadOnlyDictionary<string, string> serializedFields
    {
        get { MainThreadGuard.Ensure(); return _serializedFieldsView; }
        internal set
        {
            _serializedFields = value is null
                ? []
                : new Dictionary<string, string>(value, StringComparer.Ordinal);
            _serializedFieldsView = new ThreadGuardedReadOnlyDictionary<string, string>(_serializedFields);
        }
    }
}
