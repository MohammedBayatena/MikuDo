using System.Security.Cryptography;
using System.Text;

namespace MikuDo.Services;

public class EncryptionService
{
    private byte[]? _derivedKey;
    private byte[]? _encryptionSalt;
    private const int Iterations = 100_000;
    private const int SaltSize = 32;

    public bool IsUnlocked => _derivedKey != null;

    public void UnlockVault(string password, string encSaltBase64)
    {
        _encryptionSalt = Convert.FromBase64String(encSaltBase64);
        _derivedKey = DeriveKey(password, _encryptionSalt);
    }

    public void LockVault()
    {
        if (_derivedKey != null)
        {
            CryptographicOperations.ZeroMemory(_derivedKey);
            _derivedKey = null;
        }
        _encryptionSalt = null;
    }

    public (string ciphertext, string iv) Encrypt(string plaintext)
    {
        if (_derivedKey == null) throw new InvalidOperationException("Vault is locked");

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _derivedKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return (Convert.ToBase64String(cipherBytes), Convert.ToBase64String(aes.IV));
    }

    public string Decrypt(string ciphertext, string iv)
    {
        if (_derivedKey == null) throw new InvalidOperationException("Vault is locked");

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _derivedKey;
        aes.IV = Convert.FromBase64String(iv);

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = Convert.FromBase64String(ciphertext);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    public static (string hash, string salt, string encSalt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var encSalt = RandomNumberGenerator.GetBytes(SaltSize);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations,
            HashAlgorithmName.SHA256, 32);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), Convert.ToBase64String(encSalt));
    }

    public static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        var salt = Convert.FromBase64String(storedSalt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations,
            HashAlgorithmName.SHA256, 32);

        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(storedHash));
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations,
            HashAlgorithmName.SHA256, 32);
    }
}
