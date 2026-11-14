using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests.IO;

public sealed class DelimitedRecordReaderTests
{
    [Fact]
    public async Task ReadRecordsAsync_WhenAQuotedFieldSpansLines_ReturnsOneRecordHoldingTheLineBreaks()
    {
        // Arrange
        const string text = "java,\"-jar app.jar\r\n--port 8080\",192.168.1.30\r\nsshd,-D,192.168.1.31\r\n";

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Should().HaveCount(2);
        records[0].Fields.Should().Equal("java", "-jar app.jar\r\n--port 8080", "192.168.1.30");
        records[0].LineNumber.Should().Be(1);
        records[1].Fields.Should().Equal("sshd", "-D", "192.168.1.31");
        records[1].LineNumber.Should().Be(3);
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenAQuotedFieldHoldsQuotesAndDelimiters_UnescapesThem()
    {
        // Arrange
        const string text = "\"say \"\"hi\"\", then leave\",next";

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Should().ContainSingle().Which.Fields.Should().Equal("say \"hi\", then leave", "next");
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenRecordsEndWithLineFeedOrCarriageReturn_SplitsOnEach()
    {
        // Arrange
        const string text = "a\nb\rc\r\nd";

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Select(record => record.Fields.Single()).Should().Equal("a", "b", "c", "d");
        records.Select(record => record.LineNumber).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenALineIsEmpty_SkipsItAndKeepsCountingLines()
    {
        // Arrange
        const string text = "a,b\r\n\r\nc,d\r\n";

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Should().HaveCount(2);
        records[1].Fields.Should().Equal("c", "d");
        records[1].LineNumber.Should().Be(3);
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenFieldsAreEmpty_KeepsThem()
    {
        // Arrange
        const string text = ",\"\",\r\n";

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Should().ContainSingle().Which.Fields.Should().Equal(string.Empty, string.Empty, string.Empty);
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenAQuoteIsInsideAnUnquotedField_KeepsItAsText()
    {
        // Arrange
        const string text = "say \"hi\",next";

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Should().ContainSingle().Which.Fields.Should().Equal("say \"hi\"", "next");
    }

    [Fact]
    public Task ReadRecordsAsync_WhenAQuoteIsNeverClosed_ThrowsNamingTheLineWhereItOpens()
    {
        // Arrange
        const string text = "a,b\r\nc,\"-jar app.jar\r\n--port 8080\r\n";

        // Act
        var read = () => ReadAllAsync(text);

        // Assert
        return read.Should().ThrowAsync<InvalidDataException>().WithMessage("*line 2*");
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenTheDelimiterIsASemicolon_SplitsOnItOnly()
    {
        // Arrange
        const string text = "a,b;\"c;d\"";

        // Act
        var records = await ReadAllAsync(text, ';');

        // Assert
        records.Should().ContainSingle().Which.Fields.Should().Equal("a,b", "c;d");
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenFieldsWereEscapedByTheWriter_ReturnsTheOriginalValues()
    {
        // Arrange
        string[] values = ["plain", "with,comma", "with \"quotes\"", "two\nlines", "two\r\nlines", string.Empty];
        var text = string.Join(',', values.Select(value => PersistentDelimitedFileWriter.EscapeField(value)));

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Should().ContainSingle().Which.Fields.Should().Equal(values);
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenTheTextIsLongerThanTheReadBuffer_ReadsEveryRecord()
    {
        // Arrange
        var lines = Enumerable.Range(0, 2000).Select(index => $"192.168.1.{index % 250},\"host\r\n{index}\"").ToList();
        var text = string.Join("\r\n", lines);

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Should().HaveCount(2000);
        records[^1].Fields.Should().Equal("192.168.1.249", "host\r\n1999");
        records[^1].LineNumber.Should().Be(3999);
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenALineBreakOrADoubledQuoteStraddlesTheReadBuffer_ReadsThemWhole()
    {
        // Arrange
        var filler = new string('x', 4095);
        var text = filler + "\r\n" + "\"a\r\nb\"\r\n" + new string('y', 4084) + ",\"\"\"q\"\r\nlast";

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Select(record => record.Fields.ToList()).Should().BeEquivalentTo(
            new List<List<string>> { new() { filler }, new() { "a\r\nb" }, new() { new string('y', 4084), "\"q" }, new() { "last" } },
            options => options.WithStrictOrdering());
        records.Select(record => record.LineNumber).Should().Equal(1, 2, 4, 5);
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenAQuotedFieldHoldsALoneCarriageReturn_CountsItAsALine()
    {
        // Arrange
        const string text = "\"a\rb\",c\r\r\nd";

        // Act
        var records = await ReadAllAsync(text);

        // Assert
        records.Should().HaveCount(2);
        records[0].Fields.Should().Equal("a\rb", "c");
        records[1].LineNumber.Should().Be(4);
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenTheOnlyFieldIsAnEmptyQuotedValue_ReturnsAnEmptyField()
    {
        // Act
        var records = await ReadAllAsync("\"\"\r\n\"a\"");

        // Assert
        records.Select(record => record.Fields.Single()).Should().Equal(string.Empty, "a");
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenALineHoldsOnlySpaces_ReturnsItAsARecord()
    {
        // Act
        var records = await ReadAllAsync("  \r\na");

        // Assert
        records.Select(record => record.Fields.Single()).Should().Equal("  ", "a");
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenTextFollowsAClosingQuote_AppendsItToTheField()
    {
        // Act
        var records = await ReadAllAsync("\"ab\"cd,e");

        // Assert
        records.Should().ContainSingle().Which.Fields.Should().Equal("abcd", "e");
    }

    [Fact]
    public Task ReadRecordsAsync_WhenAQuoteOpensAfterAMultilineField_ThrowsNamingTheLineOfThatQuote()
    {
        // Arrange
        const string text = "\"a\r\nb\",\"c\r\nd";

        // Act
        var read = () => ReadAllAsync(text);

        // Assert
        return read.Should().ThrowAsync<InvalidDataException>().WithMessage("*line 2*");
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenFieldsWereEscapedForASemicolon_ReturnsTheOriginalValues()
    {
        // Arrange
        string[] values = ["a;b", "c,d", "e\nf"];
        var text = string.Join(';', values.Select(value => PersistentDelimitedFileWriter.EscapeField(value, ';')));

        // Act
        var records = await ReadAllAsync(text, ';');

        // Assert
        records.Should().ContainSingle().Which.Fields.Should().Equal(values);
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenCancelledAfterTheFirstRecord_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var enumerator = DelimitedRecordReader.ReadRecordsAsync(new StringReader("a\r\nb\r\nc"), cancellationToken: cancellation.Token)
            .GetAsyncEnumerator();
        await using var _ = enumerator.ConfigureAwait(false);
        await enumerator.MoveNextAsync();
        await cancellation.CancelAsync();

        // Act
        var next = async () => await enumerator.MoveNextAsync();

        // Assert
        await next.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenTheTextIsEmpty_ReturnsNoRecord()
    {
        // Act
        var records = await ReadAllAsync(string.Empty);

        // Assert
        records.Should().BeEmpty();
    }

    [Theory]
    [InlineData('"')]
    [InlineData('\r')]
    [InlineData('\n')]
    public void ReadRecordsAsync_WhenTheDelimiterIsAQuoteOrALineBreak_ThrowsArgumentException(char delimiter)
    {
        // Act
        var read = () => DelimitedRecordReader.ReadRecordsAsync(new StringReader("a"), delimiter);

        // Assert
        read.Should().Throw<ArgumentException>().WithParameterName("delimiter");
    }

    [Fact]
    public void ReadRecordsAsync_WhenTheReaderIsNull_ThrowsArgumentNullException()
    {
        // Act
        var read = () => DelimitedRecordReader.ReadRecordsAsync(null!);

        // Assert
        read.Should().Throw<ArgumentNullException>().WithParameterName("reader");
    }

    [Fact]
    public async Task ReadRecordsAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var read = async () =>
        {
            await foreach (var _ in DelimitedRecordReader.ReadRecordsAsync(new StringReader("a\r\nb"), cancellationToken: cancellation.Token))
            {
            }
        };

        // Assert
        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ReadRecords_WhenAQuotedFieldSpansLines_ReturnsOneRecordHoldingTheLineBreaks()
    {
        // Arrange
        const string text = "java,\"-jar app.jar\r\n--port 8080\",192.168.1.30\r\nsshd,-D,192.168.1.31\r\n";

        // Act
        var records = DelimitedRecordReader.ReadRecords(new StringReader(text)).ToList();

        // Assert
        records.Should().HaveCount(2);
        records[0].Fields.Should().Equal("java", "-jar app.jar\r\n--port 8080", "192.168.1.30");
        records[0].LineNumber.Should().Be(1);
        records[1].Fields.Should().Equal("sshd", "-D", "192.168.1.31");
        records[1].LineNumber.Should().Be(3);
    }

    [Theory]
    [InlineData("a\nb\rc\r\nd", ',')]
    [InlineData("\r\n\r\na,b\r\n\r\nc", ',')]
    [InlineData("\"say \"\"hi\"\", then leave\",next", ',')]
    [InlineData("a;\"b;c\";\"d\"\"e\"\r\n;;", ';')]
    [InlineData("\"\"\r\n   \r\n\"a\"tail,x", ',')]
    [InlineData("", ',')]
    public async Task ReadRecords_WhenGivenTheSameText_ReturnsTheRecordsOfReadRecordsAsync(string text, char delimiter)
    {
        // Arrange
        var expected = await ReadAllAsync(text, delimiter);

        // Act
        var records = DelimitedRecordReader.ReadRecords(new StringReader(text), delimiter).ToList();

        // Assert
        records.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ReadRecords_WhenALineBreakOrADoubledQuoteStraddlesTheReadBuffer_ReadsThemWhole()
    {
        // Arrange: CR at index 4095 and LF at 4096; the doubled quote at 8191 and 8192 (the read buffer holds 4096 characters).
        var firstLine = new string('x', 4095);
        var secondLineStart = new string('y', 4091);
        var text = firstLine + "\r\n" + secondLineStart + ",\"c\"\"d\"";

        // Act
        var records = DelimitedRecordReader.ReadRecords(new StringReader(text)).ToList();

        // Assert
        text.Substring(8191, 2).Should().Be("\"\"");
        records.Should().HaveCount(2);
        records[0].Fields.Should().Equal(firstLine);
        records[1].Fields.Should().Equal(secondLineStart, "c\"d");
        records.Select(record => record.LineNumber).Should().Equal(1, 2);
    }

    [Fact]
    public void ReadRecords_WhenAQuoteIsNeverClosed_ThrowsDuringEnumerationNamingTheLineWhereItOpens()
    {
        // Arrange
        var records = DelimitedRecordReader.ReadRecords(new StringReader("a,b\r\nc,\"-jar app.jar\r\n--port 8080\r\n"));

        // Act
        var read = () => records.ToList();

        // Assert
        read.Should().Throw<InvalidDataException>().WithMessage("*line 2*");
    }

    [Theory]
    [InlineData('"')]
    [InlineData('\r')]
    [InlineData('\n')]
    public void ReadRecords_WhenTheDelimiterIsAQuoteOrALineBreak_ThrowsArgumentExceptionBeforeEnumeration(char delimiter)
    {
        // Act
        var read = () => DelimitedRecordReader.ReadRecords(new StringReader("a"), delimiter);

        // Assert
        read.Should().Throw<ArgumentException>().WithParameterName("delimiter");
    }

    [Fact]
    public void ReadRecords_WhenTheReaderIsNull_ThrowsArgumentNullExceptionBeforeEnumeration()
    {
        // Act
        var read = () => DelimitedRecordReader.ReadRecords(null!);

        // Assert
        read.Should().Throw<ArgumentNullException>().WithParameterName("reader");
    }

    private static async Task<List<DelimitedRecord>> ReadAllAsync(string text, char delimiter = ',')
    {
        var records = new List<DelimitedRecord>();
        await foreach (var record in DelimitedRecordReader.ReadRecordsAsync(new StringReader(text), delimiter))
        {
            records.Add(record);
        }

        return records;
    }
}
