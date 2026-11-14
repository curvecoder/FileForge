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
    public async Task AppendLineAsync_WhenExistingFileIsReopened_DoesNotWriteAnotherByteOrderMark()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "reopened.csv");
        await using (var firstWriter = new PersistentDelimitedFileWriter(testFile, "Host"))
        {
            await firstWriter.InitializeAsync();
            await firstWriter.AppendLineAsync("nas.home.arpa");
        }

        await using var writer = new PersistentDelimitedFileWriter(testFile, "Host");
        await writer.InitializeAsync();

        // Act
        await writer.AppendLineAsync("printer.home.arpa");
        await writer.DisposeAsync();

        // Assert
        var bytes = await File.ReadAllBytesAsync(testFile);
        var expected = Utf8ByteOrderMark.Concat(Encoding.UTF8.GetBytes(
            "Host" + Environment.NewLine + "nas.home.arpa" + Environment.NewLine + "printer.home.arpa" + Environment.NewLine));
        bytes.Should().Equal(expected);
    }

    [Fact]
    public async Task ReadExistingLinesAsync_WhenFileWasWrittenInUtf16_ReturnsTheAppendedLines()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "utf16.csv");
        await using (var firstWriter = new PersistentDelimitedFileWriter(testFile, "Host", Encoding.Unicode))
        {
            await firstWriter.InitializeAsync();
            await firstWriter.AppendLineAsync("nas.home.arpa");
        }

        await using var writer = new PersistentDelimitedFileWriter(testFile, "Host", Encoding.Unicode);
        await writer.InitializeAsync();
        await writer.AppendLineAsync("printer.home.arpa");

        // Act
        var lines = await writer.ReadExistingLinesAsync();

        // Assert
        lines.Should().Equal("nas.home.arpa", "printer.home.arpa");
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
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReadExistingLinesAsync_WhenHeaderLineIsBlank_ReturnsFirstRecord(string headerLine)
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "audit.ndjson");
        await using (var firstWriter = new PersistentDelimitedFileWriter(testFile, headerLine, new UTF8Encoding(false)))
        {
            await firstWriter.InitializeAsync();
            await firstWriter.AppendLineAsync("{\"host\":\"nas.home.arpa\"}");
            await firstWriter.AppendLineAsync("{\"host\":\"printer.home.arpa\"}");
        }

        await using var writer = new PersistentDelimitedFileWriter(testFile, headerLine, new UTF8Encoding(false));

        // Act
        var lines = await writer.ReadExistingLinesAsync();

        // Assert
        lines.Should().Equal("{\"host\":\"nas.home.arpa\"}", "{\"host\":\"printer.home.arpa\"}");
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

    [Fact]
    public void EscapeField_WhenNeutralizationIsNotRequested_KeepsLeadingFormulaCharacter()
    {
        // Act
        var result = PersistentDelimitedFileWriter.EscapeField("=HYPERLINK(\"http://nas.home.arpa\")", ',');

        // Assert
        result.Should().Be("\"=HYPERLINK(\"\"http://nas.home.arpa\"\")\"");
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd|' /C calc'!A0", "'+cmd|' /C calc'!A0")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1:A2)", "'@SUM(A1:A2)")]
    [InlineData("\tcalc", "'\tcalc")]
    [InlineData("\rcalc", "\"'\rcalc\"")]
    [InlineData("＝1+1", "'＝1+1")]
    [InlineData("＋1", "'＋1")]
    [InlineData("－1", "'－1")]
    [InlineData("＠SUM(A1)", "'＠SUM(A1)")]
    public void EscapeField_WhenNeutralizationIsRequestedAndFieldStartsWithFormulaCharacter_PrefixesApostrophe(string input, string expected)
    {
        // Act
        var result = PersistentDelimitedFileWriter.EscapeField(input, ',', neutralizeFormulas: true);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void EscapeField_WhenNeutralizedFieldHoldsDelimiter_QuotesTheField()
    {
        // Act
        var result = PersistentDelimitedFileWriter.EscapeField("=A1;B1", ';', neutralizeFormulas: true);

        // Assert
        result.Should().Be("\"'=A1;B1\"");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("a=b", "a=b")]
    [InlineData("value,with,commas", "\"value,with,commas\"")]
    public void EscapeField_WhenNeutralizationIsRequestedAndFieldDoesNotStartWithFormulaCharacter_EscapesAsBefore(string input, string expected)
    {
        // Act
        var result = PersistentDelimitedFileWriter.EscapeField(input, ',', neutralizeFormulas: true);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task EscapeField_WhenNeutralizedFieldIsReadBack_ReturnsPrefixedValue()
    {
        // Arrange
        var line = PersistentDelimitedFileWriter.EscapeField("=1+1", ',', neutralizeFormulas: true) + ","
            + PersistentDelimitedFileWriter.EscapeField("@a,b", ',', neutralizeFormulas: true);
        using var reader = new StringReader(line);

        // Act
        var records = new List<DelimitedRecord>();
        await foreach (var record in DelimitedRecordReader.ReadRecordsAsync(reader))
        {
            records.Add(record);
        }

        // Assert
        records.Should().ContainSingle().Which.Fields.Should().Equal("'=1+1", "'@a,b");
    }
}
