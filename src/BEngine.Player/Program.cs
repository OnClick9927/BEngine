using BEngine.Serialization;

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
            var projectPath = Path.GetFullPath(args[0]);
            using var application = new PlayerApplication(projectPath);
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
