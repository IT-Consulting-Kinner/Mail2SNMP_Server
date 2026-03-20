using System.Security.Cryptography;
using Mail2SNMP.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// AES-256-GCM implementation of <see cref="ICredentialEncryptor"/>. Uses a 12-byte random nonce and 16-byte GCM authentication tag.
/// </summary>
public class AesGcmCredentialEncryptor : ICredentialEncryptor
{
    private readonly byte[] _masterKey;
    private readonly ILogger<AesGcmCredentialEncryptor> _logger;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public AesGcmCredentialEncryptor(byte[] masterKey, ILogger<AesGcmCredentialEncryptor> logger)
    {
        if (masterKey.Length != 32)
            throw new ArgumentException("Master key must be 256 bits (32 bytes).");
        _masterKey = masterKey;
        _logger = logger;
    }

    /// <summary>
    /// Encrypts a plaintext string and returns the result as a Base64 string containing nonce, ciphertext, and tag.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Format: nonce + ciphertext + tag
        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + ciphertext.Length);

        CryptographicOperations.ZeroMemory(plaintextBytes);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts a Base64-encoded ciphertext produced by <see cref="Encrypt"/> back to the original plaintext.
    /// </summary>
    public string Decrypt(string ciphertextBase64)
    {
        var data = Convert.FromBase64String(ciphertextBase64);
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Invalid ciphertext format.");

        var nonce = data[..NonceSize];
        var ciphertext = data[NonceSize..^TagSize];
        var tag = data[^TagSize..];

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        var result = System.Text.Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        return result;
    }

    /// <summary>
    /// Attempts to decrypt the ciphertext, returning <c>false</c> if decryption fails (e.g. wrong master key).
    /// </summary>
    public bool TryDecrypt(string ciphertextBase64, out string plaintext)
    {
        try
        {
            plaintext = Decrypt(ciphertextBase64);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt credential. Master key may not match.");
            plaintext = string.Empty;
            return false;
        }
    }
}
