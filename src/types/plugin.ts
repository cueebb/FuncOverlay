export interface PluginPosition {
  x: number;
  y: number;
}

export interface PluginSize {
  width: number;
  height: number;
}

export interface PluginMetadata {
  id: string;
  name: string;
  description: string;
  version: string;
  author: string;
  entryPoint: "main.py" | "plugin.dll";
  language: "python" | "csharp";
  position: PluginPosition;
  size: PluginSize;
  enabled: boolean;
  icon?: string;
  hotkey?: string;
  dependencies?: string[];
  widgetType?: 'clock' | 'sysinfo' | 'notes' | 'volume' | 'custom';
  customScriptContent?: string;
}

export interface PluginVirtualFile {
  name: string;
  path: string;
  content: string;
  isMetadata?: boolean;
}

export interface PluginFolder {
  folderName: string;
  path: string;
  metadata: PluginMetadata;
  hasMainPy: boolean;
  hasPluginDll: boolean;
  files: PluginVirtualFile[];
}
