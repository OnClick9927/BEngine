using BEngine.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Player;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("A BEngine project path is required.");
            }
            using var engineThread = EngineThreadContext.BindCurrentThread("BEngine Player");
            var projectPath = Path.GetFullPath(args[0]);
            using var services = new ServiceCollection()
                .AddBEnginePlayer(projectPath)
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            using var application = services.GetRequiredService<PlayerApplication>();
            application.Run();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
