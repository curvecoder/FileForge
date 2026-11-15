namespace FileForge.IO;

/// <summary>
/// Runs an external command without a shell and captures its output.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>
    /// Starts the command, writes the optional standard input, and waits for it to exit.
    /// </summary>
    /// <param name="request">Command, arguments, standard input and timeout.</param>
    /// <param name="cancellationToken">Token that cancels the run; the process is then killed.</param>
    /// <returns>The exit code and the captured output.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">The command could not be started (missing or not executable).</exception>
    /// <exception cref="TimeoutException">The command did not exit within <see cref="ProcessRunRequest.Timeout"/> and was killed.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled and the process killed.</exception>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// A command to run. Arguments are passed one by one, never as a shell string.
/// </summary>
/// <param name="FileName">Command name (resolved on PATH) or full path.</param>
/// <param name="Arguments">Arguments, each passed as a separate argument.</param>
/// <param name="StandardInput">Text written to standard input then closed, or <c>null</c> for none.</param>
/// <param name="Timeout">Maximum run time before the process is killed.</param>
internal sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput,
    TimeSpan Timeout)
{
    // The standard input carries the secret to store: it must never reach a log through ToString.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"FileName = {FileName}, ArgumentCount = {Arguments.Count}, Timeout = {Timeout}");
        return true;
    }
}

/// <summary>
/// The outcome of a finished command.
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="StandardOutput">Captured standard output.</param>
/// <param name="StandardError">Captured standard error.</param>
internal sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError)
{
    // The standard output carries the secret read by a lookup: it must never reach a log through ToString.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"ExitCode = {ExitCode}");
        return true;
    }
}
