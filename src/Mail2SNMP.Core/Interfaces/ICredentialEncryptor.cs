namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Provides AES-256-GCM encryption and decryption for sensitive credentials
/// (mailbox passwords, SNMP auth/priv passwords, webhook secrets).
/// All credentials are stored in encrypted form; the master key is managed separately.
/// </summary>
public interface ICredentialEncryptor
{
    /// <summary>
    /// Encrypts a plaintext credential and returns a Base64-encoded ciphertext
    /// containing nonce + ciphertext + GCM tag.
    /// </summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a Base64-encoded ciphertext back to plaintext.
    /// Throws <see cref="System.Security.Cryptography.CryptographicException"/> on failure.
    /// </summary>
    string Decrypt(string ciphertext);

    /// <summary>
    /// Attempts to decrypt a ciphertext. Returns false if the master key does not match
    /// or the data is corrupt, without throwing an exception.
    /// </summary>
    bool TryDecrypt(string ciphertext, out string plaintext);

    /// <summary>
    /// J1: Idempotent encrypt. If <paramref name="value"/> already decodes as valid
    /// AES-GCM ciphertext under the current master key it is returned unchanged;
    /// otherwise it is treated as plaintext and encrypted. Empty/null is returned as
    /// empty so callers can blindly route untrusted input from the UI through this
    /// helper without leaking plaintext to the database.
    /// </summary>
    string EnsureEncrypted(string? value);
}
