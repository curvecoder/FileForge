using System.ComponentModel;
using System.Globalization;

namespace FileForge.IO;

/// <summary>
/// Stores one secret in the freedesktop Secret Service (GNOME Keyring, KWallet) through the <c>secret-tool</c> command
/// (Debian and Ubuntu package <c>libsecret-tools</c>). The secret is written on the standard input of the command,
/// never on its command line. The item is identified by caller-given attributes (for example application and key name).
/// </summary>
/// <remarks>
/// Call <see cref="IsAvailableAsync"/> before choosing this store: it is unavailable when the command is missing
/// or when no keyring session answers (no D-Bus session, locked keyring that waits for a prompt).
/// </remarks>
public sealed class SecretServiceSecretStore : ISecretStore
{
    private const string CommandName = "secret-tool";
    private const int MaxErrorTextLength = 500;

    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] s_availabilityProbe = ["lookup", "fileforge-probe", "availability"];

    private readonly string _label;
    private readonly string[] _attributeArguments;
    private readonly IProcessRunner _runner;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Creates a store that runs <c>secret-tool</c> with a 10-second timeout per command.
    /// </summary>
    /// <param name="label">Human-readable name of the item shown by keyring managers.</param>
    /// <param name="attributes">Attributes identifying the item; at least one, with non-blank names.</param>
    /// <exception cref="ArgumentException">The label is blank, or the attributes are empty or have a blank name.</exception>
    public SecretServiceSecretStore(string label, IReadOnlyDictionary<string, string> attributes)
        : this(label, attributes, new SystemProcessRunner(), s_defaultTimeout)
    {
    }

    /// <summary>
    /// Creates a store with a given process runner and timeout.
    /// </summary>
    /// <param name="label">Human-readable name of the item shown by keyring managers.</param>
    /// <param name="attributes">Attributes identifying the item; at least one, with non-blank names.</param>
    /// <param name="runner">Runs the <c>secret-tool</c> command.</param>
    /// <param name="timeout">Maximum run time of each command; a keyring waiting for a prompt never answers.</param>
    /// <exception cref="ArgumentException">The label is blank, or the attributes are empty or have a blank name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is not positive.</exception>
    internal SecretServiceSecretStore(
        string label,
        IReadOnlyDictionary<string, string> attributes,
        IProcessRunner runner,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        if (attributes.Count == 0)
        {
            throw new ArgumentException("At least one attribute is required to identify the secret.", nameof(attributes));
        }

        var arguments = new List<string>(attributes.Count * 2);
        foreach (var (name, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An attribute name cannot be blank.", nameof(attributes));
            }

            if (value is null)
            {
                throw new ArgumentException($"The value of the attribute '{name}' cannot be null.", nameof(attributes));
            }

            arguments.Add(name);
            arguments.Add(value);
        }

        _label = label;
        _attributeArguments = [.. arguments];
        _runner = runner;
        _timeout = timeout;
    }

    /// <summary>
    /// Checks that <c>secret-tool</c> is installed and that a keyring answers within the timeout.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the check.</param>
    /// <returns><c>true</c> when the store can be used; <c>false</c> when the command is missing, the keyring does not answer or fails.</returns>
    /// <exception cref="OperationCanceledException">The check was cancelled.</exception>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _runner
                .RunAsync(new ProcessRunRequest(CommandName, s_availabilityProbe, null, _timeout), cancellationToken)
                .ConfigureAwait(false);
            return IsSuccessOrNotFound(result);
        }
        catch (Exception ex) when (ex is Win32Exception or TimeoutException)
        {
            // A missing command or a keyring that never answers means the store cannot be used, which is the answer the caller asked for.
            return false;
        }
    }

    /// <inheritdoc />
    /// <exception cref="SecretStoreUnavailableException">The command is missing or did not answer within the timeout.</exception>
    public async Task<string?> ReadSecretAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync("lookup", ["lookup", .. _attributeArguments], null, cancellationToken).ConfigureAwait(false);
        if (!IsSuccessOrNotFound(result))
        {
            throw Failure("lookup", result);
        }

        return result is { ExitCode: 0, StandardOutput.Length: > 0 } ? result.StandardOutput : null;
    }

    /// <inheritdoc />
    /// <exception cref="SecretStoreUnavailableException">The command is missing or did not answer within the timeout.</exception>
    public async Task WriteSecretAsync(string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        string[] arguments = ["store", $"--label={_label}", .. _attributeArguments];
        var result = await RunAsync("store", arguments, secret, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw Failure("store", result);
        }
    }

    /// <inheritdoc />
    /// <exception cref="SecretStoreUnavailableException">The command is missing or did not answer within the timeout.</exception>
    public async Task ClearSecretAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync("clear", ["clear", .. _attributeArguments], null, cancellationToken).ConfigureAwait(false);
        if (!IsSuccessOrNotFound(result))
        {
            throw Failure("clear", result);
        }
    }

    // secret-tool exits with 1 and prints nothing when the item does not exist; any message on stderr means the keyring itself failed.
    private static bool IsSuccessOrNotFound(ProcessRunResult result) =>
        result.ExitCode == 0 || (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StandardError));

    private static SecretStoreException Failure(string operation, ProcessRunResult result)
    {
        // secret-tool never echoes its standard input, so its error text can be quoted; the length stays bounded for the logs.
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? "no message" : result.StandardError.Trim();
        if (detail.Length > MaxErrorTextLength)
        {
            detail = string.Concat(detail.AsSpan(0, MaxErrorTextLength), "...");
        }

        return new SecretStoreException(string.Create(
            CultureInfo.InvariantCulture,
            $"Secret Service '{operation}' failed with exit code {result.ExitCode}: {detail}"));
    }

    private async Task<ProcessRunResult> RunAsync(
        string operation,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _runner
                .RunAsync(new ProcessRunRequest(CommandName, arguments, standardInput, _timeout), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            throw new SecretStoreUnavailableException(
                $"Secret Service '{operation}' failed: '{CommandName}' could not be started (install libsecret-tools).",
                ex);
        }
        catch (TimeoutException ex)
        {
            throw new SecretStoreUnavailableException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Secret Service '{operation}' timed out after {_timeout.TotalSeconds:0.#} s: no keyring session answered."),
                ex);
        }
    }
}
