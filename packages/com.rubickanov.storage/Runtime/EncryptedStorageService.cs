using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;

namespace Rubickanov.Storage
{
    public class EncryptedStorageService : IStorageService
    {
        private const int KeySize = 32;
        private const int IVSize = 16;
        private const int SaltSize = 16;
        private const int Iterations = 100_000;

        private readonly IStorageService _inner;
        private readonly byte[] _key;

        public EncryptedStorageService(IStorageService inner, string passphrase)
        {
            _inner = inner;

            var salt = DeriveStableSalt(passphrase);
            using var deriveBytes = new Rfc2898DeriveBytes(
                passphrase, salt, Iterations, HashAlgorithmName.SHA256);
            _key = deriveBytes.GetBytes(KeySize);
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            var raw = _inner.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) return defaultValue;

            var decrypted = Decrypt(raw);
            if (decrypted == null) return defaultValue;

            return float.TryParse(decrypted, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : defaultValue;
        }

        public UniTask SetFloat(string key, float value)
        {
            var encrypted = Encrypt(value.ToString(CultureInfo.InvariantCulture));
            return _inner.SetString(key, encrypted);
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            var raw = _inner.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) return defaultValue;

            var decrypted = Decrypt(raw);
            if (decrypted == null) return defaultValue;

            return int.TryParse(decrypted, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v
                : defaultValue;
        }

        public UniTask SetInt(string key, int value)
        {
            var encrypted = Encrypt(value.ToString(CultureInfo.InvariantCulture));
            return _inner.SetString(key, encrypted);
        }

        public string GetString(string key, string defaultValue = "")
        {
            var raw = _inner.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) return defaultValue;

            var decrypted = Decrypt(raw);
            return decrypted ?? defaultValue;
        }

        public UniTask SetString(string key, string value)
        {
            var encrypted = Encrypt(value);
            return _inner.SetString(key, encrypted);
        }

        public bool HasKey(string key) => _inner.HasKey(key);

        public UniTask DeleteKey(string key) => _inner.DeleteKey(key);

        private string Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            var result = new byte[IVSize + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, IVSize);
            Buffer.BlockCopy(cipherBytes, 0, result, IVSize, cipherBytes.Length);

            return Convert.ToBase64String(result);
        }

        private string? Decrypt(string base64)
        {
            try
            {
                var data = Convert.FromBase64String(base64);
                if (data.Length < IVSize + 1) return null;

                var iv = new byte[IVSize];
                Buffer.BlockCopy(data, 0, iv, 0, IVSize);

                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                var plainBytes = decryptor.TransformFinalBlock(data, IVSize, data.Length - IVSize);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static byte[] DeriveStableSalt(string passphrase)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(passphrase));
            var salt = new byte[SaltSize];
            Buffer.BlockCopy(hash, 0, salt, 0, SaltSize);
            return salt;
        }
    }
}
