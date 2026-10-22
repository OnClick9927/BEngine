namespace BEngine.Editor;
public interface IAssetMoveHandler
{
    bool CanMove(string path);
    string Move(string oldPath, string newPath);
}
