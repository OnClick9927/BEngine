using BEngine.Player;
using BEngine;
using BEngine.App;

namespace Game;

/// <summary>The first gameplay scene starts the game after AOT has prepared its assemblies.</summary>
public sealed class GameEntry : MonoBehaviour
{
    public override void Awake()
    {
        // Startup can synchronously change scenes. Reuse this domain while it is Starting too.
        if (Launcher.Current?.ActiveDomain is not ProjectGame)
            Launcher.Run(new ProjectGame(), replaceExisting: true);
    }
}
