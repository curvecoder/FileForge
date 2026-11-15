namespace FileForge.IO;

/// <summary>
/// The secret store cannot be used on this machine (command missing, no keyring session, locked keyring, no answer in time).
/// </summary>
public sealed class SecretStoreUnavailableException : SecretStoreException
{
    /// <summary>Initializes a new instance.</summary>
    public SecretStoreUnavailableException()
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">Message naming the failing operation.</param>
    public SecretStoreUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and the underlying cause.</summary>
    /// <param name="message">Message naming the failing operation.</param>
    /// <param name="innerException">The underlying cause.</param>
    public SecretStoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
