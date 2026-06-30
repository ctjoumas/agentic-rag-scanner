using AgenticRagScanner.BingCustomSearchCli.Cli;

namespace AgenticRagScanner.BingCustomSearchCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return CliParser.Invoke(args);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Interrupted.");
            return 1;
        }
    }
}
