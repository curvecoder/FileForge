namespace FileForge.IO;

/// <summary>
/// Reads, writes and deletes one small secret (a token, a password) in a platform-specific store.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Reads the stored secret.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The secret, or <c>null</c> when none is stored.</returns>
    /// <exception cref="SecretStoreException">The store failed to answer.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    Task<string?> ReadSecretAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the secret, replacing any previous one.
    /// </summary>
    /// <param name="secret">Non-empty secret to store.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="secret"/> is null, empty or whitespace.</exception>
    /// <exception cref="SecretStoreException">The store refused or failed the write.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    Task WriteSecretAsync(string secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the stored secret; does nothing when none is stored.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <exception cref="SecretStoreException">The store failed to delete.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    Task ClearSecretAsync(CancellationToken cancellationToken = default);
}
