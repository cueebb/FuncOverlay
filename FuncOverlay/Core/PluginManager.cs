using System;
using System.IO;
using System.IO.Compression;
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

    public class PluginSettingDefinition
    {
        public string key { get; set; } = string.Empty;
        public string label { get; set; } = string.Empty;
        public string type { get; set; } = "string"; // "string", "int", "bool", "select"
        public object? defaultValue { get; set; }
        public object? value { get; set; }
        public List<string>? options { get; set; }
        public string? description { get; set; }
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
        public List<PluginSettingDefinition>? settingsSchema { get; set; }
        public Dictionary<string, object>? settings { get; set; }
    }

    public class PluginFolderItem
    {
        public string FolderName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public PluginMetadata Metadata { get; set; } = new PluginMetadata();
    }

    public class PluginManager
    {
        public string PluginsDirectory { get; private set; }
        public List<PluginFolderItem> LoadedPluginFolders { get; private set; } = new List<PluginFolderItem>();
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
            LoadedPluginFolders.Clear();
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
                        var meta = JsonSerializer.Deserialize<PluginMetadata>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (meta != null && meta.enabled)
                        {
                            string folderName = Path.GetFileName(folderPath);
                            LoadedPluginFolders.Add(new PluginFolderItem
                            {
                                FolderName = folderName,
                                FolderPath = folderPath,
                                Metadata = meta
                            });
                            LoadedPlugins.Add(meta);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to load plugin at {folderPath}: {ex.Message}");
                    }
                }
            }
        }

        public bool ExtractPluginArchive(string zipFilePath)
        {
            try
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(zipFilePath);
                string targetDir = Path.Combine(PluginsDirectory, fileNameWithoutExt);
                
                if (Directory.Exists(targetDir))
                {
                    targetDir = Path.Combine(PluginsDirectory, $"{fileNameWithoutExt}_{DateTime.Now:yyyyMMddHHmmss}");
                }

                Directory.CreateDirectory(targetDir);
                ZipFile.ExtractToDirectory(zipFilePath, targetDir, overwriteFiles: true);
                LoadAllPlugins();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to extract plugin zip: {ex.Message}");
                return false;
            }
        }

        public bool DeletePlugin(string folderName)
        {
            try
            {
                string folderPath = Path.Combine(PluginsDirectory, folderName);
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, recursive: true);
                    LoadAllPlugins();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete plugin: {ex.Message}");
            }
            return false;
        }

        public bool UpdatePluginSetting(string folderName, string settingKey, object? newValue)
        {
            try
            {
                string metadataPath = Path.Combine(PluginsDirectory, folderName, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    string json = File.ReadAllText(metadataPath);
                    var meta = JsonSerializer.Deserialize<PluginMetadata>(json);
                    if (meta != null)
                    {
                        meta.settings ??= new Dictionary<string, object>();
                        meta.settings[settingKey] = newValue;

                        if (meta.settingsSchema != null)
                        {
                            foreach (var def in meta.settingsSchema)
                            {
                                if (def.key == settingKey)
                                {
                                    def.value = newValue;
                                }
                            }
                        }

                        string updatedJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(metadataPath, updatedJson);
                        LoadAllPlugins();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update plugin setting: {ex.Message}");
            }
            return false;
        }
    }
}
