using System.Text;

namespace FileForge.IO;

/// <summary>
/// Replaces the content of a file so that a reader, a crash or a power cut finds either the previous content or the new one.
/// </summary>
/// <remarks>
/// <para>
/// A write holds <c>&lt;file&gt;.lock</c>, opened without sharing, from start to end: a second write or a recovery of
/// the same file meanwhile fails with an <see cref="IOException"/>. Windows deletes the lock once released; on Linux
/// and macOS, where the lock is advisory, the <c>.lock</c> file stays beside the file. The new content is written to
/// <c>&lt;file&gt;.tmp</c> and flushed to disk, then renamed <c>&lt;file&gt;.pending</c>, which then replaces the file.
/// On Windows the replacement keeps the access control list and attributes of the file; elsewhere the new content
/// takes the permissions of the file. A symbolic link is followed: the file it points to is replaced and the link is
/// kept; a link cycle throws an <see cref="IOException"/>. A hard link is not kept.
/// </para>
/// <para>
/// A <c>.tmp</c> file is a content that was never committed; a <c>.pending</c> file is a complete one that has not
/// replaced the file yet. <see cref="RecoverInterruptedWrite"/> settles both, and should run before the file is read
/// after a crash. In the rare case where Windows removes the file but cannot rename the new content, the file is
/// missing until the next write or recovery, and the new content is in <c>.pending</c>.
/// </para>
/// <para>
/// These files are created beside the file: its directory must be writable, and a file mounted on its own (a
/// container bind mount) cannot be replaced by renaming.
/// </para>
/// </remarks>
public static class AtomicFileWriter
{
    private const string LockSuffix = ".lock";
    private const string TemporarySuffix = ".tmp";
    private const string PendingSuffix = ".pending";

    private static readonly Encoding DefaultEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Replaces the content of a file with what <paramref name="writeContent"/> writes.
    /// </summary>
    /// <param name="path">File to replace; created with its directory when missing.</param>
    /// <param name="writeContent">Writes the new content to the stream it receives; the stream is flushed and closed afterwards.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that ends when the file holds the new content.</returns>
    /// <exception cref="ArgumentException">When <paramref name="path"/> is empty or blank.</exception>
    /// <exception cref="ArgumentNullException">When <paramref name="writeContent"/> is null.</exception>
    /// <exception cref="IOException">
    /// When another write or a recovery of the same file is in progress, or when the file was not replaced: if the new
    /// content could not be committed the file is unchanged; if the file could not be replaced (on Windows, while
    /// another process holds it open), the new content is kept in <c>&lt;file&gt;.pending</c> and the message names it.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">When the directory of the file is not writable.</exception>
    public static Task WriteAsync(string path, Func<Stream, CancellationToken, Task> writeContent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeContent);

        return WriteValidatedAsync(path, writeContent, cancellationToken);
    }

    /// <summary>
    /// Replaces the content of a file with a text.
    /// </summary>
    /// <param name="path">File to replace; created with its directory when missing.</param>
    /// <param name="contents">New content.</param>
    /// <param name="encoding">Encoding of the text; UTF-8 without byte order mark when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that ends when the file holds the new content.</returns>
    /// <exception cref="ArgumentException">When <paramref name="path"/> is empty or blank.</exception>
    /// <exception cref="ArgumentNullException">When <paramref name="contents"/> is null.</exception>
    /// <exception cref="IOException">As for <see cref="WriteAsync"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">When the directory of the file is not writable.</exception>
    public static Task WriteAllTextAsync(string path, string contents, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        return WriteValidatedAsync(
            path,
            async (stream, token) =>
            {
                var writer = new StreamWriter(stream, encoding ?? DefaultEncoding, leaveOpen: true);
                await using (writer.ConfigureAwait(false))
                {
                    await writer.WriteAsync(contents.AsMemory(), token).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Replaces the content of a file with a text.
    /// </summary>
    /// <param name="path">File to replace; created with its directory when missing.</param>
    /// <param name="contents">New content.</param>
    /// <param name="encoding">Encoding of the text; UTF-8 without byte order mark when null.</param>
    /// <exception cref="ArgumentException">When <paramref name="path"/> is empty or blank.</exception>
    /// <exception cref="ArgumentNullException">When <paramref name="contents"/> is null.</exception>
    /// <exception cref="IOException">As for <see cref="WriteAsync"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">When the directory of the file is not writable.</exception>
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        var target = ResolveTarget(path);
        using var writeLock = AcquireWriteLock(target);
        var temporary = target + TemporarySuffix;
        var committed = false;
        try
        {
            using (var stream = CreateTemporaryFile(target, temporary, FileOptions.None))
            {
                using (var writer = new StreamWriter(stream, encoding ?? DefaultEncoding, leaveOpen: true))
                {
                    writer.Write(contents);
                }

                stream.Flush(flushToDisk: true);
            }

            Commit(target, temporary);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                DiscardTemporaryFile(temporary);
            }
        }
    }

    /// <summary>
    /// Completes or discards a write interrupted beside a file: a complete new content replaces the file, an
    /// uncommitted one is deleted.
    /// </summary>
    /// <param name="path">File whose interrupted write is completed or discarded.</param>
    /// <returns>What was found.</returns>
    /// <exception cref="ArgumentException">When <paramref name="path"/> is empty or blank.</exception>
    /// <exception cref="IOException">When a write of the file is in progress, or the file cannot be replaced.</exception>
    /// <exception cref="UnauthorizedAccessException">When the directory of the file is not writable.</exception>
    public static InterruptedWriteRecovery RecoverInterruptedWrite(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var target = ResolveTarget(path);
        var temporary = target + TemporarySuffix;
        var pending = target + PendingSuffix;
        if (!File.Exists(temporary) && !File.Exists(pending))
        {
            return new InterruptedWriteRecovery(PendingContentApplied: false, IncompleteContentDiscarded: false);
        }

        using var writeLock = AcquireWriteLock(target);
        var incompleteDiscarded = File.Exists(temporary);
        if (incompleteDiscarded)
        {
            File.Delete(temporary);
        }

        var pendingApplied = File.Exists(pending);
        if (pendingApplied)
        {
            Replace(target, pending);
        }

        return new InterruptedWriteRecovery(pendingApplied, incompleteDiscarded);
    }

    private static async Task WriteValidatedAsync(string path, Func<Stream, CancellationToken, Task> writeContent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ResolveTarget(path);
        using (AcquireWriteLock(target))
        {
            var temporary = target + TemporarySuffix;
            var committed = false;
            try
            {
                var stream = CreateTemporaryFile(target, temporary, FileOptions.Asynchronous);
                await using (stream.ConfigureAwait(false))
                {
                    await writeContent(stream, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                Commit(target, temporary);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    DiscardTemporaryFile(temporary);
                }
            }
        }
    }

    private static string ResolveTarget(string path)
    {
        var file = new FileInfo(path);

        // Renaming over a symbolic link would replace the link with a plain file and leave the linked file unchanged.
        return file.LinkTarget is null ? file.FullName : file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName;
    }

    private static WriteLock AcquireWriteLock(string target)
    {
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lockPath = target + LockSuffix;
        try
        {
            // The lock covers the whole write, renames included: the .tmp and .pending names are shared by every
            // writer of the file, so a second writer or a recovery running meanwhile would take or delete this content.
            return new WriteLock(lockPath, new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1));
        }
        catch (IOException ex)
        {
            throw new IOException($"'{target}' could not be locked through '{lockPath}'; another write may be in progress: {ex.Message}", ex);
        }
    }

    private static FileStream CreateTemporaryFile(string target, string temporary, FileOptions options)
    {
        var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, options);
        if (OperatingSystem.IsWindows() || !File.Exists(target))
        {
            return stream;
        }

        try
        {
            // Renaming gives the file the permissions of the new content: a file readable by its owner only would
            // otherwise become readable by everyone. Set on the open file, the mode escapes the umask and also applies
            // to a .tmp left by a crash. Windows keeps the access control list through File.Replace.
            File.SetUnixFileMode(stream.SafeFileHandle, File.GetUnixFileMode(target));
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void DiscardTemporaryFile(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The exception that stopped the write matters more to the caller than this one: a .tmp left behind is
            // overwritten by the next write or deleted by the next recovery, and never replaces the file.
        }
    }

    private static void Commit(string target, string temporary)
    {
        var pending = target + PendingSuffix;
        try
        {
            File.Move(temporary, pending, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"'{target}' is unchanged: its new content could not be renamed '{pending}': {ex.Message}", ex);
        }

        Replace(target, pending);
    }

    private sealed class WriteLock(string lockPath, FileStream stream) : IDisposable
    {
        public void Dispose()
        {
            stream.Dispose();
            if (!OperatingSystem.IsWindows())
            {
                // On Linux and macOS the lock is advisory and bound to the file, not to its name: deleting the name
                // would let a writer that opened it just before keep a lock that a writer creating a new one would
                // not see. The .lock file therefore stays.
                return;
            }

            try
            {
                File.Delete(lockPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another writer opened the lock right after its release and holds it: that writer deletes it in turn.
            }
        }
    }

    private static void Replace(string target, string pending)
    {
        try
        {
            if (File.Exists(target))
            {
                File.Replace(pending, target, destinationBackupFileName: null);
            }
            else
            {
                File.Move(pending, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"'{target}' could not be replaced, so its new content is kept in '{pending}' until the next write or " +
                $"recovery: {ex.Message}",
                ex);
        }
    }
}
