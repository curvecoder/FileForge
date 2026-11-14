namespace FileForge.IO;

/// <summary>
/// What <see cref="AtomicFileWriter.RecoverInterruptedWrite"/> found beside a file.
/// </summary>
/// <param name="PendingContentApplied">A complete new content was waiting and now replaces the file.</param>
/// <param name="IncompleteContentDiscarded">An incomplete new content was left by an interrupted write and is deleted; the file keeps its previous content.</param>
public sealed record InterruptedWriteRecovery(bool PendingContentApplied, bool IncompleteContentDiscarded);
