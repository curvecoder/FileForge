using System.ComponentModel;
using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests.IO;

public sealed class SecretServiceSecretStoreTests
{
    private static readonly Dictionary<string, string> s_attributes = new() { ["application"] = "DiscLookup", ["key"] = "discogs token" };

    private readonly FakeProcessRunner _runner = new();

    private SecretServiceSecretStore CreateStore() =>
        new("DiscLookup Discogs token", s_attributes, _runner, TimeSpan.FromSeconds(5));

    [Fact]
    public async Task WriteSecretAsync_WhenCalled_PassesSecretOnStdinAndAttributesAsSeparateArguments()
    {
        _runner.Next = new ProcessRunResult(0, string.Empty, string.Empty);

        await CreateStore().WriteSecretAsync("s3cr3t value", CancellationToken.None);

        var request = _runner.Requests.Should().ContainSingle().Subject;
        request.FileName.Should().Be("secret-tool");
        request.StandardInput.Should().Be("s3cr3t value");
        request.Arguments.Should().Equal("store", "--label=DiscLookup Discogs token", "application", "DiscLookup", "key", "discogs token");
        request.Arguments.Should().NotContain(a => a.Contains("s3cr3t", StringComparison.Ordinal));
        request.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteSecretAsync_WhenSecretIsBlank_ThrowsArgumentException(string? secret)
    {
        var act = () => CreateStore().WriteSecretAsync(secret!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _runner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteSecretAsync_WhenExitCodeIsNotZero_ThrowsSecretStoreExceptionWithoutSecret()
    {
        _runner.Next = new ProcessRunResult(1, string.Empty, "Cannot create an item in a locked collection");

        var act = () => CreateStore().WriteSecretAsync("s3cr3t", CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<SecretStoreException>()).Which;
        exception.Should().NotBeOfType<SecretStoreUnavailableException>();
        exception.Message.Should().Contain("store").And.Contain("locked collection").And.NotContain("s3cr3t");
    }

    [Fact]
    public async Task ReadSecretAsync_WhenSecretExists_ReturnsStdout()
    {
        _runner.Next = new ProcessRunResult(0, "tok-123", string.Empty);

        var secret = await CreateStore().ReadSecretAsync(CancellationToken.None);

        secret.Should().Be("tok-123");
        _runner.Requests.Single().Arguments.Should().Equal("lookup", "application", "DiscLookup", "key", "discogs token");
        _runner.Requests.Single().StandardInput.Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public async Task ReadSecretAsync_WhenSecretIsAbsent_ReturnsNull(int exitCode)
    {
        _runner.Next = new ProcessRunResult(exitCode, string.Empty, string.Empty);

        var secret = await CreateStore().ReadSecretAsync(CancellationToken.None);

        secret.Should().BeNull();
    }

    [Fact]
    public async Task ReadSecretAsync_WhenKeyringFails_ThrowsSecretStoreException()
    {
        _runner.Next = new ProcessRunResult(1, string.Empty, "Cannot autolaunch D-Bus without X11 $DISPLAY");

        var act = () => CreateStore().ReadSecretAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<SecretStoreException>()).Which.Message.Should().Contain("lookup").And.Contain("D-Bus");
    }

    [Fact]
    public async Task ClearSecretAsync_WhenCalled_RunsClearWithAttributes()
    {
        _runner.Next = new ProcessRunResult(0, string.Empty, string.Empty);

        await CreateStore().ClearSecretAsync(CancellationToken.None);

        _runner.Requests.Single().Arguments.Should().Equal("clear", "application", "DiscLookup", "key", "discogs token");
    }

    [Fact]
    public async Task ClearSecretAsync_WhenExitCodeIsNotZeroWithError_ThrowsSecretStoreException()
    {
        _runner.Next = new ProcessRunResult(1, string.Empty, "boom");

        var act = () => CreateStore().ClearSecretAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<SecretStoreException>()).Which.Message.Should().Contain("clear");
    }

    [Fact]
    public Task ClearSecretAsync_WhenNothingIsStored_Completes()
    {
        _runner.Next = new ProcessRunResult(1, string.Empty, string.Empty);

        var act = () => CreateStore().ClearSecretAsync(CancellationToken.None);

        return act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReadSecretAsync_WhenCommandIsMissing_ThrowsSecretStoreUnavailableException()
    {
        _runner.Failure = new Win32Exception(2, "No such file");

        var act = () => CreateStore().ReadSecretAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<SecretStoreUnavailableException>()).Which.Message.Should().Contain("secret-tool");
    }

    [Fact]
    public async Task WriteSecretAsync_WhenCommandTimesOut_ThrowsSecretStoreUnavailableExceptionWithoutSecret()
    {
        _runner.Failure = new TimeoutException();

        var act = () => CreateStore().WriteSecretAsync("s3cr3t", CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<SecretStoreUnavailableException>()).Which;
        exception.Message.Should().Contain("store").And.Contain("timed out").And.NotContain("s3cr3t");
    }

    [Fact]
    public Task ReadSecretAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        _runner.Failure = new OperationCanceledException();

        var act = () => CreateStore().ReadSecretAsync(CancellationToken.None);

        return act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IsAvailableAsync_WhenKeyringAnswersNotFound_ReturnsTrue()
    {
        _runner.Next = new ProcessRunResult(1, string.Empty, string.Empty);

        var available = await CreateStore().IsAvailableAsync(CancellationToken.None);

        available.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_WhenCommandIsMissing_ReturnsFalse()
    {
        _runner.Failure = new Win32Exception(2, "No such file");

        var available = await CreateStore().IsAvailableAsync(CancellationToken.None);

        available.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_WhenSessionBusIsAbsent_ReturnsFalse()
    {
        _runner.Next = new ProcessRunResult(1, string.Empty, "Cannot autolaunch D-Bus without X11 $DISPLAY");

        var available = await CreateStore().IsAvailableAsync(CancellationToken.None);

        available.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_WhenCommandTimesOut_ReturnsFalse()
    {
        _runner.Failure = new TimeoutException();

        var available = await CreateStore().IsAvailableAsync(CancellationToken.None);

        available.Should().BeFalse();
    }

    [Fact]
    public Task IsAvailableAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        _runner.Failure = new OperationCanceledException();

        var act = () => CreateStore().IsAvailableAsync(CancellationToken.None);

        return act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_WhenAttributesAreEmpty_ThrowsArgumentException()
    {
        var act = () => new SecretServiceSecretStore("label", new Dictionary<string, string>(), _runner, TimeSpan.FromSeconds(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenLabelIsBlank_ThrowsArgumentException()
    {
        var act = () => new SecretServiceSecretStore(" ", s_attributes, _runner, TimeSpan.FromSeconds(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WhenTimeoutIsNotPositive_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new SecretServiceSecretStore("label", s_attributes, _runner, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WhenAttributeNameIsBlank_ThrowsArgumentException()
    {
        var attributes = new Dictionary<string, string> { [" "] = "value" };

        var act = () => new SecretServiceSecretStore("label", attributes, _runner, TimeSpan.FromSeconds(1));

        act.Should().Throw<ArgumentException>();
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public ProcessRunResult Next { get; set; } = new(0, string.Empty, string.Empty);

        public Exception? Failure { get; set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Failure is null ? Task.FromResult(Next) : Task.FromException<ProcessRunResult>(Failure);
        }
    }
}
