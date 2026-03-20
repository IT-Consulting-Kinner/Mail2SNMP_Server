using System.Security.Cryptography;
using Mail2SNMP.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mail2SNMP.Tests.Infrastructure;

public class AesGcmCredentialEncryptorTests
{
    private static byte[] GenerateKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    [Fact]
    public void EncryptDecrypt_Roundtrip()
    {
        var encryptor = new AesGcmCredentialEncryptor(GenerateKey(), NullLogger<AesGcmCredentialEncryptor>.Instance);
        var plaintext = "MySuperSecretPassword!123";
        var encrypted = encryptor.Encrypt(plaintext);
        var decrypted = encryptor.Decrypt(encrypted);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        var encryptor = new AesGcmCredentialEncryptor(GenerateKey(), NullLogger<AesGcmCredentialEncryptor>.Instance);
        var encrypted1 = encryptor.Encrypt("same-password");
        var encrypted2 = encryptor.Encrypt("same-password");
        Assert.NotEqual(encrypted1, encrypted2); // Different nonces
    }

    [Fact]
    public void Decrypt_WrongKey_Fails()
    {
        var key1 = GenerateKey();
        var key2 = GenerateKey();
        var enc1 = new AesGcmCredentialEncryptor(key1, NullLogger<AesGcmCredentialEncryptor>.Instance);
        var enc2 = new AesGcmCredentialEncryptor(key2, NullLogger<AesGcmCredentialEncryptor>.Instance);

        var encrypted = enc1.Encrypt("secret");
        Assert.ThrowsAny<CryptographicException>(() => enc2.Decrypt(encrypted));
    }

    [Fact]
    public void TryDecrypt_WrongKey_ReturnsFalse()
    {
        var key1 = GenerateKey();
        var key2 = GenerateKey();
        var enc1 = new AesGcmCredentialEncryptor(key1, NullLogger<AesGcmCredentialEncryptor>.Instance);
        var enc2 = new AesGcmCredentialEncryptor(key2, NullLogger<AesGcmCredentialEncryptor>.Instance);

        var encrypted = enc1.Encrypt("secret");
        Assert.False(enc2.TryDecrypt(encrypted, out var result));
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void TryDecrypt_ValidKey_ReturnsTrue()
    {
        var key = GenerateKey();
        var encryptor = new AesGcmCredentialEncryptor(key, NullLogger<AesGcmCredentialEncryptor>.Instance);
        var encrypted = encryptor.Encrypt("test");
        Assert.True(encryptor.TryDecrypt(encrypted, out var result));
        Assert.Equal("test", result);
    }

    [Fact]
    public void Constructor_InvalidKeySize_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new AesGcmCredentialEncryptor(new byte[16], NullLogger<AesGcmCredentialEncryptor>.Instance));
    }

    [Fact]
    public void EncryptDecrypt_EmptyString()
    {
        var encryptor = new AesGcmCredentialEncryptor(GenerateKey(), NullLogger<AesGcmCredentialEncryptor>.Instance);
        var encrypted = encryptor.Encrypt("");
        var decrypted = encryptor.Decrypt(encrypted);
        Assert.Equal("", decrypted);
    }

    [Fact]
    public void EncryptDecrypt_Unicode()
    {
        var encryptor = new AesGcmCredentialEncryptor(GenerateKey(), NullLogger<AesGcmCredentialEncryptor>.Instance);
        var plaintext = "Passwort mit Ümläuten: äöü ß €";
        var encrypted = encryptor.Encrypt(plaintext);
        var decrypted = encryptor.Decrypt(encrypted);
        Assert.Equal(plaintext, decrypted);
    }
}
