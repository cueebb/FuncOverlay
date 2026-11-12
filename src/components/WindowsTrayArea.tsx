import React, { useState, useRef, useEffect } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { FuncOverlayIcon } from './FuncOverlayIcon';
import {
  Keyboard,
  Settings,
  FolderOpen,
  RotateCw,
  Power,
  ChevronUp,
} from 'lucide-react';

export const WindowsTrayArea: React.FC = () => {
  const {
    isOverlayOpen,
    toggleOverlay,
    hotkey,
    openSettings,
    openExplorer,
    restartApp,
  } = useApp();

  const [isTrayMenuOpen, setIsTrayMenuOpen] = useState(false);
  const [time, setTime] = useState(new Date());
  const trayRef = useRef<HTMLDivElement>(null);

  // Update clock every minute
  useEffect(() => {
    const timer = setInterval(() => setTime(new Date()), 30000);
    return () => clearInterval(timer);
  }, []);

  // Click outside listener
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (trayRef.current && !trayRef.current.contains(e.target as Node)) {
        setIsTrayMenuOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const timeString = time.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const dateString = time.toLocaleDateString([], { month: 'numeric', day: 'numeric', year: 'numeric' });

  return (
    <div
      ref={trayRef}
      className="fixed bottom-3 right-3 z-40 select-none flex items-center gap-2"
    >
      {/* Windows 11 Notification Area Capsule */}
      <div className="flex items-center gap-1.5 p-1 rounded-xl bg-zinc-950/85 border border-zinc-800 backdrop-blur-md shadow-lg text-xs font-sans">
        
        {/* System Time & Date Widget */}
        <div className="px-2 py-0.5 text-right font-mono text-[10px] text-zinc-400 border-r border-zinc-800/80 leading-tight">
          <div className="text-zinc-200 font-semibold">{timeString}</div>
          <div className="text-zinc-500 text-[9px]">{dateString}</div>
        </div>

        {/* FuncOverlay Tray Icon Trigger */}
        <button
          onClick={() => setIsTrayMenuOpen((prev) => !prev)}
          onContextMenu={(e) => {
            e.preventDefault();
            setIsTrayMenuOpen(true);
          }}
          className={`relative p-1.5 rounded-lg transition-all cursor-pointer flex items-center gap-1 ${
            isTrayMenuOpen
              ? 'bg-zinc-800 text-white border border-zinc-700 shadow-sm'
              : 'hover:bg-zinc-900 text-zinc-300'
          }`}
          title={`${t('app.name')} - ${t('tray.tooltip')}`}
        >
          {/* Custom Monochrome FuncOverlay Icon */}
          <FuncOverlayIcon size={18} className="w-4.5 h-4.5" />
          
          {/* Active Status Pip (White when overlay is open) */}
          <span
            className={`w-1.5 h-1.5 rounded-full transition-all ${
              isOverlayOpen ? 'bg-white shadow-[0_0_8px_rgba(255,255,255,0.8)]' : 'bg-zinc-600'
            }`}
          />

          <ChevronUp
            className={`w-3 h-3 text-zinc-500 transition-transform ${
              isTrayMenuOpen ? 'rotate-180 text-white' : ''
            }`}
          />
        </button>
      </div>

      {/* TRAY CONTEXT MENU: Black and White Fluent Theme */}
      {isTrayMenuOpen && (
        <div className="absolute bottom-12 right-0 w-64 rounded-2xl bg-zinc-950/95 border border-zinc-800 p-2 text-zinc-100 shadow-2xl backdrop-blur-xl animate-in slide-in-from-bottom-2 duration-150 z-50 font-sans">
          
          {/* Header */}
          <div className="px-3 py-2.5 border-b border-zinc-800 flex items-center justify-between mb-1">
            <div className="flex items-center gap-2">
              <FuncOverlayIcon size={20} className="w-5 h-5" />
              <div>
                <h4 className="text-xs font-bold text-white leading-tight font-sans">
                  {t('app.name')}
                </h4>
                <p className="text-[10px] text-zinc-400 font-mono">
                  {t('tray.headerDesc')}
                </p>
              </div>
            </div>
            <span className="px-1.5 py-0.5 rounded bg-black text-zinc-300 font-mono text-[9px] border border-zinc-800">
              {isOverlayOpen ? 'Active' : 'Standby'}
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
              className="w-full flex items-center justify-between px-3 py-2 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer"
            >
              <div className="flex items-center gap-2.5">
                <Keyboard className="w-4 h-4 text-zinc-400" />
                <span className="font-medium">{t('tray.toggleOverlay')}</span>
              </div>
              <kbd className="px-1.5 py-0.5 rounded bg-black text-zinc-300 font-mono text-[10px] border border-zinc-800">
                {hotkey}
              </kbd>
            </button>

            {/* 2. Settings (Opens the redesigned settings window) */}
            <button
              onClick={() => {
                openSettings('general');
                setIsTrayMenuOpen(false);
              }}
              className="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer"
            >
              <Settings className="w-4 h-4 text-zinc-400" />
              <span className="font-medium">{t('tray.openSettings')}</span>
            </button>

            {/* 3. Open Plugins Folder */}
            <button
              onClick={() => {
                openExplorer();
                setIsTrayMenuOpen(false);
              }}
              className="w-full flex items-center justify-between px-3 py-2 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer"
            >
              <div className="flex items-center gap-2.5">
                <FolderOpen className="w-4 h-4 text-zinc-400" />
                <span className="font-medium">{t('tray.openPluginsFolder')}</span>
              </div>
              <span className="text-[10px] text-zinc-500 font-mono">./plugins</span>
            </button>

            <div className="my-1 border-t border-zinc-800" />

            {/* 4. Restart Application */}
            <button
              onClick={() => {
                restartApp();
                setIsTrayMenuOpen(false);
              }}
              className="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer"
            >
              <RotateCw className="w-4 h-4 text-zinc-400" />
              <span className="font-medium">{t('tray.restartApp')}</span>
            </button>

            {/* 5. Exit FuncOverlay */}
            <button
              onClick={() => {
                toggleOverlay();
                setIsTrayMenuOpen(false);
              }}
              className="w-full flex items-center gap-2.5 px-3 py-2 rounded-lg hover:bg-zinc-900 text-zinc-300 transition-colors cursor-pointer"
            >
              <Power className="w-4 h-4 text-zinc-400" />
              <span className="font-medium">{t('tray.exitApp')}</span>
            </button>
          </div>

        </div>
      )}
    </div>
  );
};
