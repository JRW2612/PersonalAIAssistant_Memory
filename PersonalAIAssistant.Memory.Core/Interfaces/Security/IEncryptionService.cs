namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    public interface IEncryptionService
    {
        string Encrypt(string plainText, string key);
        string Decrypt(string cipherText, string key);
    }
}
