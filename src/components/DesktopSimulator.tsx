import React, { useEffect } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { Terminal, Folder, Github, Settings, Play, RefreshCw, Cpu, Keyboard } from 'lucide-react';

export const DesktopSimulator: React.FC = () => {
  const {
    isOverlayOpen,
    toggleOverlay,
    hotkey,
    openSettings,
    openExplorer,
    openCsharpSpec,
    restartApp,
    toastMessage,
    plugins,
    addSamplePlugin,
  } = useApp();

  // Listen for global hotkey (default Alt+Enter)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Parse keybind like "Alt+Enter" or "Ctrl+Shift+O"
      const parts = hotkey.toLowerCase().split('+');
      const needAlt = parts.includes('alt');
      const needCtrl = parts.includes('ctrl') || parts.includes('control');
      const needShift = parts.includes('shift');
      const targetKey = parts[parts.length - 1];

      let keyPressed = e.key.toLowerCase();
      if (e.code.toLowerCase() === 'enter') keyPressed = 'enter';

      if (
        e.altKey === needAlt &&
        e.ctrlKey === needCtrl &&
        e.shiftKey === needShift &&
        keyPressed === targetKey
      ) {
        e.preventDefault();
        toggleOverlay();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [hotkey, toggleOverlay]);

  return (
    <div className="relative w-screen h-screen overflow-hidden select-none bg-gradient-to-br from-slate-950 via-slate-900 to-slate-950 font-sans">
      {/* Simulated Windows 11 Wallpaper Accent Lights */}
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_50%_30%,rgba(37,99,235,0.15),transparent_60%)] pointer-events-none" />
      <div className="absolute top-1/4 left-1/3 w-96 h-96 bg-blue-600/10 rounded-full blur-3xl pointer-events-none" />
      <div className="absolute bottom-1/3 right-1/4 w-96 h-96 bg-indigo-600/10 rounded-full blur-3xl pointer-events-none" />

      {/* Top Banner Toolbar for Simulation Convenience */}
      <header className="absolute top-0 left-0 right-0 h-12 bg-slate-900/60 backdrop-blur-md border-b border-white/10 px-6 flex items-center justify-between z-10 text-xs">
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2 font-semibold text-blue-400">
            <Cpu className="w-4 h-4 text-blue-400 animate-pulse" />
            <span className="tracking-wide">{t('app.name')}</span>
          </div>
          <span className="text-slate-600 font-mono">|</span>
          <span className="text-slate-400 font-medium hidden sm:inline">{t('app.tagline')}</span>
        </div>

        <div className="flex items-center gap-3">
          {/* Global Hotkey Badge Button */}
          <button
            onClick={toggleOverlay}
            className="flex items-center gap-2 px-3 py-1.5 rounded-md bg-blue-600/20 hover:bg-blue-600/30 text-blue-300 border border-blue-500/30 transition-all font-mono active:scale-95 cursor-pointer"
            title={t('hotkey.pressToToggle', { hotkey })}
          >
            <Keyboard className="w-3.5 h-3.5 text-blue-400" />
            <span>{t('hotkey.shortcutLabel')}:</span>
            <kbd className="px-1.5 py-0.5 rounded bg-blue-500/30 text-blue-100 font-bold text-[11px] shadow-xs">
              {hotkey}
            </kbd>
            <span className="ml-1 text-[10px] text-blue-300/70 font-sans">
              ({isOverlayOpen ? 'Click to Hide' : 'Click to Show'})
            </span>
          </button>

          <button
            onClick={() => openExplorer()}
            className="flex items-center gap-1.5 px-2.5 py-1.5 rounded bg-slate-800/80 hover:bg-slate-700 text-slate-300 border border-white/10 transition-colors cursor-pointer"
          >
            <Folder className="w-3.5 h-3.5 text-amber-400" />
            <span className="hidden md:inline">plugins/</span>
            <span className="text-[10px] bg-slate-700 px-1.5 py-0.5 rounded-full text-amber-300 font-mono">
              {plugins.length}
            </span>
          </button>

          <button
            onClick={openCsharpSpec}
            className="flex items-center gap-1.5 px-2.5 py-1.5 rounded bg-slate-800/80 hover:bg-slate-700 text-slate-300 border border-white/10 transition-colors cursor-pointer"
          >
            <Terminal className="w-3.5 h-3.5 text-emerald-400" />
            <span className="hidden md:inline">C# Code</span>
          </button>

          <button
            onClick={() => openSettings('general')}
            className="p-1.5 rounded bg-slate-800/80 hover:bg-slate-700 text-slate-300 border border-white/10 transition-colors cursor-pointer"
            title={t('settings.title')}
          >
            <Settings className="w-4 h-4" />
          </button>
        </div>
      </header>

      {/* Simulated Desktop Icons Container */}
      <div className="absolute top-16 left-6 grid grid-cols-1 gap-4 z-0">
        <div
          onClick={toggleOverlay}
          className="w-24 h-24 rounded-xl flex flex-col items-center justify-center gap-2 p-2 text-center text-slate-300 hover:bg-white/10 hover:border hover:border-white/20 transition-all cursor-pointer group"
        >
          <div className="w-12 h-12 rounded-xl bg-gradient-to-tr from-blue-600 to-indigo-500 flex items-center justify-center shadow-lg group-hover:scale-105 transition-transform">
            <Cpu className="w-6 h-6 text-white" />
          </div>
          <span className="text-xs font-medium drop-shadow-md leading-tight">FuncOverlay</span>
        </div>

        <div
          onClick={() => openExplorer()}
          className="w-24 h-24 rounded-xl flex flex-col items-center justify-center gap-2 p-2 text-center text-slate-300 hover:bg-white/10 hover:border hover:border-white/20 transition-all cursor-pointer group"
        >
          <div className="w-12 h-12 rounded-xl bg-gradient-to-tr from-amber-500 to-amber-600 flex items-center justify-center shadow-lg group-hover:scale-105 transition-transform">
            <Folder className="w-6 h-6 text-white" />
          </div>
          <span className="text-xs font-medium drop-shadow-md leading-tight">plugins/ Folder</span>
        </div>

        <a
          href="https://github.com/FuncOverlay/plugins"
          target="_blank"
          rel="noreferrer"
          className="w-24 h-24 rounded-xl flex flex-col items-center justify-center gap-2 p-2 text-center text-slate-300 hover:bg-white/10 hover:border hover:border-white/20 transition-all cursor-pointer group"
        >
          <div className="w-12 h-12 rounded-xl bg-slate-800 border border-slate-700 flex items-center justify-center shadow-lg group-hover:scale-105 transition-transform">
            <Github className="w-6 h-6 text-white" />
          </div>
          <span className="text-xs font-medium drop-shadow-md leading-tight">Official GitHub</span>
        </a>

        {plugins.length === 0 && (
          <div
            onClick={() => addSamplePlugin('clock', 'python')}
            className="w-24 h-24 rounded-xl flex flex-col items-center justify-center gap-2 p-2 text-center text-slate-300 hover:bg-white/10 hover:border hover:border-white/20 transition-all cursor-pointer group border border-dashed border-blue-500/40"
          >
            <div className="w-12 h-12 rounded-xl bg-blue-600/30 flex items-center justify-center group-hover:scale-105 transition-transform">
              <Play className="w-6 h-6 text-blue-400" />
            </div>
            <span className="text-[11px] font-medium text-blue-300 leading-tight">+ Quick Plugin</span>
          </div>
        )}
      </div>

      {/* Desktop Background Watermark */}
      <div className="absolute bottom-16 right-8 text-right text-slate-500/40 pointer-events-none select-none">
        <p className="text-2xl font-bold tracking-tight text-slate-400/30">Windows 11 Pro</p>
        <p className="text-xs font-mono">FuncOverlay Native Host Edition (C# WPF / Python 3.11)</p>
      </div>

      {/* Floating System Event Toast */}
      {toastMessage && (
        <div className="fixed top-16 right-6 z-50 animate-in fade-in slide-in-from-top-4 duration-200">
          <div className="fluent-glass px-4 py-2.5 rounded-lg border border-blue-500/40 text-xs font-medium text-slate-200 flex items-center gap-2 shadow-xl">
            <RefreshCw className="w-3.5 h-3.5 text-blue-400 animate-spin" />
            <span>{toastMessage}</span>
          </div>
        </div>
      )}
    </div>
  );
};
