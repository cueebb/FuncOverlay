using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Diagnostics;

namespace FuncOverlay.Core
{
    public class PluginPosition
    {
        public int x { get; set; } = 100;
        public int y { get; set; } = 100;
    }

    public class PluginSize
    {
        public int width { get; set; } = 320;
        public int height { get; set; } = 220;
    }

    public class PluginMetadata
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public string version { get; set; } = "1.0.0";
        public string author { get; set; } = "Author";
        public string entryPoint { get; set; } = "main.py"; // "main.py" or "plugin.dll"
        public string language { get; set; } = "python"; // "python" or "csharp"
        public PluginPosition position { get; set; } = new PluginPosition();
        public PluginSize size { get; set; } = new PluginSize();
        public bool enabled { get; set; } = true;
    }

    public class PluginManager
    {
        public string PluginsDirectory { get; private set; }
        public List<PluginMetadata> LoadedPlugins { get; private set; } = new List<PluginMetadata>();

        public PluginManager()
        {
            // {FuncOverlay_folder}/plugins/
            PluginsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            if (!Directory.Exists(PluginsDirectory))
            {
                Directory.CreateDirectory(PluginsDirectory);
            }
        }

        public void LoadAllPlugins()
        {
            LoadedPlugins.Clear();
            if (!Directory.Exists(PluginsDirectory)) return;

            string[] subdirectories = Directory.GetDirectories(PluginsDirectory);
            foreach (string folderPath in subdirectories)
            {
                string metadataPath = Path.Combine(folderPath, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    try
                    {
                        string json = File.ReadAllText(metadataPath);
                        var meta = JsonSerializer.Deserialize<PluginMetadata>(json);
                        if (meta != null && meta.enabled)
                        {
                            // Verify presence of main file: main.py or plugin.dll
                            string mainFile = Path.Combine(folderPath, meta.entryPoint);
                            if (File.Exists(mainFile))
                            {
                                LoadedPlugins.Add(meta);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to load plugin at {folderPath}: {ex.Message}");
                    }
                }
            }
        }
    }
}
