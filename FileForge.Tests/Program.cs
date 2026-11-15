namespace FileForge.Tests;

/// <summary>
/// Entry point of the test assembly, run by <c>dotnet FileForge.Tests.dll &lt;mode&gt;</c> as a portable child process
/// for the process runner tests (no dependency on cmd.exe, ping or a shell).
/// </summary>
internal static class Program
{
    public const string SleepMode = "sleep";
    public const string EchoMode = "echo";
    public const string FailMode = "fail";

    private static int Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case SleepMode:
                Thread.Sleep(TimeSpan.FromMinutes(5));
                return 0;
            case EchoMode:
                Console.Out.Write(Console.In.ReadToEnd());
                return 0;
            case FailMode:
                Console.Error.Write("failure details");
                return 3;
            default:
                return 0;
        }
    }
}
