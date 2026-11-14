using System.Runtime.CompilerServices;
using System.Text;

namespace FileForge.IO;

/// <summary>
/// Reads delimited files (CSV, TSV) record by record according to RFC 4180, the format that
/// <see cref="PersistentDelimitedFileWriter.EscapeField"/> writes.
/// </summary>
/// <remarks>
/// <para>
/// A quoted field may hold delimiters, line breaks and doubled quotes. Records end with CRLF, LF or a lone CR. Empty
/// lines carry no record and are skipped, so a one-column record whose value is empty must be written as <c>""</c>;
/// a line of spaces is a record. A quote inside an unquoted field is kept as text; text after the closing quote of a
/// quoted field is appended to the field.
/// </para>
/// <para>The caller owns the <see cref="TextReader"/> and disposes of it; each enumeration reads it once.</para>
/// </remarks>
public static class DelimitedRecordReader
{
    private const int BufferSize = 4096;

    /// <summary>
    /// Reads every record of a delimited text.
    /// </summary>
    /// <param name="reader">Source text.</param>
    /// <param name="delimiter">Field separator; neither a quote nor a line break.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The records, in file order.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="reader"/> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="delimiter"/> is a quote or a line break.</exception>
    /// <exception cref="InvalidDataException">During enumeration, when a quoted field is still open at the end of the text.</exception>
    public static IAsyncEnumerable<DelimitedRecord> ReadRecordsAsync(
        TextReader reader,
        char delimiter = ',',
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (delimiter is '"' or '\r' or '\n')
        {
            throw new ArgumentException("The delimiter cannot be a quote or a line break.", nameof(delimiter));
        }

        return ReadValidatedRecordsAsync(reader, delimiter, cancellationToken);
    }

    private static async IAsyncEnumerable<DelimitedRecord> ReadValidatedRecordsAsync(
        TextReader reader,
        char delimiter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new char[BufferSize];
        var builder = new DelimitedRecordBuilder(delimiter);

        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                if (builder.Accept(buffer[index]) is { } record)
                {
                    // One read can hold thousands of records: the token is checked for each, not only by the reader.
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return record;
                }
            }
        }

        if (builder.Complete() is { } last)
        {
            yield return last;
        }
    }

    private sealed class DelimitedRecordBuilder(char delimiter)
    {
        private readonly List<string> _fields = [];
        private readonly StringBuilder _field = new();
        private FieldState _state = FieldState.Start;
        private bool _recordStarted;
        private bool _previousWasCarriageReturn;
        private int _line = 1;
        private int _recordLine = 1;
        private int _quoteLine = 1;

        private enum FieldState
        {
            Start,
            Unquoted,
            Quoted,
            QuoteInQuoted,
        }

        public DelimitedRecord? Accept(char character)
        {
            // CRLF is one line break: its LF neither counts a line nor ends a record a second time.
            var isLineFeedOfCrLf = character == '\n' && _previousWasCarriageReturn;
            _previousWasCarriageReturn = character == '\r';
            var isLineBreak = character is '\r' or '\n';

            if (_state == FieldState.Quoted)
            {
                AcceptQuoted(character, isLineBreak && !isLineFeedOfCrLf);
                return null;
            }

            if (_state == FieldState.QuoteInQuoted && character == '"')
            {
                _field.Append('"');
                _state = FieldState.Quoted;
                return null;
            }

            if (isLineFeedOfCrLf)
            {
                return null;
            }

            if (isLineBreak)
            {
                _line++;
                return EndRecord();
            }

            StartRecordIfNeeded();
            if (character == delimiter)
            {
                EndField();
            }
            else if (character == '"' && _state == FieldState.Start)
            {
                _quoteLine = _line;
                _state = FieldState.Quoted;
            }
            else
            {
                _field.Append(character);
                _state = FieldState.Unquoted;
            }

            return null;
        }

        public DelimitedRecord? Complete()
        {
            if (_state == FieldState.Quoted)
            {
                throw new InvalidDataException($"The quoted field opened on line {_quoteLine} is not closed before the end of the data.");
            }

            return EndRecord();
        }

        private void AcceptQuoted(char character, bool startsNewLine)
        {
            if (startsNewLine)
            {
                _line++;
            }

            if (character == '"')
            {
                _state = FieldState.QuoteInQuoted;
            }
            else
            {
                _field.Append(character);
            }
        }

        private void StartRecordIfNeeded()
        {
            if (!_recordStarted)
            {
                _recordStarted = true;
                _recordLine = _line;
            }
        }

        private void EndField()
        {
            _fields.Add(_field.ToString());
            _field.Clear();
            _state = FieldState.Start;
        }

        private DelimitedRecord? EndRecord()
        {
            if (!_recordStarted)
            {
                return null;
            }

            EndField();
            var record = new DelimitedRecord(_recordLine, _fields.ToArray().AsReadOnly());
            _fields.Clear();
            _recordStarted = false;
            return record;
        }
    }
}
