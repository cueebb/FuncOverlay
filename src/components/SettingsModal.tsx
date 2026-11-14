import React, { useState } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { PluginFolder, PluginSettingDefinition } from '../types/plugin';
import {
  X,
  Minus,
  FolderOpen,
  RotateCw,
  Upload,
  Trash2,
  Sliders,
  Keyboard,
  Package,
  Folder,
} from 'lucide-react';

export const SettingsModal: React.FC = () => {
  const {
    isSettingsOpen,
    isSettingsMinimized,
    minimizeSettings,
    closeSettings,
    runOnStartup,
    setRunOnStartup,
    fadeDurationMs,
    setFadeDurationMs,
    overlayOpacity,
    setOverlayOpacity,
    hotkey,
    setHotkey,
    plugins,
    openExplorer,
    restartApp,
    installPluginArchive,
    togglePluginEnabled,
    deletePlugin,
    updatePluginSetting,
  } = useApp();

  // Key recording state
  const [isRecording, setIsRecording] = useState(false);
  const [recordedCombo, setRecordedCombo] = useState<string>('');

  if (!isSettingsOpen || isSettingsMinimized) return null;

  const handleKeyDown = (e: React.KeyboardEvent) => {
    e.preventDefault();
    if (e.key === 'Escape') {
      setIsRecording(false);
      return;
    }

    const modifiers: string[] = [];
    if (e.ctrlKey) modifiers.push('Ctrl');
    if (e.altKey) modifiers.push('Alt');
    if (e.shiftKey) modifiers.push('Shift');

    let keyName = e.key;
    if (keyName === ' ') keyName = 'Space';
    if (keyName === 'Control' || keyName === 'Alt' || keyName === 'Shift' || keyName === 'Meta') {
      return;
    }

    // Capitalize single letter
    if (keyName.length === 1) keyName = keyName.toUpperCase();

    const combo = modifiers.length > 0 ? `${modifiers.join('+')}+${keyName}` : keyName;
    setRecordedCombo(combo);
  };

  const handleSaveRecordedHotkey = () => {
    if (recordedCombo) {
      setHotkey(recordedCombo);
      setIsRecording(false);
      setRecordedCombo('');
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-md select-none animate-in fade-in duration-150"
      onClick={(e) => {
        if (e.target === e.currentTarget) closeSettings();
      }}
    >
      {/* Design styled identically to 'How to Install Plugins' Window */}
      <div className="w-[660px] max-w-[94vw] max-h-[88vh] rounded-2xl bg-zinc-950/95 border border-zinc-800 p-8 text-zinc-100 shadow-2xl backdrop-blur-xl flex flex-col overflow-hidden animate-in zoom-in-95 duration-200">
        
        {/* Header with ONLY Minimize (_) and Close (X) in upper-right corner */}
        <div className="flex items-start justify-between pb-4 mb-6 border-b border-zinc-800 shrink-0">
          <div>
            <h2 className="text-xl font-bold tracking-tight text-white font-sans">
              {t('settings.title')}
            </h2>
            <p className="text-xs text-zinc-400 mt-1 leading-relaxed">
              {t('settings.subtitle')}
            </p>
          </div>
          <div className="flex items-center gap-1 shrink-0">
            <button
              onClick={minimizeSettings}
              className="w-8 h-8 rounded-lg hover:bg-zinc-900 text-zinc-400 hover:text-white transition-colors cursor-pointer flex items-center justify-center"
              title={t('settings.minimize')}
            >
              <Minus className="w-4 h-4" />
            </button>
            <button
              onClick={closeSettings}
              className="w-8 h-8 rounded-lg hover:bg-zinc-900 text-zinc-400 hover:text-white transition-colors cursor-pointer flex items-center justify-center"
              title={t('settings.close')}
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>

        {/* Scrollable Settings Content */}
        <div className="flex-1 overflow-y-auto space-y-6 pr-2">
          
          {/* ================================================================= */}
          {/* 1. GENERAL SECTION                                               */}
          {/* ================================================================= */}
          <div className="p-5 rounded-xl bg-zinc-900/80 border border-zinc-800 space-y-5">
            <div className="flex items-center gap-2 border-b border-zinc-800 pb-2.5">
              <Sliders className="w-4 h-4 text-zinc-300" />
              <h3 className="text-sm font-semibold text-white tracking-wide uppercase font-mono text-[11px]">
                {t('settings.general.sectionTitle')}
              </h3>
            </div>

            {/* First Setting: Run on startup (Boolean) */}
            <div className="flex items-center justify-between gap-4 pb-1">
              <div className="space-y-0.5">
                <label className="font-semibold text-white text-xs block">
                  {t('settings.general.runOnStartupLabel')}
                </label>
                <p className="text-[11px] text-zinc-400 leading-normal">
                  {t('settings.general.runOnStartupDesc')}
                </p>
              </div>
              <button
                type="button"
                onClick={() => setRunOnStartup(!runOnStartup)}
                className={`w-10 h-5 flex items-center rounded-full p-0.5 transition-colors cursor-pointer border shrink-0 ${
                  runOnStartup
                    ? 'bg-white border-white justify-end'
                    : 'bg-zinc-950 border-zinc-700 justify-start'
                }`}
                aria-label={t('settings.general.runOnStartupLabel')}
              >
                <span
                  className={`w-4 h-4 rounded-full transition-transform ${
                    runOnStartup ? 'bg-black' : 'bg-zinc-400'
                  }`}
                />
              </button>
            </div>

            {/* Field 2: Transition (time, in ms) */}
            <div className="space-y-1.5 pt-3 border-t border-zinc-800/70">
              <div className="flex justify-between items-center text-xs">
                <label className="font-semibold text-white">
                  {t('settings.general.transitionLabel')} ({t('settings.general.transitionUnit')})
                </label>
                <span className="font-mono text-xs text-zinc-200 font-bold bg-black px-2 py-0.5 rounded border border-zinc-800">
                  {fadeDurationMs} ms
                </span>
              </div>
              <input
                type="range"
                min="50"
                max="1000"
                step="25"
                value={fadeDurationMs}
                onChange={(e) => setFadeDurationMs(Number(e.target.value))}
                className="w-full accent-white cursor-pointer"
              />
              <p className="text-[11px] text-zinc-400 leading-normal">
                {t('settings.general.transitionDesc')}
              </p>
            </div>

            {/* Field 2: Overlay Alpha (0-100%) */}
            <div className="space-y-1.5 pt-1 border-t border-zinc-800/70">
              <div className="flex justify-between items-center text-xs">
                <label className="font-semibold text-white">
                  {t('settings.general.overlayAlphaLabel')}
                </label>
                <span className="font-mono text-xs text-zinc-200 font-bold bg-black px-2 py-0.5 rounded border border-zinc-800">
                  {Math.round(overlayOpacity * 100)}%
                </span>
              </div>
              <input
                type="range"
                min="0.05"
                max="1.0"
                step="0.05"
                value={overlayOpacity}
                onChange={(e) => setOverlayOpacity(Number(e.target.value))}
                className="w-full accent-white cursor-pointer"
              />
              <p className="text-[11px] text-zinc-400 leading-normal">
                {t('settings.general.overlayAlphaDesc')}
              </p>
            </div>

            {/* Field 3: Global Hotkey (Text label -> text field -> Primary button "Change Keybind") */}
            <div className="space-y-2 pt-1 border-t border-zinc-800/70">
              <label className="font-semibold text-white text-xs block">
                {t('settings.general.globalHotkeyLabel')}
              </label>

              {isRecording ? (
                /* Interactive Keybind Recorder */
                <div className="p-3 rounded-lg bg-black border border-white space-y-2.5 animate-in fade-in">
                  <p className="text-xs text-zinc-300 font-medium font-mono animate-pulse">
                    {t('settings.general.recordingPrompt')}
                  </p>
                  <input
                    type="text"
                    readOnly
                    onKeyDown={handleKeyDown}
                    value={recordedCombo || 'Press key combination...'}
                    className="w-full bg-zinc-900 border border-zinc-700 rounded-md px-3 py-1.5 font-mono text-xs text-white text-center focus:outline-none"
                    autoFocus
                  />
                  <div className="flex items-center gap-2">
                    {/* Primary Button: White with black text */}
                    <button
                      onClick={handleSaveRecordedHotkey}
                      disabled={!recordedCombo}
                      className="bg-white text-black font-semibold hover:bg-zinc-200 transition-colors cursor-pointer rounded-lg px-4 py-1.5 text-xs shadow-sm disabled:opacity-40"
                    >
                      {t('settings.general.saveKeybindBtn')}
                    </button>
                    {/* Secondary Button: Black with white outline */}
                    <button
                      onClick={() => setIsRecording(false)}
                      className="bg-black text-white font-medium border border-white hover:bg-zinc-900 transition-colors cursor-pointer rounded-lg px-4 py-1.5 text-xs"
                    >
                      {t('settings.general.cancelBtn')}
                    </button>
                  </div>
                </div>
              ) : (
                /* Standard Display: After text -> text field -> primary button */
                <div className="flex items-center gap-2.5">
                  <input
                    type="text"
                    readOnly
                    value={hotkey}
                    className="flex-1 bg-black border border-zinc-800 rounded-lg px-3 py-2 font-mono text-xs text-white font-semibold focus:outline-none"
                  />
                  {/* Primary button: White with black text */}
                  <button
                    onClick={() => {
                      setIsRecording(true);
                      setRecordedCombo('');
                    }}
                    className="bg-white text-black font-semibold hover:bg-zinc-200 transition-colors cursor-pointer rounded-lg px-4 py-2 text-xs shadow-sm shrink-0 active:scale-95 flex items-center gap-1.5"
                  >
                    <Keyboard className="w-3.5 h-3.5" />
                    <span>{t('settings.general.changeKeybindBtn')}</span>
                  </button>
                  {/* Secondary button: Black with white outline */}
                  <button
                    onClick={() => setHotkey('Alt+Space')}
                    className="bg-black text-white font-medium border border-white hover:bg-zinc-900 transition-colors cursor-pointer rounded-lg px-3 py-2 text-xs shrink-0 active:scale-95"
                    title={t('settings.general.resetDefaultBtn')}
                  >
                    Reset (Alt+Space)
                  </button>
                </div>
              )}
            </div>
          </div>

          {/* ================================================================= */}
          {/* 2. PLUGINS SECTION                                               */}
          {/* ================================================================= */}
          <div className="p-5 rounded-xl bg-zinc-900/80 border border-zinc-800 space-y-5">
            <div className="flex items-center justify-between border-b border-zinc-800 pb-2.5">
              <div className="flex items-center gap-2">
                <Package className="w-4 h-4 text-zinc-300" />
                <h3 className="text-sm font-semibold text-white tracking-wide uppercase font-mono text-[11px]">
                  {t('settings.plugins.sectionTitle')}
                </h3>
              </div>
              <span className="text-[11px] font-mono text-zinc-400">
                {plugins.length} installed
              </span>
            </div>

            {/* Row 1: Open Plugins Folder (Primary) + Restart FuncOverlay (Secondary) */}
            <div className="flex items-center gap-3">
              {/* Primary button: White with black text */}
              <button
                onClick={() => openExplorer()}
                className="bg-white text-black font-semibold hover:bg-zinc-200 transition-colors cursor-pointer rounded-lg px-4 py-2 text-xs shadow-sm flex items-center gap-2 active:scale-95"
              >
                <FolderOpen className="w-3.5 h-3.5" />
                <span>{t('settings.plugins.openPluginsFolderBtn')}</span>
              </button>

              {/* Secondary button: Black with white outline */}
              <button
                onClick={restartApp}
                className="bg-black text-white font-medium border border-white hover:bg-zinc-900 transition-colors cursor-pointer rounded-lg px-4 py-2 text-xs flex items-center gap-2 active:scale-95"
              >
                <RotateCw className="w-3.5 h-3.5" />
                <span>{t('settings.plugins.restartFuncOverlayBtn')}</span>
              </button>
            </div>

            {/* Row 2: Clearly visible gray hint text */}
            <div className="px-1 text-xs text-zinc-400 leading-normal">
              {t('settings.plugins.restartHint')}
            </div>

            {/* Row 3: Install Plugin button (Primary, white, extracts even from zip archives) */}
            <div className="pt-2 border-t border-zinc-800/70 space-y-1.5">
              <button
                onClick={() => installPluginArchive()}
                className="bg-white text-black font-semibold hover:bg-zinc-200 transition-colors cursor-pointer rounded-lg px-4 py-2 text-xs shadow-sm flex items-center gap-2 active:scale-95"
              >
                <Upload className="w-3.5 h-3.5" />
                <span>{t('settings.plugins.installPluginBtn')}</span>
              </button>
              <p className="text-[11px] text-zinc-400 leading-normal px-1">
                {t('settings.plugins.installPluginDesc')}
              </p>
            </div>

            {/* =============================================================== */}
            {/* 3. ALREADY INSTALLED PLUGINS LIST                               */}
            {/* =============================================================== */}
            <div className="pt-3 border-t border-zinc-800/80 space-y-3">
              <h4 className="text-xs font-semibold text-white uppercase font-mono tracking-wider">
                {t('settings.plugins.installedPluginsTitle')}
              </h4>

              {plugins.length === 0 ? (
                /* Empty state with helpful prompt */
                <div className="p-4 rounded-xl bg-black/40 border border-zinc-800 text-center space-y-3">
                  <p className="text-xs text-zinc-400">
                    {t('settings.plugins.noInstalledPlugins', { FuncOverlay_folder: '.' })}
                  </p>
                  <button
                    onClick={() => installPluginArchive('obs_shortcuts')}
                    className="bg-white text-black font-semibold hover:bg-zinc-200 transition-colors cursor-pointer rounded-lg px-3.5 py-1.5 text-xs shadow-sm inline-flex items-center gap-1.5"
                  >
                    <Package className="w-3.5 h-3.5" />
                    <span>+ Install "obs shortcuts" Plugin</span>
                  </button>
                </div>
              ) : (
                /* List of Installed Plugins */
                <div className="space-y-3">
                  {plugins.map((pf) => (
                    <InstalledPluginCard
                      key={pf.metadata.id}
                      pluginFolder={pf}
                      onToggleEnabled={() => togglePluginEnabled(pf.metadata.id)}
                      onDelete={() => deletePlugin(pf.metadata.id)}
                      onOpenFolder={() => openExplorer(pf.folderName)}
                      onUpdateSetting={(key, val) => updatePluginSetting(pf.metadata.id, key, val)}
                    />
                  ))}
                </div>
              )}
            </div>

          </div>

        </div>

      </div>
    </div>
  );
};

/* ========================================================================= */
/* INSTALLED PLUGIN CARD WITH INLINE METADATA SETTINGS (STRING, INT, BOOL)    */
/* ========================================================================= */
interface InstalledPluginCardProps {
  pluginFolder: PluginFolder;
  onToggleEnabled: () => void;
  onDelete: () => void;
  onOpenFolder: () => void;
  onUpdateSetting: (settingKey: string, newValue: any) => void;
}

const InstalledPluginCard: React.FC<InstalledPluginCardProps> = ({
  pluginFolder,
  onToggleEnabled,
  onDelete,
  onOpenFolder,
  onUpdateSetting,
}) => {
  const { metadata } = pluginFolder;
  const { name, description, entryPoint, enabled, settingsSchema, settings } = metadata;

  return (
    <div className="p-4 rounded-xl bg-zinc-950 border border-zinc-800 space-y-3.5 hover:border-zinc-700 transition-colors">
      {/* Top Row: Name & Description on the left, Enabled/Disabled + Buttons stacked on the right */}
      <div className="flex items-start justify-between gap-4">
        <div className="flex-1 space-y-1">
          {/* Name: main text, white color */}
          <div className="flex items-center gap-2">
            <h5 className="text-sm font-semibold text-white font-sans leading-tight">
              {name}
            </h5>
            <span className="px-1.5 py-0.2 rounded text-[9px] font-mono uppercase bg-zinc-900 text-zinc-400 border border-zinc-800">
              {entryPoint}
            </span>
          </div>
          {/* Description: gray text, smaller font */}
          <p className="text-xs text-zinc-400 leading-relaxed font-sans">
            {description}
          </p>
        </div>

        {/* Right Controls: Enabled/Disabled toggle ABOVE Open Folder & Delete Plugin buttons */}
        <div className="flex flex-col items-end gap-2 shrink-0">
          {/* Enabled / Disabled Toggle Row */}
          <div className="flex items-center gap-2">
            <span className={`text-[11px] font-medium ${enabled ? 'text-white' : 'text-zinc-400'}`}>
              {enabled ? t('common.enabled') : t('common.disabled')}
            </span>
            <button
              type="button"
              onClick={onToggleEnabled}
              className={`w-10 h-5 flex items-center rounded-full p-0.5 transition-colors cursor-pointer border ${
                enabled
                  ? 'bg-white border-white justify-end'
                  : 'bg-zinc-900 border-zinc-700 justify-start'
              }`}
              title={enabled ? t('common.enabled') : t('common.disabled')}
            >
              <span
                className={`w-4 h-4 rounded-full transition-transform ${
                  enabled ? 'bg-black' : 'bg-zinc-400'
                }`}
              />
            </button>
          </div>

          {/* Secondary Buttons: Black with white outline */}
          <div className="flex items-center gap-2">
            {/* Open Plugin Folder (Secondary) */}
            <button
              onClick={onOpenFolder}
              className="bg-black text-white font-medium border border-white hover:bg-zinc-900 transition-colors cursor-pointer rounded-lg px-3 py-1.5 text-xs flex items-center gap-1.5 active:scale-95"
              title="Open Folder"
            >
              <Folder className="w-3 h-3" />
              <span>{t('settings.plugins.openPluginFolderBtn')}</span>
            </button>

            {/* Delete Plugin (Secondary) */}
            <button
              onClick={onDelete}
              className="bg-black text-white font-medium border border-white hover:bg-zinc-900 transition-colors cursor-pointer rounded-lg px-3 py-1.5 text-xs flex items-center gap-1.5 active:scale-95"
              title="Delete Plugin"
            >
              <Trash2 className="w-3 h-3" />
              <span>{t('settings.plugins.deletePluginBtn')}</span>
            </button>
          </div>
        </div>
      </div>

      {/* Settings of this plugin (Defined by metadata.json: string, int, bool, select) */}
      {settingsSchema && settingsSchema.length > 0 && (
        <div className="pt-3 border-t border-zinc-900 space-y-2.5">
          <div className="text-[10px] font-mono text-zinc-500 uppercase tracking-wider font-semibold">
            {t('settings.plugins.pluginSettingsTitle')} (metadata.json)
          </div>

          <div className="grid grid-cols-1 gap-2.5 bg-black/60 p-3 rounded-lg border border-zinc-900">
            {settingsSchema.map((def: PluginSettingDefinition) => (
              <PluginSettingField
                key={def.key}
                definition={def}
                currentValue={settings?.[def.key] !== undefined ? settings[def.key] : def.value}
                onChange={(newVal) => onUpdateSetting(def.key, newVal)}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  );
};

/* ========================================================================= */
/* DYNAMIC SETTING FIELD (STRING, INT, BOOL, SELECT)                         */
/* ========================================================================= */
interface PluginSettingFieldProps {
  definition: PluginSettingDefinition;
  currentValue: any;
  onChange: (val: any) => void;
}

const PluginSettingField: React.FC<PluginSettingFieldProps> = ({
  definition,
  currentValue,
  onChange,
}) => {
  const { key, label, type, description, options } = definition;

  return (
    <div className="flex items-center justify-between gap-3 text-xs">
      <div className="flex-1">
        <div className="font-medium text-zinc-200">{label}</div>
        {description && (
          <div className="text-[10px] text-zinc-500 leading-tight">{description}</div>
        )}
      </div>

      <div className="shrink-0">
        {/* BOOL TYPE */}
        {type === 'bool' && (
          <button
            type="button"
            onClick={() => onChange(!currentValue)}
            className={`w-10 h-5 flex items-center rounded-full p-0.5 transition-colors cursor-pointer border ${
              currentValue
                ? 'bg-white border-white justify-end'
                : 'bg-zinc-900 border-zinc-700 justify-start'
            }`}
          >
            <span
              className={`w-4 h-4 rounded-full transition-transform ${
                currentValue ? 'bg-black' : 'bg-zinc-400'
              }`}
            />
          </button>
        )}

        {/* INT TYPE */}
        {type === 'int' && (
          <input
            type="number"
            value={currentValue ?? 0}
            onChange={(e) => onChange(Number(e.target.value))}
            className="w-24 bg-zinc-900 border border-zinc-800 rounded px-2 py-1 text-right font-mono text-xs text-white focus:outline-none focus:border-zinc-600"
          />
        )}

        {/* STRING TYPE */}
        {type === 'string' && (
          <input
            type="text"
            value={currentValue ?? ''}
            onChange={(e) => onChange(e.target.value)}
            className="w-36 bg-zinc-900 border border-zinc-800 rounded px-2 py-1 font-mono text-xs text-white focus:outline-none focus:border-zinc-600"
          />
        )}

        {/* SELECT TYPE */}
        {type === 'select' && options && (
          <select
            value={currentValue}
            onChange={(e) => onChange(e.target.value)}
            className="bg-zinc-900 border border-zinc-800 rounded px-2 py-1 font-mono text-xs text-white focus:outline-none focus:border-zinc-600 cursor-pointer"
          >
            {options.map((opt) => (
              <option key={opt} value={opt}>
                {opt}
              </option>
            ))}
          </select>
        )}
      </div>
    </div>
  );
};
