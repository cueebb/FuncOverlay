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
        public string anchor { get; set; } = "top-left";
        public string anchorTarget { get; set; } = "screen";
        public string targetAnchor { get; set; } = "top-left";
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
        public bool locked { get; set; } = false;
        public string widgetType { get; set; } = string.Empty;
        public List<PluginSettingDefinition>? settingsSchema { get; set; }
        public Dictionary<string, object?>? settings { get; set; }
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

                        if (meta != null)
                        {
                            meta.position ??= new PluginPosition();
                            meta.position.anchor = NormalizeAnchor(meta.position.anchor);
                            meta.position.anchorTarget = string.IsNullOrWhiteSpace(meta.position.anchorTarget)
                                ? "screen"
                                : meta.position.anchorTarget.Trim();
                            meta.position.targetAnchor = NormalizeAnchor(
                                string.IsNullOrWhiteSpace(meta.position.targetAnchor)
                                    ? meta.position.anchor
                                    : meta.position.targetAnchor);

                            string folderName = Path.GetFileName(folderPath);
                            LoadedPluginFolders.Add(new PluginFolderItem
                            {
                                FolderName = folderName,
                                FolderPath = folderPath,
                                Metadata = meta
                            });

                            if (meta.enabled)
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

        public bool SetPluginEnabled(string folderName, bool enabled)
        {
            try
            {
                string metadataPath = Path.Combine(PluginsDirectory, folderName, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    string json = File.ReadAllText(metadataPath);
                    var meta = JsonSerializer.Deserialize<PluginMetadata>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (meta != null)
                    {
                        meta.enabled = enabled;
                        string updatedJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(metadataPath, updatedJson);
                        LoadAllPlugins();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update plugin enabled state: {ex.Message}");
            }
            return false;
        }

        public bool SetPluginLocked(string folderName, bool locked)
        {
            try
            {
                string metadataPath = Path.Combine(PluginsDirectory, folderName, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    string json = File.ReadAllText(metadataPath);
                    var meta = JsonSerializer.Deserialize<PluginMetadata>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (meta != null)
                    {
                        meta.locked = locked;
                        string updatedJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(metadataPath, updatedJson);
                        var loaded = FindFolderByPluginId(folderName);
                        if (loaded != null) loaded.Metadata.locked = locked;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update plugin locked state: {ex.Message}");
            }
            return false;
        }

        public static readonly string[] AnchorOptions = new[]
        {
            "top-left", "top-center", "top-right",
            "center-left", "center", "center-right",
            "bottom-left", "bottom-center", "bottom-right"
        };

        public static string NormalizeAnchor(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "top-left";
            string s = raw.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            return s switch
            {
                "top-left" or "left-top" or "upper-left" or "left-upper" => "top-left",
                "top-center" or "center-top" or "upper-center" or "top" => "top-center",
                "top-right" or "right-top" or "upper-right" or "right-upper" => "top-right",
                "center-left" or "left-center" or "middle-left" or "left" => "center-left",
                "center" or "middle" or "center-center" => "center",
                "center-right" or "right-center" or "middle-right" or "right" => "center-right",
                "bottom-left" or "left-bottom" or "lower-left" or "left-lower" => "bottom-left",
                "bottom-center" or "center-bottom" or "lower-center" or "bottom" => "bottom-center",
                "bottom-right" or "right-bottom" or "lower-right" or "right-lower" => "bottom-right",
                _ => "top-left"
            };
        }

        public bool InstallFromPath(string sourcePath)
        {
            if (Directory.Exists(sourcePath))
            {
                return CopyPluginDirectory(sourcePath);
            }
            if (File.Exists(sourcePath) && Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractPluginArchive(sourcePath);
            }
            return false;
        }

        public bool CopyPluginDirectory(string sourceDir)
        {
            try
            {
                string folderName = new DirectoryInfo(sourceDir).Name;
                string targetDir = Path.Combine(PluginsDirectory, folderName);
                if (Directory.Exists(targetDir))
                {
                    targetDir = Path.Combine(PluginsDirectory, $"{folderName}_{DateTime.Now:yyyyMMddHHmmss}");
                }

                CopyDirectoryRecursive(sourceDir, targetDir);
                LoadAllPlugins();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to copy plugin directory: {ex.Message}");
                return false;
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSubDir);
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

        public bool UpdatePluginPosition(
            string pluginId,
            int x,
            int y,
            string? anchor = null,
            string? anchorTarget = null,
            string? targetAnchor = null)
        {
            try
            {
                foreach (var folder in LoadedPluginFolders)
                {
                    if (string.Equals(folder.Metadata.id, pluginId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(folder.FolderName, pluginId, StringComparison.OrdinalIgnoreCase))
                    {
                        string metadataPath = Path.Combine(folder.FolderPath, "metadata.json");
                        if (!File.Exists(metadataPath)) return false;

                        string json = File.ReadAllText(metadataPath);
                        var meta = JsonSerializer.Deserialize<PluginMetadata>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (meta != null)
                        {
                            meta.position ??= new PluginPosition();
                            meta.position.x = x;
                            meta.position.y = y;
                            if (anchor != null) meta.position.anchor = NormalizeAnchor(anchor);
                            if (anchorTarget != null)
                            {
                                meta.position.anchorTarget = string.IsNullOrWhiteSpace(anchorTarget)
                                    ? "screen"
                                    : anchorTarget.Trim();
                            }
                            if (targetAnchor != null) meta.position.targetAnchor = NormalizeAnchor(targetAnchor);

                            string updatedJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                            File.WriteAllText(metadataPath, updatedJson);

                            folder.Metadata.position ??= new PluginPosition();
                            folder.Metadata.position.x = meta.position.x;
                            folder.Metadata.position.y = meta.position.y;
                            folder.Metadata.position.anchor = NormalizeAnchor(meta.position.anchor);
                            folder.Metadata.position.anchorTarget = meta.position.anchorTarget;
                            folder.Metadata.position.targetAnchor = NormalizeAnchor(meta.position.targetAnchor);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update plugin position: {ex.Message}");
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
                    var meta = JsonSerializer.Deserialize<PluginMetadata>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (meta != null)
                    {
                        meta.settings ??= new Dictionary<string, object?>();
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

        public PluginFolderItem? FindFolderByPluginId(string pluginId)
        {
            foreach (var folder in LoadedPluginFolders)
            {
                if (string.Equals(folder.Metadata.id, pluginId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(folder.FolderName, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    return folder;
                }
            }
            return null;
        }

        public static Dictionary<string, object?> GetMergedSettings(PluginMetadata meta)
        {
            var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (meta.settingsSchema != null)
            {
                foreach (var def in meta.settingsSchema)
                {
                    if (string.IsNullOrEmpty(def.key)) continue;

                    object? chosen = def.value ?? def.defaultValue;
                    if (meta.settings != null && meta.settings.TryGetValue(def.key, out object? dictVal) && dictVal != null)
                    {
                        string defDefaultStr = def.defaultValue?.ToString() ?? string.Empty;
                        string schemaValStr = def.value?.ToString() ?? string.Empty;
                        string dictValStr = dictVal.ToString() ?? string.Empty;

                        bool dictChanged = !string.Equals(dictValStr, defDefaultStr, StringComparison.Ordinal);
                        bool schemaChanged = !string.Equals(schemaValStr, defDefaultStr, StringComparison.Ordinal);

                        if (dictChanged || !schemaChanged)
                        {
                            chosen = dictVal;
                        }
                    }

                    merged[def.key] = chosen;
                }
            }

            if (meta.settings != null)
            {
                foreach (var kv in meta.settings)
                {
                    if (!merged.ContainsKey(kv.Key))
                    {
                        merged[kv.Key] = kv.Value;
                    }
                }
            }

            return merged;
        }
    }
}
