import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { PluginFolder, PluginMetadata, PluginSettingDefinition } from '../types/plugin';

interface AppContextType {
  isOverlayOpen: boolean;
  isFading: boolean;
  runOnStartup: boolean;
  overlayOpacity: number;
  fadeDurationMs: number;
  hotkey: string;
  theme: 'dark' | 'light';
  plugins: PluginFolder[];
  isSettingsOpen: boolean;
  isSettingsMinimized: boolean;
  isExplorerOpen: boolean;
  isCsharpSpecOpen: boolean;
  activeSettingsTab: 'general' | 'hotkey' | 'plugins' | 'csharpDev';
  selectedExplorerFolder: string | null;
  toastMessage: string | null;
  toggleOverlay: () => void;
  setOverlayOpen: (open: boolean) => void;
  setRunOnStartup: (enabled: boolean) => void;
  setHotkey: (key: string) => void;
  setOverlayOpacity: (opacity: number) => void;
  setFadeDurationMs: (ms: number) => void;
  setTheme: (theme: 'dark' | 'light') => void;
  openSettings: (tab?: 'general' | 'hotkey' | 'plugins' | 'csharpDev') => void;
  minimizeSettings: () => void;
  closeSettings: () => void;
  openExplorer: (folderName?: string) => void;
  closeExplorer: () => void;
  openCsharpSpec: () => void;
  closeCsharpSpec: () => void;
  updatePluginPosition: (id: string, x: number, y: number) => void;
  updatePluginSize: (id: string, width: number, height: number) => void;
  updatePluginMetadata: (id: string, newMetadata: Partial<PluginMetadata>) => void;
  updatePluginSetting: (id: string, settingKey: string, newValue: any) => void;
  togglePluginEnabled: (id: string) => void;
  deletePlugin: (id: string) => void;
  addSamplePlugin: (widgetType: 'clock' | 'sysinfo' | 'notes' | 'volume', language?: 'python' | 'csharp') => void;
  addCustomPluginFolder: (folderName: string, language: 'python' | 'csharp', name: string, description: string) => void;
  installPluginArchive: (pluginName?: string) => void;
  saveMetadataJsonFile: (folderName: string, jsonString: string) => boolean;
  clearAllPlugins: () => void;
  restartApp: () => void;
  showToast: (message: string) => void;
}

const AppContext = createContext<AppContextType | undefined>(undefined);

const STORAGE_KEY_PLUGINS = 'funcoverlay_plugins_v1';
const STORAGE_KEY_HOTKEY = 'funcoverlay_hotkey_v2'; // Bumped for Alt+Space default
const STORAGE_KEY_OPACITY = 'funcoverlay_opacity_v1';
const STORAGE_KEY_FADE = 'funcoverlay_fade_v1';
const STORAGE_KEY_THEME = 'funcoverlay_theme_v1';
const STORAGE_KEY_STARTUP = 'funcoverlay_startup_v1';

export const AppProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [isOverlayOpen, setIsOverlayOpenState] = useState<boolean>(true);
  const [isFading, setIsFading] = useState<boolean>(false);
  const [runOnStartup, setRunOnStartupState] = useState<boolean>(false);
  const [overlayOpacity, setOverlayOpacityState] = useState<number>(0.40);
  const [fadeDurationMs, setFadeDurationMsState] = useState<number>(250); // 250ms (0.25s)
  const [hotkey, setHotkeyState] = useState<string>('Alt+Space'); // Default: Alt+Space
  const [theme, setThemeState] = useState<'dark' | 'light'>('dark');
  const [plugins, setPlugins] = useState<PluginFolder[]>([]);
  const [isSettingsOpen, setIsSettingsOpen] = useState<boolean>(false);
  const [isSettingsMinimized, setIsSettingsMinimized] = useState<boolean>(false);
  const [isExplorerOpen, setIsExplorerOpen] = useState<boolean>(false);
  const [isCsharpSpecOpen, setIsCsharpSpecOpen] = useState<boolean>(false);
  const [activeSettingsTab, setActiveSettingsTab] = useState<'general' | 'hotkey' | 'plugins' | 'csharpDev'>('general');
  const [selectedExplorerFolder, setSelectedExplorerFolder] = useState<string | null>(null);
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  // Load initial settings and plugins from localStorage
  useEffect(() => {
    try {
      const savedHotkey = localStorage.getItem(STORAGE_KEY_HOTKEY);
      if (savedHotkey && savedHotkey !== 'Alt+Enter') {
        setHotkeyState(savedHotkey);
      } else {
        setHotkeyState('Alt+Space');
        localStorage.setItem(STORAGE_KEY_HOTKEY, 'Alt+Space');
      }

      const savedOpacity = localStorage.getItem(STORAGE_KEY_OPACITY);
      if (savedOpacity !== null) setOverlayOpacityState(parseFloat(savedOpacity));

      const savedFade = localStorage.getItem(STORAGE_KEY_FADE);
      if (savedFade !== null) setFadeDurationMsState(parseInt(savedFade, 10));

      const savedTheme = localStorage.getItem(STORAGE_KEY_THEME);
      if (savedTheme === 'light' || savedTheme === 'dark') setThemeState(savedTheme);

      const savedStartup = localStorage.getItem(STORAGE_KEY_STARTUP);
      if (savedStartup === 'true') setRunOnStartupState(true);

      const savedPlugins = localStorage.getItem(STORAGE_KEY_PLUGINS);
      if (savedPlugins) {
        setPlugins(JSON.parse(savedPlugins));
      }
    } catch (e) {
      console.error('Failed to load storage', e);
    }
  }, []);

  const showToast = useCallback((msg: string) => {
    setToastMessage(msg);
    setTimeout(() => {
      setToastMessage((curr) => (curr === msg ? null : curr));
    }, 3000);
  }, []);

  const toggleOverlay = useCallback(() => {
    setIsFading(true);
    setIsOverlayOpenState((prev) => !prev);
    setTimeout(() => {
      setIsFading(false);
    }, fadeDurationMs);
  }, [fadeDurationMs]);

  const setOverlayOpen = useCallback((open: boolean) => {
    setIsFading(true);
    setIsOverlayOpenState(open);
    setTimeout(() => {
      setIsFading(false);
    }, fadeDurationMs);
  }, [fadeDurationMs]);

  const setHotkey = useCallback((key: string) => {
    setHotkeyState(key);
    localStorage.setItem(STORAGE_KEY_HOTKEY, key);
    showToast(`Hotkey set to ${key}`);
  }, [showToast]);

  const setOverlayOpacity = useCallback((opacity: number) => {
    setOverlayOpacityState(opacity);
    localStorage.setItem(STORAGE_KEY_OPACITY, opacity.toString());
  }, []);

  const setFadeDurationMs = useCallback((ms: number) => {
    setFadeDurationMsState(ms);
    localStorage.setItem(STORAGE_KEY_FADE, ms.toString());
  }, []);

  const setTheme = useCallback((t: 'dark' | 'light') => {
    setThemeState(t);
    localStorage.setItem(STORAGE_KEY_THEME, t);
    if (t === 'dark') {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }, []);

  const setRunOnStartup = useCallback((enabled: boolean) => {
    setRunOnStartupState(enabled);
    localStorage.setItem(STORAGE_KEY_STARTUP, enabled ? 'true' : 'false');
    showToast(enabled ? 'Run on startup enabled' : 'Run on startup disabled');
  }, [showToast]);

  const openSettings = useCallback((tab: 'general' | 'hotkey' | 'plugins' | 'csharpDev' = 'general') => {
    setActiveSettingsTab(tab);
    setIsSettingsMinimized(false);
    setIsSettingsOpen(true);
  }, []);

  const minimizeSettings = useCallback(() => {
    setIsSettingsMinimized(true);
    showToast('Settings minimized (reopen from tray)');
  }, [showToast]);

  const closeSettings = useCallback(() => {
    setIsSettingsOpen(false);
    setIsSettingsMinimized(false);
  }, []);

  const openExplorer = useCallback((folderName?: string) => {
    if (folderName) setSelectedExplorerFolder(folderName);
    setIsExplorerOpen(true);
  }, []);

  const closeExplorer = useCallback(() => {
    setIsExplorerOpen(false);
  }, []);

  const openCsharpSpec = useCallback(() => {
    setIsCsharpSpecOpen(true);
  }, []);

  const closeCsharpSpec = useCallback(() => {
    setIsCsharpSpecOpen(false);
  }, []);

  const updatePluginPosition = useCallback((id: string, x: number, y: number) => {
    setPlugins((prev) => {
      const updated = prev.map((pf) => {
        if (pf.metadata.id === id) {
          const newMeta = { ...pf.metadata, position: { x: Math.round(x), y: Math.round(y) } };
          const updatedFiles = pf.files.map((file) => {
            if (file.isMetadata) {
              return { ...file, content: JSON.stringify(newMeta, null, 2) };
            }
            return file;
          });
          return { ...pf, metadata: newMeta, files: updatedFiles };
        }
        return pf;
      });
      localStorage.setItem(STORAGE_KEY_PLUGINS, JSON.stringify(updated));
      return updated;
    });
  }, []);

  const updatePluginSize = useCallback((id: string, width: number, height: number) => {
    setPlugins((prev) => {
      const updated = prev.map((pf) => {
        if (pf.metadata.id === id) {
          const newMeta = { ...pf.metadata, size: { width: Math.round(width), height: Math.round(height) } };
          const updatedFiles = pf.files.map((file) => {
            if (file.isMetadata) {
              return { ...file, content: JSON.stringify(newMeta, null, 2) };
            }
            return file;
          });
          return { ...pf, metadata: newMeta, files: updatedFiles };
        }
        return pf;
      });
      localStorage.setItem(STORAGE_KEY_PLUGINS, JSON.stringify(updated));
      return updated;
    });
  }, []);

  const updatePluginMetadata = useCallback((id: string, newMetadata: Partial<PluginMetadata>) => {
    setPlugins((prev) => {
      const updated = prev.map((pf) => {
        if (pf.metadata.id === id) {
          const mergedMeta = { ...pf.metadata, ...newMetadata };
          const updatedFiles = pf.files.map((file) => {
            if (file.isMetadata) {
              return { ...file, content: JSON.stringify(mergedMeta, null, 2) };
            }
            return file;
          });
          return { ...pf, metadata: mergedMeta, files: updatedFiles };
        }
        return pf;
      });
      localStorage.setItem(STORAGE_KEY_PLUGINS, JSON.stringify(updated));
      return updated;
    });
  }, []);

  // Update specific setting defined in plugin's metadata.json (string, int, bool, select)
  const updatePluginSetting = useCallback((id: string, settingKey: string, newValue: any) => {
    setPlugins((prev) => {
      const updated = prev.map((pf) => {
        if (pf.metadata.id === id) {
          const currentSettings = { ...(pf.metadata.settings || {}) };
          currentSettings[settingKey] = newValue;

          const updatedSchema = (pf.metadata.settingsSchema || []).map((def) => {
            if (def.key === settingKey) {
              return { ...def, value: newValue };
            }
            return def;
          });

          const mergedMeta: PluginMetadata = {
            ...pf.metadata,
            settings: currentSettings,
            settingsSchema: updatedSchema,
          };

          const updatedFiles = pf.files.map((file) => {
            if (file.isMetadata) {
              return { ...file, content: JSON.stringify(mergedMeta, null, 2) };
            }
            return file;
          });

          return { ...pf, metadata: mergedMeta, files: updatedFiles };
        }
        return pf;
      });
      localStorage.setItem(STORAGE_KEY_PLUGINS, JSON.stringify(updated));
      return updated;
    });
    showToast(`Updated setting '${settingKey}'`);
  }, [showToast]);

  const togglePluginEnabled = useCallback((id: string) => {
    setPlugins((prev) => {
      const updated = prev.map((pf) => {
        if (pf.metadata.id === id) {
          const enabled = !pf.metadata.enabled;
          const mergedMeta = { ...pf.metadata, enabled };
          const updatedFiles = pf.files.map((file) => {
            if (file.isMetadata) {
              return { ...file, content: JSON.stringify(mergedMeta, null, 2) };
            }
            return file;
          });
          return { ...pf, metadata: mergedMeta, files: updatedFiles };
        }
        return pf;
      });
      localStorage.setItem(STORAGE_KEY_PLUGINS, JSON.stringify(updated));
      return updated;
    });
  }, []);

  const deletePlugin = useCallback((id: string) => {
    setPlugins((prev) => {
      const updated = prev.filter((pf) => pf.metadata.id !== id);
      localStorage.setItem(STORAGE_KEY_PLUGINS, JSON.stringify(updated));
      return updated;
    });
    showToast('Plugin removed from plugins folder');
  }, [showToast]);

  const clearAllPlugins = useCallback(() => {
    setPlugins([]);
    localStorage.removeItem(STORAGE_KEY_PLUGINS);
    showToast('Plugins folder cleared');
  }, [showToast]);

  const addSamplePlugin = useCallback((widgetType: 'clock' | 'sysinfo' | 'notes' | 'volume', language: 'python' | 'csharp' = 'python') => {
    const id = `plugin_${widgetType}_${Date.now().toString(36)}`;
    const folderName = widgetType === 'clock' ? 'ClockWidget' : widgetType === 'sysinfo' ? 'SystemMonitor' : widgetType === 'notes' ? 'QuickNotes' : 'VolumeMixer';
    const isPython = language === 'python';
    const entryPoint = isPython ? 'main.py' : 'plugin.dll';

    const existingCount = plugins.length;
    const x = 80 + (existingCount * 40) % 600;
    const y = 80 + (existingCount * 50) % 400;

    const names = {
      clock: 'Digital Clock Widget',
      sysinfo: 'System Hardware Monitor',
      notes: 'Overlay Quick Notes',
      volume: 'Audio Volume Mixer',
    };

    const descriptions = {
      clock: 'Sleek digital clock and date display with configurable formatting.',
      sysinfo: 'Real-time CPU and system performance monitor.',
      notes: 'Quick scratchpad notes module overlay.',
      volume: 'Fast volume slider mixer for active Windows audio streams.',
    };

    // Rich settings schema demonstrating string, int, bool, and select types
    const defaultSettingsSchema: Record<string, PluginSettingDefinition[]> = {
      clock: [
        {
          key: 'show_seconds',
          label: 'Show Seconds',
          type: 'bool',
          defaultValue: true,
          value: true,
          description: 'Toggle display of seconds counter.',
        },
        {
          key: 'time_format',
          label: 'Time Format',
          type: 'select',
          defaultValue: '24-hour',
          value: '24-hour',
          options: ['24-hour', '12-hour AM/PM'],
          description: 'Clock hour display format.',
        },
        {
          key: 'header_text',
          label: 'Header Text',
          type: 'string',
          defaultValue: 'SYSTEM TIME',
          value: 'SYSTEM TIME',
          description: 'Label rendered above the digital time.',
        },
        {
          key: 'update_interval_ms',
          label: 'Update Interval (ms)',
          type: 'int',
          defaultValue: 1000,
          value: 1000,
          description: 'Tick refresh interval in milliseconds.',
        },
      ],
      sysinfo: [
        {
          key: 'monitor_cpu',
          label: 'Monitor CPU',
          type: 'bool',
          defaultValue: true,
          value: true,
          description: 'Enable CPU usage gauge.',
        },
        {
          key: 'polling_rate_ms',
          label: 'Polling Rate (ms)',
          type: 'int',
          defaultValue: 2000,
          value: 2000,
          description: 'Sensor polling frequency.',
        },
        {
          key: 'device_name',
          label: 'Device Tag',
          type: 'string',
          defaultValue: 'Core-0',
          value: 'Core-0',
          description: 'Custom hardware label.',
        },
      ],
      notes: [
        {
          key: 'auto_save',
          label: 'Auto Save Notes',
          type: 'bool',
          defaultValue: true,
          value: true,
          description: 'Persist notes automatically on keystroke.',
        },
        {
          key: 'font_size',
          label: 'Font Size (px)',
          type: 'int',
          defaultValue: 12,
          value: 12,
          description: 'Editor font size.',
        },
        {
          key: 'note_title',
          label: 'Default Note Title',
          type: 'string',
          defaultValue: 'Quick Scratchpad',
          value: 'Quick Scratchpad',
          description: 'Title header of the note.',
        },
      ],
      volume: [
        {
          key: 'mute_on_overlay_hide',
          label: 'Mute On Overlay Hide',
          type: 'bool',
          defaultValue: false,
          value: false,
          description: 'Automatically mute audio when overlay closes.',
        },
        {
          key: 'step_size',
          label: 'Volume Step Size',
          type: 'int',
          defaultValue: 5,
          value: 5,
          description: 'Slider percentage step.',
        },
      ],
    };

    const schema = defaultSettingsSchema[widgetType] || [];
    const settingsMap = schema.reduce((acc, s) => ({ ...acc, [s.key]: s.value }), {});

    const metadata: PluginMetadata = {
      id,
      name: names[widgetType],
      description: descriptions[widgetType],
      version: '1.0.0',
      author: isPython ? 'PythonDev' : 'DotNetDev',
      entryPoint: entryPoint as any,
      language,
      position: { x, y },
      size: { width: widgetType === 'sysinfo' ? 360 : 320, height: widgetType === 'notes' ? 260 : 220 },
      enabled: true,
      icon: widgetType,
      widgetType,
      settingsSchema: schema,
      settings: settingsMap,
    };

    const codeSnippet = isPython
      ? `# main.py - ${metadata.name}\nimport sys\nimport time\nimport json\n\ndef init_plugin(context):\n    print(f"[{metadata.name}] Initialized via Python 3.11 engine")\n\ndef update_tick():\n    pass\n\nif __name__ == "__main__":\n    init_plugin({})\n`
      : `// plugin.dll - C# (.NET 8 Assembly)\nusing System;\nusing FuncOverlay.Sdk;\n\nnamespace ${folderName}\n{\n    public class MainPlugin : IFuncOverlayPlugin\n    {\n        public void OnLoad(IOverlayContext context)\n        {\n            Console.WriteLine("${metadata.name} loaded into C# Assembly context.");\n        }\n\n        public void OnRender() { }\n        public void OnUnload() { }\n    }\n}`;

    const newFolder: PluginFolder = {
      folderName,
      path: `plugins/${folderName}`,
      metadata,
      hasMainPy: isPython,
      hasPluginDll: !isPython,
      files: [
        {
          name: 'metadata.json',
          path: `plugins/${folderName}/metadata.json`,
          content: JSON.stringify(metadata, null, 2),
          isMetadata: true,
        },
        {
          name: entryPoint,
          path: `plugins/${folderName}/${entryPoint}`,
          content: codeSnippet,
        },
        {
          name: 'README.md',
          path: `plugins/${folderName}/README.md`,
          content: `# ${metadata.name}\n\n${metadata.description}\n\n- Entrypoint: \`${entryPoint}\`\n- Author: ${metadata.author}\n`,
        },
      ],
    };

    setPlugins((prev) => {
      const updated = [...prev, newFolder];
      localStorage.setItem(STORAGE_KEY_PLUGINS, JSON.stringify(updated));
      return updated;
    });

    showToast(`Installed plugin '${folderName}' (${language.toUpperCase()})`);
  }, [plugins.length, showToast]);

  // Install Plugin from directory or extracted from .zip archive
  const installPluginArchive = useCallback((pluginName: string = 'SampleWidget') => {
    const types: ('clock' | 'sysinfo' | 'notes')[] = ['clock', 'sysinfo', 'notes'];
    const chosen = types[plugins.length % types.length];
    addSamplePlugin(chosen, plugins.length % 2 === 0 ? 'python' : 'csharp');
  }, [addSamplePlugin, plugins.length]);

  const addCustomPluginFolder = useCallback((folderName: string, language: 'python' | 'csharp', name: string, description: string) => {
    const cleanFolderName = folderName.replace(/[^a-zA-Z0-9_-]/g, '') || 'NewPlugin';
    const id = `plugin_${cleanFolderName.toLowerCase()}_${Date.now().toString(36)}`;
    const isPython = language === 'python';
    const entryPoint = isPython ? 'main.py' : 'plugin.dll';

    const defaultSchema: PluginSettingDefinition[] = [
      {
        key: 'plugin_title',
        label: 'Display Title',
        type: 'string',
        defaultValue: name || cleanFolderName,
        value: name || cleanFolderName,
        description: 'Module banner title',
      },
      {
        key: 'refresh_rate',
        label: 'Refresh Rate (ms)',
        type: 'int',
        defaultValue: 500,
        value: 500,
        description: 'Update cycle frequency',
      },
      {
        key: 'enable_logging',
        label: 'Enable Verbose Logging',
        type: 'bool',
        defaultValue: false,
        value: false,
        description: 'Output debug logs to console',
      },
    ];

    const metadata: PluginMetadata = {
      id,
      name: name || cleanFolderName,
      description: description || 'Custom user plugin module',
      version: '1.0.0',
      author: 'User',
      entryPoint: entryPoint as any,
      language,
      position: { x: 120, y: 120 },
      size: { width: 340, height: 240 },
      enabled: true,
      widgetType: 'custom',
      settingsSchema: defaultSchema,
      settings: {
        plugin_title: name || cleanFolderName,
        refresh_rate: 500,
        enable_logging: false,
      },
    };

    const newFolder: PluginFolder = {
      folderName: cleanFolderName,
      path: `plugins/${cleanFolderName}`,
      metadata,
      hasMainPy: isPython,
      hasPluginDll: !isPython,
      files: [
        {
          name: 'metadata.json',
          path: `plugins/${cleanFolderName}/metadata.json`,
          content: JSON.stringify(metadata, null, 2),
          isMetadata: true,
        },
        {
          name: entryPoint,
          path: `plugins/${cleanFolderName}/${entryPoint}`,
          content: isPython ? `# ${entryPoint} for ${cleanFolderName}\nprint("Hello from custom Python plugin")\n` : `// C# Plugin binary assembly\n`,
        },
      ],
    };

    setPlugins((prev) => {
      const updated = [...prev, newFolder];
      localStorage.setItem(STORAGE_KEY_PLUGINS, JSON.stringify(updated));
      return updated;
    });

    showToast(`Created plugin folder './plugins/${cleanFolderName}'`);
  }, [showToast]);

  const saveMetadataJsonFile = useCallback((folderName: string, jsonString: string): boolean => {
    try {
      const parsed = JSON.parse(jsonString) as PluginMetadata;
      if (!parsed.name || !parsed.entryPoint) {
        throw new Error('Metadata JSON must have name and entryPoint properties');
      }

      setPlugins((prev) => {
        const updated = prev.map((pf) => {
          if (pf.folderName === folderName) {
            const hasMainPy = parsed.entryPoint === 'main.py' || pf.hasMainPy;
            const hasPluginDll = parsed.entryPoint === 'plugin.dll' || pf.hasPluginDll;

            const files = pf.files.map((file) => {
              if (file.isMetadata) {
                return { ...file, content: JSON.stringify(parsed, null, 2) };
              }
              return file;
            });

            return {
              ...pf,
              metadata: parsed,
              hasMainPy,
              hasPluginDll,
              files,
            };
          }
          return pf;
        });

        localStorage.setItem(STORAGE_KEY_PLUGINS, JSON.stringify(updated));
        return updated;
      });

      showToast(`Updated metadata.json for ${folderName}`);
      return true;
    } catch (e: any) {
      showToast(`JSON Error: ${e.message}`);
      return false;
    }
  }, [showToast]);

  const restartApp = useCallback(() => {
    showToast('Restarting FuncOverlay...');
    setIsFading(true);
    setTimeout(() => {
      setIsFading(false);
      showToast('FuncOverlay restarted successfully (plugins reloaded)');
    }, 500);
  }, [showToast]);

  return (
    <AppContext.Provider
      value={{
        isOverlayOpen,
        isFading,
        runOnStartup,
        overlayOpacity,
        fadeDurationMs,
        hotkey,
        theme,
        plugins,
        isSettingsOpen,
        isSettingsMinimized,
        isExplorerOpen,
        isCsharpSpecOpen,
        activeSettingsTab,
        selectedExplorerFolder,
        toastMessage,
        toggleOverlay,
        setOverlayOpen,
        setRunOnStartup,
        setHotkey,
        setOverlayOpacity,
        setFadeDurationMs,
        setTheme,
        openSettings,
        minimizeSettings,
        closeSettings,
        openExplorer,
        closeExplorer,
        openCsharpSpec,
        closeCsharpSpec,
        updatePluginPosition,
        updatePluginSize,
        updatePluginMetadata,
        updatePluginSetting,
        togglePluginEnabled,
        deletePlugin,
        addSamplePlugin,
        addCustomPluginFolder,
        installPluginArchive,
        saveMetadataJsonFile,
        clearAllPlugins,
        restartApp,
        showToast,
      }}
    >
      {children}
    </AppContext.Provider>
  );
};

export const useApp = () => {
  const context = useContext(AppContext);
  if (!context) throw new Error('useApp must be used within AppProvider');
  return context;
};
