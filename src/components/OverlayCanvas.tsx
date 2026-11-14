import React, { useState, useRef, useEffect } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { PluginFolder } from '../types/plugin';
import {
  Github,
  FolderOpen,
  RotateCw,
  X,
  GripHorizontal,
  FileCode,
  ExternalLink,
  Clock,
  Activity,
  FileText,
  Settings,
} from 'lucide-react';

export const OverlayCanvas: React.FC = () => {
  const {
    isOverlayOpen,
    isFading,
    overlayOpacity,
    fadeDurationMs,
    plugins,
    hotkey,
    openSettings,
    openExplorer,
    openCsharpSpec,
    restartApp,
    toggleOverlay,
    setOverlayOpen,
  } = useApp();

  const [contextMenu, setContextMenu] = useState<{ x: number; y: number } | null>(null);

  // If overlay is hidden and not fading out, do not render content
  if (!isOverlayOpen && !isFading) return null;

  const handleContextMenu = (e: React.MouseEvent) => {
    if ((e.target as HTMLElement).closest('button, a, input, textarea')) return;
    e.preventDefault();
    setContextMenu({ x: e.clientX, y: e.clientY });
  };

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (contextMenu) {
      setContextMenu(null);
      return;
    }
    if (e.target === e.currentTarget) {
      setOverlayOpen(false);
    }
  };

  return (
    <div
      onClick={handleBackdropClick}
      onContextMenu={handleContextMenu}
      className="fixed inset-0 z-30 flex items-center justify-center select-none overflow-hidden"
      style={{
        backgroundColor: `rgba(0, 0, 0, ${overlayOpacity})`,
        backdropFilter: 'blur(12px)',
        opacity: isOverlayOpen ? 1 : 0,
        transition: `opacity ${fadeDurationMs}ms cubic-bezier(0.16, 1, 0.3, 1), backdrop-filter ${fadeDurationMs}ms cubic-bezier(0.16, 1, 0.3, 1)`,
        pointerEvents: isOverlayOpen ? 'auto' : 'none',
      }}
    >
      {/* CASE 1: NO PLUGINS INSTALLED -> SHOW SINGLE CENTER FLUENT INSTALLATION GUIDE WINDOW */}
      {plugins.length === 0 ? (
        <NoPluginsCenterWindow />
      ) : (
        /* CASE 2: PLUGINS INSTALLED -> RENDER DRAGGABLE FLUENT PLUGIN MODULE WIDGETS */
        <div
          onClick={handleBackdropClick}
          className="absolute inset-0 overflow-hidden pointer-events-auto"
        >
          {plugins.map((pluginFolder) => (
            <PluginWidgetCard key={pluginFolder.metadata.id} pluginFolder={pluginFolder} />
          ))}
        </div>
      )}

      {/* TRAY CONTEXT MENU ON RIGHT CLICK (TINTED SCREEN CONTEXT) */}
      {contextMenu && (
        <div
          className="fixed z-50 w-56 rounded-xl bg-zinc-950/95 border border-zinc-800 p-1.5 shadow-2xl backdrop-blur-md text-xs font-sans animate-in fade-in zoom-in-95 duration-100"
          style={{
            left: Math.min(contextMenu.x, window.innerWidth - 230),
            top: Math.min(contextMenu.y, window.innerHeight - 210),
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <div className="px-2.5 py-1.5 border-b border-zinc-800 text-[10px] font-mono text-zinc-500 flex justify-between items-center">
            <span>FuncOverlay</span>
            <span className="text-zinc-400">{hotkey}</span>
          </div>
          <div className="space-y-0.5 mt-1">
            <button
              onClick={() => {
                setContextMenu(null);
                toggleOverlay();
              }}
              className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer text-left"
            >
              <span>{t('tray.toggleOverlay')}</span>
              <kbd className="text-[10px] font-mono text-zinc-500">{hotkey}</kbd>
            </button>
            <button
              onClick={() => {
                setContextMenu(null);
                openSettings('general');
              }}
              className="w-full flex items-center gap-2 px-2.5 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer text-left"
            >
              <span>{t('tray.openSettings')}</span>
            </button>
            <button
              onClick={() => {
                setContextMenu(null);
                openExplorer();
              }}
              className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer text-left"
            >
              <span>{t('tray.openPluginsFolder')}</span>
              <span className="text-[10px] font-mono text-zinc-500">./plugins</span>
            </button>
            <button
              onClick={() => {
                setContextMenu(null);
                openCsharpSpec();
              }}
              className="w-full flex items-center gap-2 px-2.5 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-200 transition-colors cursor-pointer text-left"
            >
              <span>{t('tray.csharpGuide')}</span>
            </button>
            <div className="my-1 border-t border-zinc-800" />
            <button
              onClick={() => {
                setContextMenu(null);
                restartApp();
              }}
              className="w-full flex items-center gap-2 px-2.5 py-1.5 rounded-lg hover:bg-zinc-900 text-zinc-300 transition-colors cursor-pointer text-left"
            >
              <span>{t('tray.restartApp')}</span>
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

/* ========================================================================= */
/* RESTRAINED MONOCHROMATIC "HOW TO INSTALL PLUGINS" CENTER WINDOW */
/* ========================================================================= */
const NoPluginsCenterWindow: React.FC = () => {
  const { openExplorer, restartApp, openSettings } = useApp();

  return (
    <div
      onClick={(e) => e.stopPropagation()}
      className="w-[540px] max-w-[92vw] rounded-2xl bg-zinc-950/90 border border-zinc-800 p-8 text-zinc-100 shadow-2xl backdrop-blur-xl animate-in zoom-in-95 duration-200"
    >
      {/* Header */}
      <div className="mb-6 pb-4 border-b border-zinc-800">
        <h2 className="text-xl font-bold tracking-tight text-white font-sans">
          {t('overlay.noPlugins.title')}
        </h2>
        <p className="text-xs text-zinc-400 mt-1 leading-relaxed">
          {t('overlay.noPlugins.subtitle')}
        </p>
      </div>

      {/* 3 Installation Steps - Strictly Monochromatic & Restrained */}
      <div className="space-y-3.5">
        {/* Step 1 */}
        <div className="p-4 rounded-xl bg-zinc-900/80 border border-zinc-800 flex items-start gap-3.5 hover:border-zinc-700 transition-colors group">
          <div className="w-7 h-7 rounded-lg bg-zinc-800 border border-zinc-700 text-zinc-200 font-mono font-semibold flex items-center justify-center shrink-0 text-xs">
            1
          </div>
          <div className="flex-1">
            <h3 className="text-xs font-semibold text-white group-hover:text-zinc-200 transition-colors">
              {t('overlay.noPlugins.step1.title')}
            </h3>
            <p className="text-xs text-zinc-400 mt-0.5 leading-normal">
              {t('overlay.noPlugins.step1.desc')}
            </p>
            <a
              href="https://github.com/FuncOverlay/plugins"
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1.5 text-xs text-zinc-300 hover:text-white mt-2 font-medium transition-colors"
            >
              <Github className="w-3.5 h-3.5 text-zinc-400" />
              <span>{t('overlay.noPlugins.step1.actionText')}</span>
              <ExternalLink className="w-3 h-3 text-zinc-500" />
            </a>
          </div>
        </div>

        {/* Step 2 */}
        <div className="p-4 rounded-xl bg-zinc-900/80 border border-zinc-800 flex items-start gap-3.5 hover:border-zinc-700 transition-colors group">
          <div className="w-7 h-7 rounded-lg bg-zinc-800 border border-zinc-700 text-zinc-200 font-mono font-semibold flex items-center justify-center shrink-0 text-xs">
            2
          </div>
          <div className="flex-1">
            <h3 className="text-xs font-semibold text-white group-hover:text-zinc-200 transition-colors">
              {t('overlay.noPlugins.step2.title')}
            </h3>
            <p className="text-xs text-zinc-400 mt-0.5 leading-normal">
              {t('overlay.noPlugins.step2.desc')}
            </p>
            <div className="mt-2 px-3 py-1.5 rounded bg-black/60 border border-zinc-800 font-mono text-[11px] text-zinc-300 flex items-center justify-between">
              <span>{t('overlay.noPlugins.step2.pathHint')}</span>
              <button
                onClick={() => openExplorer()}
                className="text-zinc-300 hover:text-white cursor-pointer flex items-center gap-1 text-xs"
              >
                <FolderOpen className="w-3 h-3 text-zinc-400" />
                <span>Open Folder</span>
              </button>
            </div>
          </div>
        </div>

        {/* Step 3 */}
        <div className="p-4 rounded-xl bg-zinc-900/80 border border-zinc-800 flex items-start gap-3.5 hover:border-zinc-700 transition-colors group">
          <div className="w-7 h-7 rounded-lg bg-zinc-800 border border-zinc-700 text-zinc-200 font-mono font-semibold flex items-center justify-center shrink-0 text-xs">
            3
          </div>
          <div className="flex-1">
            <h3 className="text-xs font-semibold text-white group-hover:text-zinc-200 transition-colors">
              {t('overlay.noPlugins.step3.title')}
            </h3>
            <p className="text-xs text-zinc-400 mt-0.5 leading-normal">
              {t('overlay.noPlugins.step3.desc')}
            </p>
            <button
              onClick={restartApp}
              className="mt-2.5 inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-lg bg-black text-white font-medium border border-white hover:bg-zinc-900 transition-colors text-xs cursor-pointer active:scale-95"
            >
              <RotateCw className="w-3.5 h-3.5 text-zinc-300" />
              <span>{t('overlay.noPlugins.step3.actionText')}</span>
            </button>
          </div>
        </div>
      </div>

      {/* Footer quick action to open Settings */}
      <div className="mt-6 pt-4 border-t border-zinc-800/80 flex items-center justify-between text-xs">
        <span className="text-[11px] font-mono text-zinc-500">FuncOverlay</span>
        <button
          onClick={() => openSettings('general')}
          className="bg-white text-black font-semibold hover:bg-zinc-200 transition-colors cursor-pointer rounded-lg px-3.5 py-1.5 text-xs shadow-sm flex items-center gap-1.5 active:scale-95"
        >
          <Settings className="w-3.5 h-3.5" />
          <span>{t('tray.openSettings')}</span>
        </button>
      </div>
    </div>
  );
};

/* ========================================================================= */
/* DRAGGABLE UNIFIED GRAY PLUGIN MODULE WIDGET CARD                           */
/* ========================================================================= */
const PluginWidgetCard: React.FC<{ pluginFolder: PluginFolder }> = ({ pluginFolder }) => {
  const { updatePluginPosition, deletePlugin, openExplorer } = useApp();
  const { metadata, folderName } = pluginFolder;
  const { id, name, entryPoint, language, position, size, enabled, widgetType } = metadata;
  const isObsShortcuts = widgetType === 'obs_shortcuts' || id === 'obs_shortcuts';

  const [pos, setPos] = useState({ x: position.x || 100, y: position.y || 100 });
  const [dim] = useState({
    width: size.width || (isObsShortcuts ? 252 : 320),
    height: size.height || (isObsShortcuts ? 96 : 220),
  });
  const [isDragging, setIsDragging] = useState(false);
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });

  useEffect(() => {
    setPos({ x: position.x || 100, y: position.y || 100 });
  }, [position.x, position.y]);

  const handleMouseDown = (e: React.MouseEvent) => {
    if ((e.target as HTMLElement).closest('.no-drag')) return;
    setIsDragging(true);
    setDragOffset({
      x: e.clientX - pos.x,
      y: e.clientY - pos.y,
    });
  };

  useEffect(() => {
    const handleMouseMove = (e: MouseEvent) => {
      if (!isDragging) return;
      const newX = Math.max(10, Math.min(window.innerWidth - dim.width - 10, e.clientX - dragOffset.x));
      const newY = Math.max(50, Math.min(window.innerHeight - 100, e.clientY - dragOffset.y));
      setPos({ x: newX, y: newY });
    };

    const handleMouseUp = () => {
      if (isDragging) {
        setIsDragging(false);
        updatePluginPosition(id, pos.x, pos.y);
      }
    };

    if (isDragging) {
      window.addEventListener('mousemove', handleMouseMove);
      window.addEventListener('mouseup', handleMouseUp);
    }
    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };
  }, [isDragging, dragOffset, dim, id, pos.x, pos.y, updatePluginPosition]);

  if (!enabled) return null;

  return (
    <div
      onMouseDown={handleMouseDown}
      onClick={(e) => e.stopPropagation()}
      className={`absolute rounded-xl bg-zinc-900/95 border border-zinc-700/70 shadow-2xl flex flex-col overflow-hidden text-zinc-100 transition-shadow ${
        isDragging ? 'ring-1 ring-zinc-400 opacity-95' : 'hover:border-zinc-600'
      }`}
      style={{
        left: `${pos.x}px`,
        top: `${pos.y}px`,
        width: `${dim.width}px`,
        height: isObsShortcuts ? 'auto' : `${dim.height}px`,
      }}
    >
      {/* Compact Gray Title Bar */}
      <div className="h-8 px-2.5 bg-zinc-800/80 border-b border-zinc-700/70 flex items-center justify-between cursor-grab active:cursor-grabbing text-xs">
        <div className="flex items-center gap-1.5 overflow-hidden">
          <GripHorizontal className="w-3.5 h-3.5 text-zinc-400 shrink-0" />
          <span className="font-semibold text-zinc-100 truncate max-w-[145px] text-[11px]">{name}</span>
          <span className="px-1 py-0.2 rounded text-[9px] font-mono uppercase bg-zinc-700/80 text-zinc-300">
            {language}
          </span>
        </div>

        <div className="flex items-center gap-0.5 no-drag">
          <button
            onClick={() => openExplorer(folderName)}
            className="p-1 rounded hover:bg-zinc-700 text-zinc-400 hover:text-white transition-colors cursor-pointer"
            title="Inspect Plugin Metadata JSON"
          >
            <FileCode className="w-3 h-3" />
          </button>
          <button
            onClick={() => deletePlugin(id)}
            className="p-1 rounded hover:bg-zinc-700 text-zinc-400 hover:text-white transition-colors cursor-pointer"
            title="Remove Module"
          >
            <X className="w-3 h-3" />
          </button>
        </div>
      </div>

      {/* Widget Body */}
      <div className={`${isObsShortcuts ? 'p-2.5' : 'flex-1 p-4 overflow-y-auto'} font-sans flex flex-col no-drag`}>
        {isObsShortcuts ? (
          <ObsShortcutsWidgetBody pluginFolder={pluginFolder} />
        ) : widgetType === 'clock' ? (
          <ClockWidgetBody />
        ) : widgetType === 'sysinfo' ? (
          <SysInfoWidgetBody />
        ) : (
          <CustomPluginBody pluginFolder={pluginFolder} />
        )}
      </div>

      {/* Footer omitted for compact plugins to avoid unused bottom space */}
      {!isObsShortcuts && (
        <div className="h-6 px-3 bg-zinc-950/60 border-t border-zinc-800 flex items-center justify-between text-[10px] font-mono text-zinc-400">
          <div>{entryPoint}</div>
          <div>{folderName}</div>
        </div>
      )}
    </div>
  );
};

/* --- OBS SHORTCUTS UNIFIED GRAY WIDGET --- */
const formatDuration = (totalSeconds: number) => {
  const hrs = Math.floor(totalSeconds / 3600);
  const mins = Math.floor((totalSeconds % 3600) / 60);
  const secs = totalSeconds % 60;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return hrs > 0 ? `${pad(hrs)}:${pad(mins)}:${pad(secs)}` : `${pad(mins)}:${pad(secs)}`;
};

const formatBufferDuration = (seconds: number) => {
  const s = Math.max(1, Math.round(seconds || 30));
  if (s >= 60 && s % 60 === 0) return `${s / 60}m`;
  if (s > 60) return `${Math.floor(s / 60)}m ${s % 60}s`;
  return `${s}s`;
};

const ObsShortcutsWidgetBody: React.FC<{ pluginFolder: PluginFolder }> = ({ pluginFolder }) => {
  const { isOverlayOpen, executeIntent } = useApp();
  const [isRecording, setIsRecording] = useState(false);
  const [recordSeconds, setRecordSeconds] = useState(0);
  const [isReplayActive, setIsReplayActive] = useState(false);
  const [isSavingReplay, setIsSavingReplay] = useState(false);
  const [isStreaming, setIsStreaming] = useState(false);

  const replayBufferDurationSec = Number(
    pluginFolder.metadata.settings?.replay_buffer_fallback_sec ?? 30
  );
  const obsExePath = String(
    pluginFolder.metadata.settings?.obs_exe_path ??
      'C:\\Program Files\\obs-studio\\bin\\64bit\\obs64.exe'
  );

  // Real-time update loop strictly ONLY while FuncOverlay is open (zero background running)
  useEffect(() => {
    if (!isOverlayOpen || !isRecording) return;
    const timer = setInterval(() => {
      setRecordSeconds((prev) => prev + 1);
    }, 1000);
    return () => clearInterval(timer);
  }, [isOverlayOpen, isRecording]);

  const handleSaveReplay = (e?: React.MouseEvent) => {
    if (e) {
      e.preventDefault();
      e.stopPropagation();
    }
    if (!isReplayActive || isSavingReplay) return;
    setIsSavingReplay(true);
    setTimeout(() => {
      setIsSavingReplay(false);
    }, 1800);
  };

  const handleToggleRecord = () => {
    setIsRecording((prev) => {
      if (prev) setRecordSeconds(0);
      return !prev;
    });
  };

  const handleOpenObs = () => {
    executeIntent('system.launch_app', { path: obsExePath }, pluginFolder.metadata.id);
    executeIntent('overlay.close', {}, pluginFolder.metadata.id);
  };

  const hasAnyStatus = isRecording || isReplayActive || isStreaming;
  const replayColor = isSavingReplay ? '#111b4f' : '#015637';

  return (
    <div className="flex flex-col">
      {/* 4 Gray Buttons in a Row */}
      <div className="grid grid-cols-4 gap-1.5">
        {/* 1. Start / Stop Recording */}
        <button
          type="button"
          onClick={handleToggleRecord}
          title={isRecording ? 'Stop Recording' : 'Start Recording'}
          className={`h-9 rounded-lg bg-zinc-800 hover:bg-zinc-700 active:bg-zinc-600 border transition-colors flex items-center justify-center cursor-pointer ${
            isRecording ? 'border-zinc-500' : 'border-zinc-700'
          }`}
        >
          <span
            className={`w-2.5 h-2.5 rounded-full transition-opacity ${
              isRecording ? 'animate-pulse' : ''
            }`}
            style={{ backgroundColor: '#6a010e' }}
          />
        </button>

        {/* 2. Start / Stop Replay Buffer (Right-click or Shift+click to Save Replay -> turns blue #111b4f) */}
        <button
          type="button"
          onClick={(e) => {
            if (e.shiftKey && isReplayActive) {
              handleSaveReplay(e);
              return;
            }
            setIsReplayActive((prev) => !prev);
          }}
          onContextMenu={(e) => {
            if (isReplayActive) {
              handleSaveReplay(e);
            } else {
              e.preventDefault();
            }
          }}
          title={
            isReplayActive
              ? 'Stop Replay Buffer (Right-click or Shift+Click to Save)'
              : 'Start Replay Buffer'
          }
          className={`h-9 rounded-lg bg-zinc-800 hover:bg-zinc-700 active:bg-zinc-600 border transition-colors flex items-center justify-center cursor-pointer ${
            isReplayActive ? 'border-zinc-500' : 'border-zinc-700'
          }`}
        >
          <span
            className={`w-2.5 h-2.5 rounded-full transition-colors ${
              isReplayActive && !isSavingReplay ? 'animate-pulse' : ''
            }`}
            style={{ backgroundColor: replayColor }}
          />
        </button>

        {/* 3. Start / Stop Broadcast */}
        <button
          type="button"
          onClick={() => setIsStreaming((prev) => !prev)}
          title={isStreaming ? 'Stop Broadcast' : 'Start Broadcast'}
          className={`h-9 rounded-lg bg-zinc-800 hover:bg-zinc-700 active:bg-zinc-600 border transition-colors flex items-center justify-center cursor-pointer ${
            isStreaming ? 'border-zinc-500' : 'border-zinc-700'
          }`}
        >
          <span
            className={`w-2.5 h-2.5 rounded-full transition-opacity ${
              isStreaming ? 'animate-pulse' : ''
            }`}
            style={{ backgroundColor: '#340f57' }}
          />
        </button>

        {/* 4. Open / Restore OBS (closes FuncOverlay via Intent) */}
        <button
          type="button"
          onClick={handleOpenObs}
          title="Open / Restore OBS Studio & Close FuncOverlay"
          className="h-9 rounded-lg bg-zinc-800 hover:bg-zinc-700 active:bg-zinc-600 border border-zinc-700 transition-colors flex items-center justify-center cursor-pointer text-zinc-200"
        >
          <ExternalLink className="w-3.5 h-3.5" />
        </button>
      </div>

      {/* Collapsible Bottom Status Strip (Zero blank space when everything is off) */}
      {hasAnyStatus && (
        <div className="mt-2 pt-0.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] font-mono text-zinc-200 px-0.5">
          {isRecording && (
            <div className="inline-flex items-center gap-1.5">
              <span
                className="w-2 h-2 rounded-full shrink-0"
                style={{ backgroundColor: '#6a010e' }}
              />
              <span>{formatDuration(recordSeconds)}</span>
            </div>
          )}
          {isReplayActive && (
            <div className="inline-flex items-center gap-1.5">
              <span
                className="w-2 h-2 rounded-full shrink-0 transition-colors"
                style={{ backgroundColor: replayColor }}
              />
              <span>
                {isSavingReplay ? 'Saving...' : formatBufferDuration(replayBufferDurationSec)}
              </span>
            </div>
          )}
          {isStreaming && (
            <div className="inline-flex items-center gap-1.5">
              <span
                className="w-2 h-2 rounded-full shrink-0"
                style={{ backgroundColor: '#340f57' }}
              />
              <span>Live</span>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

/* --- UNIFIED GRAY WIDGET COMPONENTS --- */
const ClockWidgetBody: React.FC = () => {
  const { isOverlayOpen } = useApp();
  const [time, setTime] = useState(new Date());
  useEffect(() => {
    if (!isOverlayOpen) return;
    const timer = setInterval(() => setTime(new Date()), 1000);
    return () => clearInterval(timer);
  }, [isOverlayOpen]);

  return (
    <div className="flex-1 flex flex-col items-center justify-center text-center">
      <div className="flex items-center gap-1.5 text-zinc-400 font-mono text-xs mb-1">
        <Clock className="w-3.5 h-3.5 text-zinc-400" />
        <span>SYSTEM TIME</span>
      </div>
      <div className="text-3xl font-bold font-mono tracking-tight text-white">
        {time.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
      </div>
      <div className="text-xs text-zinc-400 mt-1">
        {time.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' })}
      </div>
    </div>
  );
};

const SysInfoWidgetBody: React.FC = () => {
  const { isOverlayOpen } = useApp();
  const [cpu, setCpu] = useState(24);
  useEffect(() => {
    if (!isOverlayOpen) return;
    const timer = setInterval(() => setCpu(Math.floor(18 + Math.random() * 25)), 2000);
    return () => clearInterval(timer);
  }, [isOverlayOpen]);

  return (
    <div className="flex-1 flex flex-col justify-center space-y-3 font-mono text-xs">
      <div>
        <div className="flex justify-between mb-1 text-zinc-300">
          <span className="flex items-center gap-1">
            <Activity className="w-3.5 h-3.5 text-zinc-400" /> CPU Load
          </span>
          <span className="text-white font-bold">{cpu}%</span>
        </div>
        <div className="w-full h-2 bg-zinc-800 rounded-full overflow-hidden border border-zinc-700">
          <div className="h-full bg-zinc-300 transition-all duration-500" style={{ width: `${cpu}%` }} />
        </div>
      </div>
    </div>
  );
};

const CustomPluginBody: React.FC<{ pluginFolder: PluginFolder }> = ({ pluginFolder }) => {
  const { metadata } = pluginFolder;
  return (
    <div className="flex-1 flex flex-col justify-between text-xs font-mono text-zinc-300">
      <div className="p-3 rounded-xl bg-zinc-800/70 border border-zinc-700 space-y-1.5">
        <div className="flex justify-between">
          <span className="text-zinc-400">Entry File:</span>
          <span className="text-white font-bold">{metadata.entryPoint}</span>
        </div>
        <div className="flex justify-between">
          <span className="text-zinc-400">Language:</span>
          <span className="text-zinc-200 capitalize">{metadata.language}</span>
        </div>
      </div>
      <p className="text-zinc-400 text-xs mt-2">{metadata.description}</p>
    </div>
  );
};
