namespace BEngine.Player;

public interface IPlayerInputContextFactory
{
    IPlayerInputContext Create(IPlayerWindow window);
}
