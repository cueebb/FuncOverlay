import React, { useState, useRef, useEffect } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import {
  Settings,
  FolderOpen,
  Terminal,
  RotateCw,
  Power,
  ChevronUp,
  Keyboard,
  Cpu,
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
    <div className="fixed bottom-0 left-0 right-0 h-11 bg-zinc-950 border-t border-zinc-800 z-40 flex items-center justify-between px-4 select-none">
      {/* Taskbar Left: Minimal Brand Wordmark & Hotkey Indicator */}
      <div className="flex items-center gap-3">
        <button
          onClick={toggleOverlay}
          className="flex items-center gap-2 px-2.5 py-1 rounded-md bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 text-zinc-200 transition-colors cursor-pointer text-xs"
        >
          <Cpu className="w-3.5 h-3.5 text-zinc-400" />
          <span className="font-medium text-white">{t('app.name')}</span>
          <kbd className="px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-300 font-mono text-[10px] border border-zinc-700">
            {hotkey}
          </kbd>
        </button>
      </div>

      {/* Taskbar Right: System Tray Menu Trigger */}
      <div className="flex items-center gap-2" ref={trayRef}>
        <div className="relative">
          <button
            onClick={() => setIsTrayMenuOpen((prev) => !prev)}
            className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md border text-xs transition-all cursor-pointer ${
              isTrayMenuOpen
                ? 'bg-zinc-800 border-zinc-700 text-white'
                : 'bg-zinc-900 hover:bg-zinc-800 border-zinc-800 text-zinc-300'
            }`}
            title={t('tray.tooltip')}
          >
            <Cpu className="w-3.5 h-3.5 text-zinc-400" />
            <span className="font-mono text-[11px] text-zinc-400">Tray</span>
            <ChevronUp className={`w-3.5 h-3.5 text-zinc-400 transition-transform ${isTrayMenuOpen ? 'rotate-180' : ''}`} />
          </button>

          {/* MONOCHROMATIC TRAY POPUP MENU */}
          {isTrayMenuOpen && (
            <div className="absolute bottom-13 right-0 w-64 rounded-xl bg-zinc-950 border border-zinc-800 p-1.5 text-zinc-100 shadow-2xl animate-in slide-in-from-bottom-2 duration-150 z-50">
              {/* Header */}
              <div className="px-3 py-2 border-b border-zinc-800 flex items-center justify-between mb-1">
                <div>
                  <h4 className="text-xs font-bold text-white">{t('app.name')}</h4>
                  <p className="text-[10px] text-zinc-400 font-mono">System Tray Service</p>
                </div>
                <span className="px-1.5 py-0.5 rounded bg-zinc-900 text-zinc-300 font-mono text-[10px] border border-zinc-800">
                  {isOverlayOpen ? 'Overlay Open' : 'Standby'}
                </span>
              </div>

              {/* Menu Items */}
              <div className="space-y-0.5 text-xs font-sans">
                {/* 1. Toggle Overlay */}
                <button
                  onClick={() => {
                    toggleOverlay();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center justify-between px-3 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer"
                >
                  <div className="flex items-center gap-2">
                    <Keyboard className="w-3.5 h-3.5 text-zinc-400" />
                    <span>{t('tray.toggleOverlay')}</span>
                  </div>
                  <kbd className="px-1.5 py-0.5 rounded bg-zinc-900 text-zinc-400 font-mono text-[10px] border border-zinc-800">
                    {hotkey}
                  </kbd>
                </button>

                {/* 2. Settings */}
                <button
                  onClick={() => {
                    openSettings('general');
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center gap-2 px-3 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer"
                >
                  <Settings className="w-3.5 h-3.5 text-zinc-400" />
                  <span>{t('tray.openSettings')}</span>
                </button>

                {/* 3. Open Plugins Folder */}
                <button
                  onClick={() => {
                    openExplorer();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center justify-between px-3 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer"
                >
                  <div className="flex items-center gap-2">
                    <FolderOpen className="w-3.5 h-3.5 text-zinc-400" />
                    <span>{t('tray.openPluginsFolder')}</span>
                  </div>
                  <span className="text-[10px] text-zinc-500 font-mono">./plugins</span>
                </button>

                {/* 4. C# Code Blueprint */}
                <button
                  onClick={() => {
                    openCsharpSpec();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center gap-2 px-3 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer"
                >
                  <Terminal className="w-3.5 h-3.5 text-zinc-400" />
                  <span>{t('tray.csharpGuide')}</span>
                </button>

                <div className="my-1 border-t border-zinc-800" />

                {/* 5. Restart Application */}
                <button
                  onClick={() => {
                    restartApp();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center gap-2 px-3 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer"
                >
                  <RotateCw className="w-3.5 h-3.5 text-zinc-400" />
                  <span>{t('tray.restartApp')}</span>
                </button>

                {/* 6. Exit Application */}
                <button
                  onClick={() => {
                    toggleOverlay();
                    setIsTrayMenuOpen(false);
                  }}
                  className="w-full flex items-center gap-2 px-3 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-300 transition-colors cursor-pointer"
                >
                  <Power className="w-3.5 h-3.5 text-zinc-400" />
                  <span>{t('tray.exitApp')}</span>
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
