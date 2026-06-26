using Shadowsocks.Model;
using Shadowsocks.Util;
using System;
using System.IO;
using System.Text.Json;

namespace Shadowsocks.Services;

public class ConfigPersistenceService : IConfigPersistenceService
{
    private const string ConfigFile = @"gui-config.json";

    public string ConfigFilePath => ConfigFile;

    public Configuration Load()
    {
        return LoadFile(ConfigFile);
    }

    public Configuration LoadFile(string filename)
    {
        Configuration config;
        try
        {
            if (File.Exists(filename))
            {
                var configContent = File.ReadAllText(filename);
                config = Deserialize(configContent);
                if (config != null)
                {
                    return config;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        config = new Configuration();
        config.FixConfiguration();
        return config;
    }

    public void Save(Configuration config)
    {
        if (config.Index >= config.Configs.Count)
        {
            config.Index = config.Configs.Count - 1;
        }
        else if (config.Index < 0)
        {
            config.Index = 0;
        }

        try
        {
            var jsonString = JsonUtils.Serialize(config, true);
            File.WriteAllText(ConfigFile, jsonString);
        }
        catch (IOException e)
        {
            Console.Error.WriteLine(e);
        }
    }

    private static Configuration Deserialize(string configStr)
    {
        try
        {
            var config = JsonSerializer.Deserialize<Configuration>(configStr);
            if (config is not null)
            {
                config.FixConfiguration();
                return config;
            }
        }
        catch
        {
            // ignored
        }
        return null;
    }
}
