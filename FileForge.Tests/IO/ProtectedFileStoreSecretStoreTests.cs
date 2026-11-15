using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests.IO;

public sealed class ProtectedFileStoreSecretStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fileforge_secretstore_{Guid.NewGuid():N}");

    private ISecretStore CreateStore() => new ProtectedFileStore(Path.Combine(_directory, "secret.dat"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadSecretAsync_WhenSecretWasWritten_ReturnsSecret()
    {
        var store = CreateStore();
        await store.WriteSecretAsync("tok-123", CancellationToken.None);

        var secret = await store.ReadSecretAsync(CancellationToken.None);

        secret.Should().Be("tok-123");
    }

    [Fact]
    public async Task ReadSecretAsync_WhenNothingIsStored_ReturnsNull()
    {
        var secret = await CreateStore().ReadSecretAsync(CancellationToken.None);

        secret.Should().BeNull();
    }

    [Fact]
    public async Task ClearSecretAsync_WhenSecretWasWritten_RemovesIt()
    {
        var store = CreateStore();
        await store.WriteSecretAsync("tok-123", CancellationToken.None);

        await store.ClearSecretAsync(CancellationToken.None);

        (await store.ReadSecretAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task WriteSecretAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => CreateStore().WriteSecretAsync("tok-123", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadSecretAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => CreateStore().ReadSecretAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ClearSecretAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => CreateStore().ClearSecretAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
