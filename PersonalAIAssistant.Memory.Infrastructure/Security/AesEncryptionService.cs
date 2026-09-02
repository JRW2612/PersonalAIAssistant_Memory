using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using System.Security.Cryptography;
using System.Text;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    public class AesEncryptionService : IEncryptionService
    {
        public string Encrypt(string plainText, string key)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            var keyBytes = DeriveKey(key);
            using var aes = Aes.Create();
            aes.Key = keyBytes;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();

            // Prepend IV to the stream
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string cipherText, string key)
        {
            if (string.IsNullOrEmpty(cipherText)) return string.Empty;
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            var keyBytes = DeriveKey(key);
            var fullCipher = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = keyBytes;

            var iv = new byte[aes.BlockSize / 8];
            var cipherBytes = new byte[fullCipher.Length - iv.Length];

            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipherBytes, 0, cipherBytes.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(cipherBytes);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);

            return sr.ReadToEnd();
        }

        private static byte[] DeriveKey(string key)
        {
            using var derive = new Rfc2898DeriveBytes(key, new byte[] { 0x1, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x8 }, 1000, HashAlgorithmName.SHA256);
            return derive.GetBytes(32);
        }
    }
}
