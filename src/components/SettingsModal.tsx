import React, { useState } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import {
  Settings,
  Keyboard,
  Folder,
  Terminal,
  Sliders,
  X,
  Check,
  RotateCcw,
  Sparkles,
  Monitor,
  Moon,
  Sun,
  Globe,
  RefreshCw,
} from 'lucide-react';

export const SettingsModal: React.FC = () => {
  const {
    isSettingsOpen,
    closeSettings,
    activeSettingsTab,
    openSettings,
    hotkey,
    setHotkey,
    overlayOpacity,
    setOverlayOpacity,
    fadeDurationMs,
    setFadeDurationMs,
    theme,
    setTheme,
    plugins,
    openExplorer,
    openCsharpSpec,
    restartApp,
  } = useApp();

  const [isRecordingKey, setIsRecordingKey] = useState(false);
  const [recordedKeys, setRecordedKeys] = useState<string>('');

  if (!isSettingsOpen) return null;

  const handleKeyDownRecorder = (e: React.KeyboardEvent) => {
    e.preventDefault();
    if (e.key === 'Escape') {
      setIsRecordingKey(false);
      return;
    }

    const modifiers = [];
    if (e.altKey) modifiers.push('Alt');
    if (e.ctrlKey) modifiers.push('Ctrl');
    if (e.shiftKey) modifiers.push('Shift');

    let keyName = e.key;
    if (keyName === ' ') keyName = 'Space';
    if (['Alt', 'Control', 'Shift', 'Meta'].includes(keyName)) return;

    const keybindStr = [...modifiers, keyName].join('+');
    setRecordedKeys(keybindStr);
  };

  const confirmRecordedKey = () => {
    if (recordedKeys) {
      setHotkey(recordedKeys);
      setIsRecordingKey(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-md select-none animate-in fade-in duration-150">
      <div className="w-[720px] max-w-[95vw] h-[520px] max-h-[90vh] rounded-2xl fluent-glass border border-white/20 flex flex-col overflow-hidden text-slate-100 shadow-2xl">
        {/* Title Bar */}
        <div className="h-12 px-5 bg-slate-950/80 border-b border-white/10 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Settings className="w-4 h-4 text-blue-400" />
            <span className="font-semibold text-sm">{t('settings.title')}</span>
          </div>
          <button
            onClick={closeSettings}
            className="p-1 rounded hover:bg-white/10 text-slate-400 hover:text-white transition-colors cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Modal Layout: Left Sidebar Navigation + Right Settings Content */}
        <div className="flex-1 flex overflow-hidden">
          {/* Sidebar Tabs */}
          <div className="w-56 bg-slate-950/40 border-r border-white/10 p-3 space-y-1 text-xs">
            <button
              onClick={() => openSettings('general')}
              className={`w-full flex items-center gap-2.5 px-3 py-2 rounded-xl transition-colors cursor-pointer ${
                activeSettingsTab === 'general'
                  ? 'bg-blue-600 text-white font-medium shadow'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-white/5'
              }`}
            >
              <Sliders className="w-4 h-4" />
              <span>{t('settings.tabs.general')}</span>
            </button>

            <button
              onClick={() => openSettings('hotkey')}
              className={`w-full flex items-center justify-between px-3 py-2 rounded-xl transition-colors cursor-pointer ${
                activeSettingsTab === 'hotkey'
                  ? 'bg-blue-600 text-white font-medium shadow'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-white/5'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Keyboard className="w-4 h-4" />
                <span>{t('settings.tabs.hotkey')}</span>
              </div>
              <span className="font-mono text-[10px] px-1.5 py-0.5 rounded bg-black/40 text-blue-300">
                {hotkey}
              </span>
            </button>

            <button
              onClick={() => openSettings('plugins')}
              className={`w-full flex items-center justify-between px-3 py-2 rounded-xl transition-colors cursor-pointer ${
                activeSettingsTab === 'plugins'
                  ? 'bg-blue-600 text-white font-medium shadow'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-white/5'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Folder className="w-4 h-4 text-amber-400" />
                <span>{t('settings.tabs.plugins')}</span>
              </div>
              <span className="font-mono text-[10px] px-1.5 py-0.5 rounded bg-black/40 text-amber-300">
                {plugins.length}
              </span>
            </button>

            <button
              onClick={() => openSettings('csharpDev')}
              className={`w-full flex items-center gap-2.5 px-3 py-2 rounded-xl transition-colors cursor-pointer ${
                activeSettingsTab === 'csharpDev'
                  ? 'bg-blue-600 text-white font-medium shadow'
                  : 'text-slate-400 hover:text-slate-200 hover:bg-white/5'
              }`}
            >
              <Terminal className="w-4 h-4 text-emerald-400" />
              <span>{t('settings.tabs.csharpDev')}</span>
            </button>
          </div>

          {/* Settings Tab Content */}
          <div className="flex-1 p-6 overflow-y-auto space-y-6 text-xs">
            {activeSettingsTab === 'general' && (
              <div className="space-y-6">
                <div>
                  <h3 className="text-sm font-semibold text-white mb-1">Overlay Visual & Animation Settings</h3>
                  <p className="text-slate-400 text-[11px]">
                    Adjust screen dimming transition speed (default 0.25s) and backdrop opacity level.
                  </p>
                </div>

                {/* Fade Duration Slider */}
                <div className="p-4 rounded-xl bg-slate-900/60 border border-white/10 space-y-3">
                  <div className="flex justify-between items-center">
                    <div>
                      <div className="font-semibold text-slate-200">{t('settings.general.fadeDurationLabel')}</div>
                      <div className="text-slate-400 text-[11px]">{t('settings.general.fadeDurationDesc')}</div>
                    </div>
                    <span className="font-mono text-blue-400 font-bold text-sm">{fadeDurationMs / 1000}s ({fadeDurationMs}ms)</span>
                  </div>
                  <input
                    type="range"
                    min="100"
                    max="1000"
                    step="50"
                    value={fadeDurationMs}
                    onChange={(e) => setFadeDurationMs(Number(e.target.value))}
                    className="w-full accent-blue-500 cursor-pointer"
                  />
                  <div className="flex justify-between text-[10px] font-mono text-slate-500">
                    <span>Fast (0.1s)</span>
                    <span className="text-blue-400 font-bold">Default (0.25s / 250ms)</span>
                    <span>Slow (1.0s)</span>
                  </div>
                </div>

                {/* Dim Level Opacity Slider */}
                <div className="p-4 rounded-xl bg-slate-900/60 border border-white/10 space-y-3">
                  <div className="flex justify-between items-center">
                    <div>
                      <div className="font-semibold text-slate-200">{t('settings.general.dimOpacityLabel')}</div>
                      <div className="text-slate-400 text-[11px]">{t('settings.general.dimOpacityDesc')}</div>
                    </div>
                    <span className="font-mono text-indigo-400 font-bold text-sm">{Math.round(overlayOpacity * 100)}%</span>
                  </div>
                  <input
                    type="range"
                    min="0.1"
                    max="0.9"
                    step="0.05"
                    value={overlayOpacity}
                    onChange={(e) => setOverlayOpacity(Number(e.target.value))}
                    className="w-full accent-indigo-500 cursor-pointer"
                  />
                  <div className="flex justify-between text-[10px] font-mono text-slate-500">
                    <span>Light (10%)</span>
                    <span className="text-indigo-400 font-bold">Default (40%)</span>
                    <span>Dark (90%)</span>
                  </div>
                </div>

                {/* Theme & Language */}
                <div className="grid grid-cols-2 gap-4">
                  <div className="p-4 rounded-xl bg-slate-900/60 border border-white/10 space-y-2">
                    <div className="font-semibold text-slate-200 flex items-center gap-1.5">
                      <Monitor className="w-3.5 h-3.5 text-blue-400" />
                      <span>{t('settings.general.themeLabel')}</span>
                    </div>
                    <div className="flex gap-2 pt-1">
                      <button
                        onClick={() => setTheme('dark')}
                        className={`flex-1 py-1.5 px-2 rounded-lg border text-xs font-medium flex items-center justify-center gap-1.5 cursor-pointer ${
                          theme === 'dark' ? 'bg-blue-600 text-white border-blue-500' : 'bg-slate-800 text-slate-400 border-white/10'
                        }`}
                      >
                        <Moon className="w-3.5 h-3.5" />
                        <span>Dark</span>
                      </button>
                      <button
                        onClick={() => setTheme('light')}
                        className={`flex-1 py-1.5 px-2 rounded-lg border text-xs font-medium flex items-center justify-center gap-1.5 cursor-pointer ${
                          theme === 'light' ? 'bg-blue-600 text-white border-blue-500' : 'bg-slate-800 text-slate-400 border-white/10'
                        }`}
                      >
                        <Sun className="w-3.5 h-3.5" />
                        <span>Light</span>
                      </button>
                    </div>
                  </div>

                  <div className="p-4 rounded-xl bg-slate-900/60 border border-white/10 space-y-2">
                    <div className="font-semibold text-slate-200 flex items-center gap-1.5">
                      <Globe className="w-3.5 h-3.5 text-emerald-400" />
                      <span>{t('settings.general.languageLabel')}</span>
                    </div>
                    <select className="w-full bg-slate-950 border border-white/10 rounded-lg p-2 text-xs text-slate-200 focus:outline-none focus:border-blue-500">
                      <option value="en">{t('settings.general.languageEn')}</option>
                    </select>
                  </div>
                </div>
              </div>
            )}

            {activeSettingsTab === 'hotkey' && (
              <div className="space-y-6">
                <div>
                  <h3 className="text-sm font-semibold text-white mb-1">{t('settings.hotkeySection.title')}</h3>
                  <p className="text-slate-400 text-[11px]">{t('settings.hotkeySection.description')}</p>
                </div>

                <div className="p-6 rounded-2xl bg-slate-900/80 border border-white/15 text-center space-y-4">
                  <div className="text-slate-400 font-medium text-xs">{t('hotkey.currentKey')}</div>
                  <div className="inline-block px-6 py-3 rounded-xl bg-blue-600/20 border border-blue-500/40 font-mono font-bold text-2xl text-blue-300 shadow-inner">
                    {hotkey}
                  </div>

                  {isRecordingKey ? (
                    <div className="p-4 rounded-xl bg-slate-950 border border-amber-500/50 space-y-3">
                      <p className="text-amber-300 font-medium animate-pulse">
                        {t('hotkey.listening')}
                      </p>
                      <input
                        type="text"
                        readOnly
                        onKeyDown={handleKeyDownRecorder}
                        value={recordedKeys || 'Press key combination...'}
                        className="w-full text-center bg-slate-900 border border-slate-700 rounded-lg p-2 font-mono text-sm text-white focus:outline-none"
                        autoFocus
                      />
                      <div className="flex justify-center gap-2 pt-2">
                        <button
                          onClick={confirmRecordedKey}
                          disabled={!recordedKeys}
                          className="px-4 py-1.5 rounded-lg bg-emerald-600 text-white font-semibold disabled:opacity-50 cursor-pointer"
                        >
                          Save Hotkey
                        </button>
                        <button
                          onClick={() => setIsRecordingKey(false)}
                          className="px-4 py-1.5 rounded-lg bg-slate-800 text-slate-300 cursor-pointer"
                        >
                          Cancel
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="flex justify-center gap-3 pt-2">
                      <button
                        onClick={() => {
                          setIsRecordingKey(true);
                          setRecordedKeys('');
                        }}
                        className="px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white font-medium text-xs cursor-pointer shadow-md transition-all active:scale-95"
                      >
                        {t('hotkey.changeHotkey')}
                      </button>
                      <button
                        onClick={() => setHotkey('Alt+Enter')}
                        className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 font-medium text-xs cursor-pointer border border-white/10 transition-all flex items-center gap-1.5"
                      >
                        <RotateCcw className="w-3.5 h-3.5" />
                        <span>{t('settings.hotkeySection.resetDefault')}</span>
                      </button>
                    </div>
                  )}
                </div>
              </div>
            )}

            {activeSettingsTab === 'plugins' && (
              <div className="space-y-6">
                <div>
                  <h3 className="text-sm font-semibold text-white mb-1">{t('settings.pluginsSection.title')}</h3>
                  <p className="text-slate-400 text-[11px]">{t('settings.pluginsSection.directoryDesc')}</p>
                </div>

                <div className="p-4 rounded-xl bg-slate-900/60 border border-white/10 space-y-3 font-mono">
                  <div className="text-slate-400 text-xs">{t('settings.pluginsSection.directoryLabel')}:</div>
                  <div className="p-2.5 rounded-lg bg-black/50 border border-white/10 text-amber-300 text-xs flex items-center justify-between">
                    <span>./plugins/</span>
                    <span className="text-[10px] text-slate-500">Root path</span>
                  </div>
                </div>

                <div className="flex items-center justify-between p-4 rounded-xl bg-slate-900/60 border border-white/10">
                  <div>
                    <div className="font-semibold text-white">Virtual File Explorer</div>
                    <div className="text-slate-400 text-[11px]">Inspect main.py, plugin.dll and metadata.json files</div>
                  </div>
                  <button
                    onClick={() => {
                      closeSettings();
                      openExplorer();
                    }}
                    className="px-3 py-1.5 rounded-lg bg-amber-600 hover:bg-amber-500 text-white text-xs font-medium cursor-pointer transition-all"
                  >
                    {t('settings.pluginsSection.openFolder')}
                  </button>
                </div>
              </div>
            )}

            {activeSettingsTab === 'csharpDev' && (
              <div className="space-y-4">
                <div>
                  <h3 className="text-sm font-semibold text-white mb-1">{t('settings.csharpDev.title')}</h3>
                  <p className="text-slate-400 text-[11px]">{t('settings.csharpDev.desc')}</p>
                </div>

                <div className="p-4 rounded-xl bg-slate-900/90 border border-white/15 space-y-3">
                  <div className="flex items-center justify-between text-xs">
                    <span className="font-semibold text-emerald-400">{t('settings.csharpDev.snippetTitle')}</span>
                    <button
                      onClick={openCsharpSpec}
                      className="text-xs text-blue-400 hover:underline cursor-pointer"
                    >
                      View Full Architecture Guide →
                    </button>
                  </div>
                  <pre className="p-3 rounded-lg bg-black/60 font-mono text-[11px] text-slate-300 overflow-x-auto border border-white/5 leading-relaxed">
{`// FuncOverlay C# Host App (.NET 8 WPF / WinUI 3)
// Register global Alt+Enter keybind via Win32 RegisterHotKey API

public class OverlayHost : Window
{
    private const int HOTKEY_ID = 9000;
    private const uint MOD_ALT = 0x0001;
    private const uint VK_RETURN = 0x0D; // Enter

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        RegisterHotKey(handle, HOTKEY_ID, MOD_ALT, VK_RETURN);
    }
}`}
                  </pre>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="h-12 px-5 bg-slate-950/80 border-t border-white/10 flex items-center justify-between text-xs">
          <span className="text-slate-500 font-mono">FuncOverlay Settings Engine</span>
          <button
            onClick={closeSettings}
            className="px-4 py-1.5 rounded-lg bg-blue-600 hover:bg-blue-500 text-white font-medium cursor-pointer transition-colors"
          >
            {t('settings.close')}
          </button>
        </div>
      </div>
    </div>
  );
};
