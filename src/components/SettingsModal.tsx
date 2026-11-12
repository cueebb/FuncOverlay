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
  RotateCcw,
  Monitor,
  Moon,
  Sun,
  Globe,
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
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-md select-none animate-in fade-in duration-150">
      <div className="w-[680px] max-w-[95vw] h-[500px] max-h-[90vh] rounded-2xl bg-zinc-950 border border-zinc-800 flex flex-col overflow-hidden text-zinc-100 shadow-2xl">
        {/* Title Bar */}
        <div className="h-11 px-5 bg-zinc-900 border-b border-zinc-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Settings className="w-4 h-4 text-zinc-400" />
            <span className="font-semibold text-xs text-white">{t('settings.title')}</span>
          </div>
          <button
            onClick={closeSettings}
            className="p-1 rounded hover:bg-zinc-800 text-zinc-400 hover:text-white transition-colors cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Modal Layout */}
        <div className="flex-1 flex overflow-hidden">
          {/* Sidebar Tabs */}
          <div className="w-52 bg-zinc-950/80 border-r border-zinc-800 p-2.5 space-y-1 text-xs font-sans">
            <button
              onClick={() => openSettings('general')}
              className={`w-full flex items-center gap-2.5 px-3 py-2 rounded-lg transition-colors cursor-pointer ${
                activeSettingsTab === 'general'
                  ? 'bg-zinc-800 text-white font-medium border border-zinc-700'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900'
              }`}
            >
              <Sliders className="w-3.5 h-3.5 text-zinc-400" />
              <span>{t('settings.tabs.general')}</span>
            </button>

            <button
              onClick={() => openSettings('hotkey')}
              className={`w-full flex items-center justify-between px-3 py-2 rounded-lg transition-colors cursor-pointer ${
                activeSettingsTab === 'hotkey'
                  ? 'bg-zinc-800 text-white font-medium border border-zinc-700'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Keyboard className="w-3.5 h-3.5 text-zinc-400" />
                <span>{t('settings.tabs.hotkey')}</span>
              </div>
              <span className="font-mono text-[10px] px-1.5 py-0.5 rounded bg-zinc-900 text-zinc-300 border border-zinc-800">
                {hotkey}
              </span>
            </button>

            <button
              onClick={() => openSettings('plugins')}
              className={`w-full flex items-center justify-between px-3 py-2 rounded-lg transition-colors cursor-pointer ${
                activeSettingsTab === 'plugins'
                  ? 'bg-zinc-800 text-white font-medium border border-zinc-700'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Folder className="w-3.5 h-3.5 text-zinc-400" />
                <span>{t('settings.tabs.plugins')}</span>
              </div>
              <span className="font-mono text-[10px] px-1.5 py-0.5 rounded bg-zinc-900 text-zinc-300 border border-zinc-800">
                {plugins.length}
              </span>
            </button>

            <button
              onClick={() => openSettings('csharpDev')}
              className={`w-full flex items-center gap-2.5 px-3 py-2 rounded-lg transition-colors cursor-pointer ${
                activeSettingsTab === 'csharpDev'
                  ? 'bg-zinc-800 text-white font-medium border border-zinc-700'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900'
              }`}
            >
              <Terminal className="w-3.5 h-3.5 text-zinc-400" />
              <span>{t('settings.tabs.csharpDev')}</span>
            </button>
          </div>

          {/* Right Content Viewport */}
          <div className="flex-1 p-6 overflow-y-auto space-y-6 text-xs font-sans">
            {activeSettingsTab === 'general' && (
              <div className="space-y-5">
                <div>
                  <h3 className="text-sm font-semibold text-white mb-1">Visual Tinting & Transitions</h3>
                  <p className="text-zinc-400 text-[11px]">
                    Adjust backdrop dimming transition duration (default 0.25s) and screen tint darkness.
                  </p>
                </div>

                {/* Fade Duration Slider */}
                <div className="p-4 rounded-xl bg-zinc-900 border border-zinc-800 space-y-3">
                  <div className="flex justify-between items-center">
                    <div>
                      <div className="font-semibold text-zinc-200">{t('settings.general.fadeDurationLabel')}</div>
                      <div className="text-zinc-400 text-[11px]">{t('settings.general.fadeDurationDesc')}</div>
                    </div>
                    <span className="font-mono text-white font-bold text-xs">{fadeDurationMs / 1000}s ({fadeDurationMs}ms)</span>
                  </div>
                  <input
                    type="range"
                    min="100"
                    max="1000"
                    step="50"
                    value={fadeDurationMs}
                    onChange={(e) => setFadeDurationMs(Number(e.target.value))}
                    className="w-full accent-zinc-100 cursor-pointer"
                  />
                </div>

                {/* Dim Opacity Slider */}
                <div className="p-4 rounded-xl bg-zinc-900 border border-zinc-800 space-y-3">
                  <div className="flex justify-between items-center">
                    <div>
                      <div className="font-semibold text-zinc-200">{t('settings.general.dimOpacityLabel')}</div>
                      <div className="text-zinc-400 text-[11px]">{t('settings.general.dimOpacityDesc')}</div>
                    </div>
                    <span className="font-mono text-white font-bold text-xs">{Math.round(overlayOpacity * 100)}%</span>
                  </div>
                  <input
                    type="range"
                    min="0.1"
                    max="0.9"
                    step="0.05"
                    value={overlayOpacity}
                    onChange={(e) => setOverlayOpacity(Number(e.target.value))}
                    className="w-full accent-zinc-100 cursor-pointer"
                  />
                </div>
              </div>
            )}

            {activeSettingsTab === 'hotkey' && (
              <div className="space-y-5">
                <div>
                  <h3 className="text-sm font-semibold text-white mb-1">{t('settings.hotkeySection.title')}</h3>
                  <p className="text-zinc-400 text-[11px]">{t('settings.hotkeySection.description')}</p>
                </div>

                <div className="p-6 rounded-xl bg-zinc-900 border border-zinc-800 text-center space-y-4">
                  <div className="text-zinc-400 text-xs">{t('hotkey.currentKey')}</div>
                  <div className="inline-block px-6 py-2.5 rounded-lg bg-black border border-zinc-700 font-mono font-bold text-xl text-white">
                    {hotkey}
                  </div>

                  {isRecordingKey ? (
                    <div className="p-4 rounded-lg bg-zinc-950 border border-zinc-700 space-y-3">
                      <p className="text-zinc-300 font-medium">{t('hotkey.listening')}</p>
                      <input
                        type="text"
                        readOnly
                        onKeyDown={handleKeyDownRecorder}
                        value={recordedKeys || 'Press key combination...'}
                        className="w-full text-center bg-zinc-900 border border-zinc-700 rounded p-2 font-mono text-xs text-white focus:outline-none"
                        autoFocus
                      />
                      <div className="flex justify-center gap-2 pt-1">
                        <button
                          onClick={confirmRecordedKey}
                          disabled={!recordedKeys}
                          className="px-4 py-1.5 rounded bg-zinc-200 text-zinc-900 font-semibold disabled:opacity-50 cursor-pointer"
                        >
                          Save
                        </button>
                        <button
                          onClick={() => setIsRecordingKey(false)}
                          className="px-4 py-1.5 rounded bg-zinc-800 text-zinc-300 cursor-pointer"
                        >
                          Cancel
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="flex justify-center gap-3">
                      <button
                        onClick={() => {
                          setIsRecordingKey(true);
                          setRecordedKeys('');
                        }}
                        className="px-4 py-2 rounded-lg bg-zinc-200 hover:bg-white text-zinc-950 font-medium text-xs cursor-pointer transition-colors"
                      >
                        {t('hotkey.changeHotkey')}
                      </button>
                      <button
                        onClick={() => setHotkey('Alt+Enter')}
                        className="px-4 py-2 rounded-lg bg-zinc-800 hover:bg-zinc-700 text-zinc-300 border border-zinc-700 font-medium text-xs cursor-pointer transition-colors flex items-center gap-1.5"
                      >
                        <RotateCcw className="w-3.5 h-3.5" />
                        <span>Reset</span>
                      </button>
                    </div>
                  )}
                </div>
              </div>
            )}

            {activeSettingsTab === 'plugins' && (
              <div className="space-y-5">
                <div>
                  <h3 className="text-sm font-semibold text-white mb-1">{t('settings.pluginsSection.title')}</h3>
                  <p className="text-zinc-400 text-[11px]">{t('settings.pluginsSection.directoryDesc')}</p>
                </div>

                <div className="p-4 rounded-xl bg-zinc-900 border border-zinc-800 space-y-2 font-mono">
                  <div className="text-zinc-400 text-xs">{t('settings.pluginsSection.directoryLabel')}:</div>
                  <div className="p-2 rounded bg-black border border-zinc-800 text-zinc-300 text-xs">
                    ./plugins/
                  </div>
                </div>

                <div className="flex items-center justify-between p-4 rounded-xl bg-zinc-900 border border-zinc-800">
                  <div>
                    <div className="font-semibold text-white">Virtual File Explorer</div>
                    <div className="text-zinc-400 text-[11px]">View metadata.json files in plugins subfolders</div>
                  </div>
                  <button
                    onClick={() => {
                      closeSettings();
                      openExplorer();
                    }}
                    className="px-3 py-1.5 rounded-lg bg-zinc-800 hover:bg-zinc-700 border border-zinc-700 text-white text-xs font-medium cursor-pointer transition-colors"
                  >
                    Open Explorer
                  </button>
                </div>
              </div>
            )}

            {activeSettingsTab === 'csharpDev' && (
              <div className="space-y-4">
                <div>
                  <h3 className="text-sm font-semibold text-white mb-1">{t('settings.csharpDev.title')}</h3>
                  <p className="text-zinc-400 text-[11px]">{t('settings.csharpDev.desc')}</p>
                </div>

                <div className="p-4 rounded-xl bg-zinc-900 border border-zinc-800 space-y-2">
                  <div className="flex justify-between items-center text-xs">
                    <span className="font-semibold text-white">C# RegisterHotKey Implementation</span>
                    <button
                      onClick={openCsharpSpec}
                      className="text-xs text-zinc-400 hover:text-white underline cursor-pointer"
                    >
                      View Full Code →
                    </button>
                  </div>
                  <pre className="p-3 rounded bg-black font-mono text-[11px] text-zinc-300 overflow-x-auto border border-zinc-800">
{`// Win32 Global Keybind Hook in C#
RegisterHotKey(handle, HOTKEY_ID, MOD_ALT, VK_RETURN);`}
                  </pre>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="h-11 px-5 bg-zinc-900 border-t border-zinc-800 flex items-center justify-between text-xs">
          <span className="text-zinc-500 font-mono">FuncOverlay Settings</span>
          <button
            onClick={closeSettings}
            className="px-4 py-1.5 rounded-lg bg-zinc-200 hover:bg-white text-zinc-950 font-medium cursor-pointer transition-colors"
          >
            {t('settings.close')}
          </button>
        </div>
      </div>
    </div>
  );
};
