import React, { useState, useRef, useEffect } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import {
  Cpu,
  Settings,
  FolderOpen,
  Terminal,
  RotateCw,
  Power,
  ChevronUp,
  Wifi,
  Volume2,
  Battery,
  Keyboard,
  CheckCircle2,
} from 'lucide-react';

export const SystemTrayMenu: React.FC = () => {
  const {
    isOverlayOpen,
    toggleOverlay,
    hotkey,
    openSettings,
    openExplorer,
    openCsharpSpec,
    restartApp,
    plugins,
  } = useApp();

  const [isTrayMenuOpen, setIsTrayMenuOpen] = useState<boolean>(false);
  const trayRef = useRef<HTMLDivElement>(null);

  // Close tray menu on click outside
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (trayRef.current && !trayRef.current.contains(e.target as Node)) {
        setIsTrayMenuOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  return (
    <div className="fixed bottom-0 left-0 right-0 h-12 bg-slate-950/80 backdrop-blur-xl border-t border-white/10 z-40 flex items-center justify-between px-4 select-none">
      {/* Windows 11 Taskbar Left/Center Brand Icon */}
      <div className="flex items-center gap-3">
        <button
          onClick={toggleOverlay}
          className="flex items-center gap-2 px-3 py-1.5 rounded-lg bg-slate-900 hover:bg-slate-800 border border-white/10 text-slate-200 transition-colors cursor-pointer"
        >
          <div className="w-5 h-5 rounded bg-blue-600 flex items-center justify-center text-white">
            <Cpu className="w-3.5 h-3.5" />
          </div>
          <span className="text-xs font-semibold">{t('app.name')}</span>
          <span className="text-[10px] px-1.5 py-0.5 rounded bg-blue-500/20 text-blue-300 font-mono border border-blue-500/30">
            {hotkey}
          </span>
        </button>

        <span className="text-slate-600 font-mono text-xs hidden sm:inline">|</span>

        <span className="text-xs text-slate-400 font-medium hidden sm:inline">
          {plugins.length === 0
            ? '0 plugins loaded (Center setup window open)'
            : `${plugins.length} active plugin module${plugins.length > 1 ? 's' : ''}`}
        </span>
      </div>

      {/* Windows 11 Taskbar Right: System Tray Area */}
      <div className="flex items-center gap-2" ref={trayRef}>
        {/* System Indicators */}
        <div className="hidden md:flex items-center gap-2 text-slate-400 px-2 py-1">
          <Wifi className="w-3.5 h-3.5" />
          <Volume2 className="w-3.5 h-3.5" />
          <Battery className="w-3.5 h-3.5" />
        </div>

        {/* Taskbar Clock */}
        <div className="hidden sm:block text-right px-2 text-slate-300 text-[11px] font-mono leading-tight">
          <div>{new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</div>
          <div className="text-slate-500 text-[10px]">{new Date().toLocaleDateString([], { month: 'short', day: 'numeric' })}</div>
        </div>

        {/* System Tray Icon Trigger Button */}
        <div className="relative">
          <button
            onClick={() => setIsTrayMenuOpen((prev) => !prev)}
            className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border transition-all cursor-pointer ${
              isTrayMenuOpen
                ? 'bg-blue-600/30 border-blue-500/50 text-white shadow-lg'
                : 'bg-slate-900/90 hover:bg-slate-800 border-white/10 text-slate-300'
            }`}
            title={t('tray.tooltip')}
          >
            <div className="relative">
              <Cpu className="w-4 h-4 text-blue-400" />
              <span className="absolute -top-1 -right-1 w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
            </div>
            <ChevronUp className={`w-3.5 h-3.5 text-slate-400 transition-transform ${isTrayMenuOpen ? 'rotate-180' : ''}`} />
          </button>

          {/* SYSTEM TRAY POPUP CONTEXT MENU */}
          {isTrayMenuOpen && (
            <div className="absolute bottom-14 right-0 w-72 rounded-2xl fluent-glass p-2 border border-white/15 text-slate-100 shadow-2xl animate-in slide-in-from-bottom-2 duration-150 z-50">
              {/* Menu Header */}
              <div className="px-3 py-2.5 border-b border-white/10 flex items-center justify-between mb-1">
                <div className="flex items-center gap-2">
                  <div className="w-6 h-6 rounded bg-gradient-to-tr from-blue-600 to-indigo-500 flex items-center justify-center text-white">
                    <Cpu className="w-3.5 h-3.5" />
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-white leading-tight">{t('app.name')}</h4>
                    <p className="text-[10px] text-slate-400 font-mono">C# Win32 Tray Service</p>
                  </div>
                </div>
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-emerald-500/20 text-emerald-300 text-[10px] font-medium border border-emerald-500/30">
                  <CheckCircle2 className="w-2.5 h-2.5 text-emerald-400" />
                  <span>{isOverlayOpen ? 'Visible' : 'Standby'}</span>
                </span>
              </div>

              {/* Menu Items */}
              <div className="space-y-0.5 text-xs">
                {/* 1. Toggle Overlay */}
                <button
                  onClick={() => {
                    toggleOverlay();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center justify-between px-3 py-2 rounded-lg hover:bg-white/10 text-slate-200 transition-colors cursor-pointer group"
                >
                  <div className="flex items-center gap-2.5">
                    <Keyboard className="w-4 h-4 text-blue-400 group-hover:scale-110 transition-transform" />
                    <span className="font-medium">{t('tray.toggleOverlay')}</span>
                  </div>
                  <kbd className="px-1.5 py-0.5 rounded bg-blue-500/20 text-blue-300 font-mono text-[10px] border border-blue-500/30">
                    {hotkey}
                  </kbd>
                </button>

                {/* 2. Settings */}
                <button
                  onClick={() => {
                    openSettings('general');
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-white/10 text-slate-200 transition-colors cursor-pointer group"
                >
                  <Settings className="w-4 h-4 text-slate-400 group-hover:text-white transition-colors" />
                  <span className="font-medium">{t('tray.openSettings')}</span>
                </button>

                {/* 3. Open Plugins Folder */}
                <button
                  onClick={() => {
                    openExplorer();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center justify-between px-3 py-2 rounded-lg hover:bg-white/10 text-slate-200 transition-colors cursor-pointer group"
                >
                  <div className="flex items-center gap-2.5">
                    <FolderOpen className="w-4 h-4 text-amber-400 group-hover:scale-110 transition-transform" />
                    <span className="font-medium">{t('tray.openPluginsFolder')}</span>
                  </div>
                  <span className="text-[10px] text-slate-400 font-mono">./plugins</span>
                </button>

                {/* 4. C# Host App Blueprint */}
                <button
                  onClick={() => {
                    openCsharpSpec();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-white/10 text-slate-200 transition-colors cursor-pointer group"
                >
                  <Terminal className="w-4 h-4 text-emerald-400 group-hover:scale-110 transition-transform" />
                  <span className="font-medium">{t('tray.csharpGuide')}</span>
                </button>

                <div className="my-1 border-t border-white/10" />

                {/* 5. Restart Application */}
                <button
                  onClick={() => {
                    restartApp();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-emerald-500/20 text-emerald-300 transition-colors cursor-pointer group"
                >
                  <RotateCw className="w-4 h-4 text-emerald-400 group-hover:rotate-180 transition-transform duration-500" />
                  <span className="font-medium">{t('tray.restartApp')}</span>
                </button>

                {/* 6. Exit Application */}
                <button
                  onClick={() => {
                    toggleOverlay();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-red-500/20 text-red-300 transition-colors cursor-pointer group"
                >
                  <Power className="w-4 h-4 text-red-400 group-hover:scale-110 transition-transform" />
                  <span className="font-medium">{t('tray.exitApp')}</span>
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
