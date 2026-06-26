using Shadowsocks.Model;

namespace Shadowsocks.Services;

public interface IConfigPersistenceService
{
    Configuration Load();
    Configuration LoadFile(string filename);
    void Save(Configuration config);
    string ConfigFilePath { get; }
}
