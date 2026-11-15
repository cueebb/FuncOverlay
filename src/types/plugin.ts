export type WidgetAnchor =
  | 'top-left'
  | 'top-center'
  | 'top-right'
  | 'center-left'
  | 'center'
  | 'center-right'
  | 'bottom-left'
  | 'bottom-center'
  | 'bottom-right';

export const WIDGET_ANCHOR_OPTIONS: WidgetAnchor[] = [
  'top-left',
  'top-center',
  'top-right',
  'center-left',
  'center',
  'center-right',
  'bottom-left',
  'bottom-center',
  'bottom-right',
];

export interface PluginPosition {
  x: number;
  y: number;
  anchor?: WidgetAnchor | string;
  anchorTarget?: string;
  targetAnchor?: WidgetAnchor | string;
}

export interface PluginSize {
  width: number;
  height: number;
}

export interface PluginSettingDefinition {
  key: string;
  label: string;
  type: 'string' | 'int' | 'bool' | 'select';
  defaultValue: string | number | boolean;
  value: string | number | boolean;
  options?: string[];
  description?: string;
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
  locked?: boolean;
  icon?: string;
  hotkey?: string;
  dependencies?: string[];
  widgetType?: 'obs_shortcuts' | 'simple_calc' | 'clock' | 'sysinfo' | 'notes' | 'volume' | 'custom';
  customScriptContent?: string;
  settingsSchema?: PluginSettingDefinition[];
  settings?: Record<string, any>;
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
