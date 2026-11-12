import React, { useEffect } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { Keyboard, RefreshCw } from 'lucide-react';

export const DesktopSimulator: React.FC = () => {
  const { isOverlayOpen, toggleOverlay, hotkey, toastMessage } = useApp();

  // Listen for global hotkey (default Alt+Enter)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
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
    <div className="relative w-screen h-screen overflow-hidden select-none bg-zinc-950 font-sans">
      {/* Quiet Screen Content Placeholder (Simulating workspace background before overlay tint) */}
      <div className="absolute inset-0 flex flex-col items-center justify-center p-8 text-center pointer-events-none">
        <div className="max-w-md space-y-3 opacity-30">
          <p className="text-xs font-mono tracking-widest text-zinc-400 uppercase">FuncOverlay Environment</p>
          <p className="text-sm text-zinc-300 font-sans">
            Screen tint triggers on keybind press ({hotkey}).
          </p>
        </div>
      </div>

      {/* Floating System Toast */}
      {toastMessage && (
        <div className="fixed top-6 right-6 z-50 animate-in fade-in slide-in-from-top-4 duration-200">
          <div className="bg-zinc-900/90 backdrop-blur-md px-4 py-2.5 rounded-lg border border-zinc-700 text-xs font-medium text-zinc-200 flex items-center gap-2 shadow-xl">
            <RefreshCw className="w-3.5 h-3.5 text-zinc-400 animate-spin" />
            <span>{toastMessage}</span>
          </div>
        </div>
      )}
    </div>
  );
};
