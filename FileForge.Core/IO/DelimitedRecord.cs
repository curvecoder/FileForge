namespace FileForge.IO;

/// <summary>
/// One record read from a delimited file (CSV, TSV).
/// </summary>
/// <param name="LineNumber">One-based physical line on which the record starts; a quoted field holding line breaks makes the next record start several lines further.</param>
/// <param name="Fields">Unescaped field values, in file order.</param>
public sealed record DelimitedRecord(int LineNumber, IReadOnlyList<string> Fields);
