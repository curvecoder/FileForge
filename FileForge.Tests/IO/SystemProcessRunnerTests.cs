using System.ComponentModel;
using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests.IO;

public sealed class SystemProcessRunnerTests
{
    private static readonly string s_dotnet = "dotnet";

    [Fact]
    public async Task RunAsync_WhenCommandSucceeds_ReturnsExitCodeAndOutput()
    {
        var request = new ProcessRunRequest(s_dotnet, ["--version"], null, TimeSpan.FromSeconds(30));

        var result = await new SystemProcessRunner().RunAsync(request, CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Trim().Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenCommandFails_ReturnsNonZeroExitCode()
    {
        var request = new ProcessRunRequest(s_dotnet, ["--no-such-option-for-fileforge"], null, TimeSpan.FromSeconds(30));

        var result = await new SystemProcessRunner().RunAsync(request, CancellationToken.None);

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public Task RunAsync_WhenCommandIsMissing_ThrowsWin32Exception()
    {
        var request = new ProcessRunRequest("fileforge-no-such-command", [], null, TimeSpan.FromSeconds(5));

        var act = () => new SystemProcessRunner().RunAsync(request, CancellationToken.None);

        return act.Should().ThrowAsync<Win32Exception>();
    }

    [Fact]
    public async Task RunAsync_WhenInputIsGivenToACommandThatIgnoresIt_Completes()
    {
        var request = new ProcessRunRequest(s_dotnet, ["--version"], "ignored input", TimeSpan.FromSeconds(30));

        var result = await new SystemProcessRunner().RunAsync(request, CancellationToken.None);

        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public Task RunAsync_WhenCommandOutlivesTimeout_ThrowsTimeoutException()
    {
        var request = new ProcessRunRequest("cmd.exe", ["/c", "ping -n 30 127.0.0.1 >nul"], null, TimeSpan.FromMilliseconds(300));

        var act = () => new SystemProcessRunner().RunAsync(request, CancellationToken.None);

        return act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task RunAsync_WhenCancelledWhileRunning_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var request = new ProcessRunRequest("cmd.exe", ["/c", "ping -n 30 127.0.0.1 >nul"], null, TimeSpan.FromSeconds(60));

        var act = () => new SystemProcessRunner().RunAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_WhenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var request = new ProcessRunRequest(s_dotnet, ["--version"], null, TimeSpan.FromSeconds(30));

        var act = () => new SystemProcessRunner().RunAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
