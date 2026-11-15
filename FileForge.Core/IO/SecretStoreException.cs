namespace FileForge.IO;

/// <summary>
/// A secret store operation failed. The message names the operation and never contains the secret.
/// </summary>
public class SecretStoreException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public SecretStoreException()
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">Message naming the failing operation.</param>
    public SecretStoreException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and the underlying cause.</summary>
    /// <param name="message">Message naming the failing operation.</param>
    /// <param name="innerException">The underlying cause.</param>
    public SecretStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
