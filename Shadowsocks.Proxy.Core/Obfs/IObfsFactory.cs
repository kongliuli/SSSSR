namespace Shadowsocks.Obfs;

public interface IObfsFactory
{
    IObfs Create(string name, string encryptMethod, string encryptPassword);
    bool IsRegistered(string name);
}
