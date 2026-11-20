namespace BEngine.Hosting;

/// <summary>Requests native execution of this exact runtime image on AOT players.
/// Updating a native image requires rebuilding the player, just as updating its host does.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class RuntimeNativeExecutionAttribute : Attribute;
