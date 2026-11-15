using System.Diagnostics;
using System.Text;

namespace FileForge.IO;

/// <summary>
/// Runs a command with <see cref="Process"/>: arguments are passed through <see cref="ProcessStartInfo.ArgumentList"/>
/// (no shell, no quoting), output and error are captured, and the process tree is killed on timeout or cancellation.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"The command '{request.FileName}' did not start a process.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(request.Timeout);

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await WriteStandardInputAsync(process, request.StandardInput, timeoutSource.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return new ProcessRunResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            KillProcessTree(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"The command '{request.FileName}' did not exit within {request.Timeout}.");
        }
    }

    private static async Task WriteStandardInputAsync(Process process, string? standardInput, CancellationToken cancellationToken)
    {
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // A command that exits before reading its input closes the pipe; its exit code and error output tell the caller why.
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout and the kill: there is nothing left to stop.
        }
    }
}
