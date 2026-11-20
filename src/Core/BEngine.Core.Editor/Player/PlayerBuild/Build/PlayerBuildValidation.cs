using BEngine.Platform;
namespace BEngine.Editor.Build;
public static class PlayerBuildValidation
{
 public static event Action<PlayerBuildContext,IEnumerable<string>,FileUtility,PathUtility,CancellationToken>? Assets;
 public static void ValidateAssets(PlayerBuildContext context,IEnumerable<string> paths,FileUtility files,PathUtility pathService,CancellationToken token) => Assets?.Invoke(context,paths,files,pathService,token);
}
