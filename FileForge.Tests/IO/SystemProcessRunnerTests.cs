using System.ComponentModel;
using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests.IO;

public sealed class SystemProcessRunnerTests
{
    private const string Dotnet = "dotnet";

    private static readonly string s_testAssembly = typeof(SystemProcessRunnerTests).Assembly.Location;

    private static ProcessRunRequest ChildRequest(string mode, string? standardInput, TimeSpan timeout) =>
        new(Dotnet, [s_testAssembly, mode], standardInput, timeout);

    [Fact]
    public async Task RunAsync_WhenCommandSucceeds_ReturnsExitCodeAndOutput()
    {
        var request = new ProcessRunRequest(Dotnet, ["--version"], null, TimeSpan.FromSeconds(30));

        var result = await new SystemProcessRunner().RunAsync(request, CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Trim().Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenCommandFails_ReturnsExitCodeAndErrorOutput()
    {
        var result = await new SystemProcessRunner().RunAsync(
            ChildRequest(Program.FailMode, null, TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        result.ExitCode.Should().Be(3);
        result.StandardError.Should().Be("failure details");
    }

    [Fact]
    public async Task RunAsync_WhenInputIsGiven_WritesItToStandardInput()
    {
        var result = await new SystemProcessRunner().RunAsync(
            ChildRequest(Program.EchoMode, "héllo secret", TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        result.StandardOutput.Should().Be("héllo secret");
    }

    [Fact]
    public Task RunAsync_WhenCommandIsMissing_ThrowsWin32Exception()
    {
        var request = new ProcessRunRequest("fileforge-no-such-command", [], null, TimeSpan.FromSeconds(5));

        var act = () => new SystemProcessRunner().RunAsync(request, CancellationToken.None);

        return act.Should().ThrowAsync<Win32Exception>();
    }

    [Fact]
    public Task RunAsync_WhenCommandOutlivesTimeout_ThrowsTimeoutException()
    {
        var request = ChildRequest(Program.SleepMode, null, TimeSpan.FromMilliseconds(500));

        var act = () => new SystemProcessRunner().RunAsync(request, CancellationToken.None);

        return act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task RunAsync_WhenCancelledWhileRunning_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var request = ChildRequest(Program.SleepMode, null, TimeSpan.FromSeconds(60));

        var act = () => new SystemProcessRunner().RunAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var request = new ProcessRunRequest(Dotnet, ["--version"], null, TimeSpan.FromSeconds(30));

        var act = () => new SystemProcessRunner().RunAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ToString_WhenRequestHasStandardInput_DoesNotPrintIt()
    {
        var request = new ProcessRunRequest("secret-tool", ["store"], "s3cr3t-input", TimeSpan.FromSeconds(1));

        request.ToString().Should().Contain("secret-tool").And.NotContain("s3cr3t-input");
    }

    [Fact]
    public void ToString_WhenResultHasStandardOutput_DoesNotPrintIt()
    {
        var result = new ProcessRunResult(0, "s3cr3t-output", "warning");

        result.ToString().Should().Contain("0").And.NotContain("s3cr3t-output");
    }
}
