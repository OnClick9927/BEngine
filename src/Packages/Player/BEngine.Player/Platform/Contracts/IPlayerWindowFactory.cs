namespace BEngine.Player;

public interface IPlayerWindowFactory
{
    IPlayerWindow Create(PlayerWindowOptions options);
}
