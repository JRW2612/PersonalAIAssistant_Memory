namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    public interface IEncryptionService
    {
        string Encrypt(string plainText, string key);
        string Decrypt(string cipherText, string key);
    }
}
