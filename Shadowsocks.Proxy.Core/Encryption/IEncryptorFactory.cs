namespace Shadowsocks.Encryption;

public interface IEncryptorFactory
{
    IEncryptor Create(string method, string password);
    bool IsRegistered(string method);
}
