using System.Text;
using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests.IO;

public sealed class PersistentDelimitedFileWriterTests : IAsyncLifetime
{
    private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "FileForge_Delimited_" + Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDirectory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitializeAsync_WhenFileDoesNotExist_WritesHeader()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "test.csv");
        await using var writer = new PersistentDelimitedFileWriter(testFile, "ColA,ColB");

        // Act
        await writer.InitializeAsync();
        await writer.DisposeAsync();

        // Assert
        var content = await File.ReadAllLinesAsync(testFile);
        content.Should().ContainSingle().Which.Should().Be("ColA,ColB");
    }

    [Fact]
    public async Task AppendLineAsync_WhenLinesAdded_AppendsCorrectly()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "test_append.csv");
        await using var writer = new PersistentDelimitedFileWriter(testFile, "Header");
        await writer.InitializeAsync();

        // Act
        await writer.AppendLineAsync("Line1");
        await writer.AppendLineAsync("Line2");
        await writer.DisposeAsync();

        // Assert
        var content = await File.ReadAllLinesAsync(testFile);
        content.Should().Equal("Header", "Line1", "Line2");
    }

    [Fact]
    public async Task InitializeAsync_WhenNoEncodingGiven_StartsFileWithUtf8ByteOrderMark()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "default_encoding.csv");
        await using var writer = new PersistentDelimitedFileWriter(testFile, "ColA");

        // Act
        await writer.InitializeAsync();
        await writer.DisposeAsync();

        // Assert
        var bytes = await File.ReadAllBytesAsync(testFile);
        bytes.Should().StartWith(Utf8ByteOrderMark);
    }

    [Fact]
    public async Task AppendLineAsync_WhenEncodingHasNoByteOrderMark_WritesNdjsonWithoutByteOrderMark()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "audit.ndjson");
        await using var writer = new PersistentDelimitedFileWriter(testFile, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.InitializeAsync();

        // Act
        await writer.AppendLineAsync("{\"host\":\"nas.home.arpa\"}");
        await writer.DisposeAsync();

        // Assert
        var bytes = await File.ReadAllBytesAsync(testFile);
        bytes.Should().Equal(Encoding.UTF8.GetBytes("{\"host\":\"nas.home.arpa\"}" + Environment.NewLine));
    }

    [Fact]
    public async Task AppendLineAsync_WhenEncodingIsGiven_WritesTextInThatEncoding()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "latin1.csv");
        var latin1 = Encoding.Latin1;
        await using var writer = new PersistentDelimitedFileWriter(testFile, "Hôte", latin1);
        await writer.InitializeAsync();

        // Act
        await writer.AppendLineAsync("serveur-é");
        await writer.DisposeAsync();

        // Assert
        var bytes = await File.ReadAllBytesAsync(testFile);
        bytes.Should().Equal(latin1.GetBytes("Hôte" + Environment.NewLine + "serveur-é" + Environment.NewLine));
    }

    [Fact]
    public async Task ReadExistingLinesAsync_WhenEncodingIsGiven_DecodesLinesWithThatEncoding()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "latin1_read.csv");
        var latin1 = Encoding.Latin1;
        await File.WriteAllBytesAsync(testFile, latin1.GetBytes("Hôte\r\nserveur-é\r\n"));
        await using var writer = new PersistentDelimitedFileWriter(testFile, "Hôte", latin1);

        // Act
        var lines = await writer.ReadExistingLinesAsync();

        // Assert
        lines.Should().Equal("serveur-é");
    }

    [Fact]
    public void Constructor_WhenEncodingIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new PersistentDelimitedFileWriter("file.csv", "ColA", encoding: null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("encoding");
    }

    [Fact]
    public async Task ReadExistingLinesAsync_WhenFileHasData_ReturnsOnlyDataLines()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "test_read.csv");
        await File.WriteAllLinesAsync(testFile, ["Header", "Row1", "Row2"]);

        await using var writer = new PersistentDelimitedFileWriter(testFile, "Header");
        await writer.InitializeAsync();

        // Act
        var lines = await writer.ReadExistingLinesAsync();

        // Assert
        lines.Should().Equal("Row1", "Row2");
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("value,with,commas", "\"value,with,commas\"")]
    [InlineData("value \"with\" quotes", "\"value \"\"with\"\" quotes\"")]
    public void EscapeField_WhenCalled_EscapesAsExpected(string input, string expected)
    {
        // Act
        var result = PersistentDelimitedFileWriter.EscapeField(input);

        // Assert
        result.Should().Be(expected);
    }
}
