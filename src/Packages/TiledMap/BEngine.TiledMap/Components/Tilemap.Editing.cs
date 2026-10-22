namespace BEngine.TiledMap;

public sealed partial class Tilemap : ISerializationCallbackReceiver
{
    private int _editDepth, _editEpoch;
    private bool _editDirty, _editCanceled;
    private string? _editOriginalData;
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public long serializationCount { get; private set; }

    /// <summary>Updates cells immediately and persists once when the outermost scope ends.</summary>
    public EditScope BeginEdit()
    {
        EnsureCells();
        if (_editDepth++ == 0) { _editEpoch++; _editOriginalData = _serializedCells; _editCanceled = false; }
        return new EditScope(this, _editEpoch);
    }

    private void EndEdit(int epoch, bool cancel)
    {
        if (epoch != _editEpoch || _editDepth == 0) return;
        _editCanceled |= cancel;
        if (--_editDepth > 0) return;
        if (_editCanceled)
        {
            _serializedCells = _editOriginalData!; _loadedCellData = "\0"; _editDirty = false;
            EnsureCells(); _revision++;
        }
        else if (_editDirty) PersistCells();
        _editOriginalData = null;
    }
    public void OnBeforeSerialize() { if (_editDirty) PersistCells(force: true); }
    public void OnAfterDeserialize()
    { _editEpoch++; _editDepth = 0; _editDirty = false; _editOriginalData = null; _loadedCellData = "\0"; _revision++; }

    public sealed class EditScope : IDisposable
    {
        private Tilemap? _owner;
        private readonly int _epoch;
        private bool _canceled;
        internal EditScope(Tilemap owner, int epoch) { _owner = owner; _epoch = epoch; }
        /// <summary>Rolls the entire outer edit back when its last scope ends.</summary>
        public void Cancel() => _canceled = true;
        public void Dispose() { var owner = _owner; _owner = null; owner?.EndEdit(_epoch, _canceled); }
    }
}
