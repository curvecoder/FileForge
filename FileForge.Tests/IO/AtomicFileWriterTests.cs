using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests.IO;

public sealed class AtomicFileWriterTests : IDisposable
{
    private const string PreviousContent = "{ \"ExcludedLocalPorts\": [ 8006 ] }";
    private const string NewContent = "{ \"ExcludedLocalPorts\": [ 8006, 8007 ] }";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FileForge_Atomic_" + Guid.NewGuid().ToString("N"));
    private readonly string _target;

    public AtomicFileWriterTests()
    {
        Directory.CreateDirectory(_directory);
        _target = Path.Combine(_directory, "portatlas.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenTheFileExists_ReplacesItsContentAndLeavesNoOtherFile()
    {
        // Arrange
        await File.WriteAllTextAsync(_target, PreviousContent);

        // Act
        await AtomicFileWriter.WriteAllTextAsync(_target, NewContent);

        // Assert
        (await File.ReadAllTextAsync(_target)).Should().Be(NewContent);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenTheFileAndItsDirectoryDoNotExist_CreatesThem()
    {
        // Arrange
        var target = Path.Combine(_directory, "profiles", "home.json");

        // Act
        await AtomicFileWriter.WriteAllTextAsync(target, NewContent);

        // Assert
        (await File.ReadAllTextAsync(target)).Should().Be(NewContent);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenNoEncodingIsGiven_WritesUtf8WithoutByteOrderMark()
    {
        // Act
        await AtomicFileWriter.WriteAllTextAsync(_target, "Média-NAS");

        // Assert
        (await File.ReadAllBytesAsync(_target)).Should().Equal(Encoding.UTF8.GetBytes("Média-NAS"));
    }

    [Fact]
    public async Task WriteAsync_WhenWritingTheContentFails_KeepsThePreviousContentAndLeavesNoOtherFile()
    {
        // Arrange
        await File.WriteAllTextAsync(_target, PreviousContent);

        // Act
        var write = () => AtomicFileWriter.WriteAsync(_target, async (stream, cancellationToken) =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{ \"Excluded"), cancellationToken);
            throw new InvalidOperationException("Serialization failed.");
        });

        // Assert
        await write.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(_target)).Should().Be(PreviousContent);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenCancelled_KeepsThePreviousContent()
    {
        // Arrange
        await File.WriteAllTextAsync(_target, PreviousContent);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var write = () => AtomicFileWriter.WriteAllTextAsync(_target, NewContent, cancellationToken: cancellation.Token);

        // Assert
        await write.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllTextAsync(_target)).Should().Be(PreviousContent);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenAReaderHoldsTheFileOpen_KeepsTheNewContentPendingAndTheReaderReadsThePreviousOne()
    {
        // Arrange
        await File.WriteAllTextAsync(_target, PreviousContent);
        var reader = new StreamReader(new FileStream(_target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1), Encoding.UTF8, false, bufferSize: 1);
        var firstHalf = new char[PreviousContent.Length / 2];
        await reader.ReadBlockAsync(firstHalf);

        // Act
        var write = () => AtomicFileWriter.WriteAllTextAsync(_target, NewContent);

        // Assert
        await write.Should().ThrowAsync<IOException>().WithMessage("*portatlas.json.pending*");
        var secondHalf = await reader.ReadToEndAsync();
        reader.Dispose();
        (new string(firstHalf) + secondHalf).Should().Be(PreviousContent);
        (await File.ReadAllTextAsync(_target)).Should().Be(PreviousContent);
        (await File.ReadAllTextAsync(_target + ".pending")).Should().Be(NewContent);
    }

    [Fact]
    public async Task WriteAsync_WhenAnotherWriteOfTheSameFileIsInProgress_ThrowsWithoutDisturbingIt()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = AtomicFileWriter.WriteAsync(_target, async (stream, cancellationToken) =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(NewContent), cancellationToken);
            started.SetResult();
            await release.Task;
        });
        (await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().Be(started.Task, "the first write must be in progress");

        // Act
        var second = () => AtomicFileWriter.WriteAllTextAsync(_target, PreviousContent);

        // Assert
        await second.Should().ThrowAsync<IOException>();
        release.SetResult();
        await first;
        (await File.ReadAllTextAsync(_target)).Should().Be(NewContent);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenWritesFollowEachOtherClosely_AppliesEachAndLeavesNoOtherFile()
    {
        // Act
        for (var index = 0; index < 50; index++)
        {
            await AtomicFileWriter.WriteAllTextAsync(_target, $"{{ \"ScanIntervalSeconds\": {index} }}");
        }

        // Assert
        (await File.ReadAllTextAsync(_target)).Should().Be("{ \"ScanIntervalSeconds\": 49 }");
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public async Task RecoverInterruptedWrite_WhenAWriteIsInProgress_ThrowsWithoutDisturbingIt()
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var write = AtomicFileWriter.WriteAsync(_target, async (stream, cancellationToken) =>
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(NewContent), cancellationToken);
            started.SetResult();
            await release.Task;
        });
        (await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().Be(started.Task, "the write must be in progress");

        // Act
        var recover = () => AtomicFileWriter.RecoverInterruptedWrite(_target);

        // Assert
        recover.Should().Throw<IOException>().WithMessage("*portatlas.json.lock*");
        release.SetResult();
        await write;
        (await File.ReadAllTextAsync(_target)).Should().Be(NewContent);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenThePendingNameCannotBeTaken_LeavesTheFileUnchangedAndNoTemporaryFile()
    {
        // Arrange
        await File.WriteAllTextAsync(_target, PreviousContent);
        await File.WriteAllTextAsync(_target + ".pending", PreviousContent);
        var scanner = new FileStream(_target + ".pending", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Act
        var write = () => AtomicFileWriter.WriteAllTextAsync(_target, NewContent);

        // Assert
        await write.Should().ThrowAsync<IOException>().WithMessage("*unchanged*");
        await scanner.DisposeAsync();
        (await File.ReadAllTextAsync(_target)).Should().Be(PreviousContent);
        File.Exists(_target + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenThePathIsASymbolicLink_WritesTheLinkedFileAndKeepsTheLink()
    {
        // Arrange
        var sharedDirectory = Path.Combine(_directory, "shared");
        Directory.CreateDirectory(sharedDirectory);
        var linked = Path.Combine(sharedDirectory, "portatlas.json");
        await File.WriteAllTextAsync(linked, PreviousContent);
        File.CreateSymbolicLink(_target, linked);

        // Act
        await AtomicFileWriter.WriteAllTextAsync(_target, NewContent);

        // Assert
        new FileInfo(_target).LinkTarget.Should().Be(linked);
        (await File.ReadAllTextAsync(linked)).Should().Be(NewContent);
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenTheFileHasAnExplicitAccessRule_KeepsIt()
    {
        // Arrange
        await File.WriteAllTextAsync(_target, PreviousContent);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var security = new FileInfo(_target).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.ReadData, AccessControlType.Allow));
        new FileInfo(_target).SetAccessControl(security);

        // Act
        await AtomicFileWriter.WriteAllTextAsync(_target, NewContent);

        // Assert
        var explicitRules = new FileInfo(_target).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();
        explicitRules.Should().Contain(rule => rule.IdentityReference == everyone);
    }

    [Fact]
    public void WriteAllText_WhenTheFileExists_ReplacesItsContentAndLeavesNoOtherFile()
    {
        // Arrange
        File.WriteAllText(_target, PreviousContent);

        // Act
        AtomicFileWriter.WriteAllText(_target, NewContent);

        // Assert
        File.ReadAllText(_target).Should().Be(NewContent);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public async Task RecoverInterruptedWrite_WhenAPendingContentIsLeft_ReplacesTheFileWithIt()
    {
        // Arrange
        await File.WriteAllTextAsync(_target, PreviousContent);
        await File.WriteAllTextAsync(_target + ".pending", NewContent);

        // Act
        var recovery = AtomicFileWriter.RecoverInterruptedWrite(_target);

        // Assert
        recovery.Should().Be(new InterruptedWriteRecovery(PendingContentApplied: true, IncompleteContentDiscarded: false));
        (await File.ReadAllTextAsync(_target)).Should().Be(NewContent);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public async Task RecoverInterruptedWrite_WhenAPendingContentIsLeftAndTheFileIsGone_CreatesTheFileFromIt()
    {
        // Arrange
        await File.WriteAllTextAsync(_target + ".pending", NewContent);

        // Act
        var recovery = AtomicFileWriter.RecoverInterruptedWrite(_target);

        // Assert
        recovery.PendingContentApplied.Should().BeTrue();
        (await File.ReadAllTextAsync(_target)).Should().Be(NewContent);
    }

    [Fact]
    public async Task RecoverInterruptedWrite_WhenAnIncompleteContentIsLeft_DeletesItAndKeepsTheFile()
    {
        // Arrange
        await File.WriteAllTextAsync(_target, PreviousContent);
        await File.WriteAllTextAsync(_target + ".tmp", "{ \"Excluded");

        // Act
        var recovery = AtomicFileWriter.RecoverInterruptedWrite(_target);

        // Assert
        recovery.Should().Be(new InterruptedWriteRecovery(PendingContentApplied: false, IncompleteContentDiscarded: true));
        (await File.ReadAllTextAsync(_target)).Should().Be(PreviousContent);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public async Task RecoverInterruptedWrite_WhenNothingIsLeft_ChangesNothing()
    {
        // Arrange
        await File.WriteAllTextAsync(_target, PreviousContent);

        // Act
        var recovery = AtomicFileWriter.RecoverInterruptedWrite(_target);

        // Assert
        recovery.Should().Be(new InterruptedWriteRecovery(PendingContentApplied: false, IncompleteContentDiscarded: false));
        (await File.ReadAllTextAsync(_target)).Should().Be(PreviousContent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public Task WriteAllTextAsync_WhenThePathIsBlank_ThrowsArgumentException(string path)
    {
        // Act
        var write = () => AtomicFileWriter.WriteAllTextAsync(path, NewContent);

        // Assert
        return write.Should().ThrowAsync<ArgumentException>().WithParameterName("path");
    }

    [Fact]
    public Task WriteAsync_WhenTheContentWriterIsNull_ThrowsArgumentNullException()
    {
        // Act
        var write = () => AtomicFileWriter.WriteAsync(_target, null!);

        // Assert
        return write.Should().ThrowAsync<ArgumentNullException>().WithParameterName("writeContent");
    }

    [Fact]
    public void WriteAllText_WhenTheContentIsNull_ThrowsArgumentNullException()
    {
        // Act
        var write = () => AtomicFileWriter.WriteAllText(_target, null!);

        // Assert
        write.Should().Throw<ArgumentNullException>().WithParameterName("contents");
    }
}
