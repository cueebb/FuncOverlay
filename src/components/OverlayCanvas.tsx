import React, { useState, useRef, useEffect } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { PluginFolder, PluginMetadata } from '../types/plugin';
import {
  Download,
  FolderDown,
  RotateCw,
  Github,
  Plus,
  FolderOpen,
  X,
  GripHorizontal,
  Code2,
  FileCode,
  Clock,
  Activity,
  FileText,
  Sliders,
  Settings,
  Sparkles,
  ExternalLink,
} from 'lucide-react';

export const OverlayCanvas: React.FC = () => {
  const {
    isOverlayOpen,
    isFading,
    overlayOpacity,
    fadeDurationMs,
    plugins,
    hotkey,
    openExplorer,
    openSettings,
    addSamplePlugin,
    restartApp,
    updatePluginPosition,
    updatePluginSize,
    deletePlugin,
    togglePluginEnabled,
  } = useApp();

  // If overlay is hidden and not fading out, do not render content
  if (!isOverlayOpen && !isFading) return null;

  return (
    <div
      className="fixed inset-0 z-30 flex items-center justify-center select-none overflow-hidden"
      style={{
        backgroundColor: `rgba(0, 0, 0, ${overlayOpacity})`,
        backdropFilter: 'blur(16px) saturate(120%)',
        opacity: isOverlayOpen ? 1 : 0,
        transition: `opacity ${fadeDurationMs}ms cubic-bezier(0.16, 1, 0.3, 1), backdrop-filter ${fadeDurationMs}ms cubic-bezier(0.16, 1, 0.3, 1)`,
        pointerEvents: isOverlayOpen ? 'auto' : 'none',
      }}
    >
      {/* Overlay Background Status Indicator (Minimal Top Hairline Bar) */}
      <div className="absolute top-0 inset-x-0 h-1 bg-gradient-to-r from-blue-600 via-indigo-500 to-emerald-500 opacity-60" />

      {/* Screen Corner Hotkey Helper Tag */}
      <div className="absolute top-4 right-6 px-3 py-1 rounded-full bg-slate-900/70 border border-white/10 text-[11px] font-mono text-slate-300 flex items-center gap-2 backdrop-blur-md">
        <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
        <span>{t('app.name')} Overlay Active</span>
        <span className="text-slate-500">|</span>
        <span>{hotkey} to hide</span>
      </div>

      {/* CASE 1: NO PLUGINS INSTALLED -> SHOW SINGLE CENTER FLUENT INSTALLATION GUIDE WINDOW */}
      {plugins.length === 0 ? (
        <NoPluginsCenterWindow />
      ) : (
        /* CASE 2: PLUGINS INSTALLED -> RENDER DRAGGABLE FLUENT PLUGIN MODULE WIDGETS */
        <div className="absolute inset-0 overflow-hidden pointer-events-auto">
          {plugins.map((pluginFolder) => (
            <PluginWidgetCard key={pluginFolder.metadata.id} pluginFolder={pluginFolder} />
          ))}
        </div>
      )}
    </div>
  );
};

/* ========================================================================= */
/* NO PLUGINS INSTALLED: CENTER FLUENT WINDOW WITH REPHRASED INSTALLATION STEPS */
/* ========================================================================= */
const NoPluginsCenterWindow: React.FC = () => {
  const { addSamplePlugin, openExplorer, restartApp, openSettings } = useApp();

  return (
    <div className="w-[580px] max-w-[92vw] rounded-2xl fluent-glass p-8 border border-white/15 text-slate-100 shadow-2xl animate-in zoom-in-95 duration-200">
      {/* Header */}
      <div className="flex items-start justify-between mb-6 pb-4 border-b border-white/10">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <span className="px-2.5 py-0.5 rounded-full bg-blue-500/20 text-blue-400 font-mono text-xs border border-blue-500/30">
              {t('overlay.noPlugins.badge')}
            </span>
            <span className="text-xs text-slate-400 font-mono">FuncOverlay v1.0</span>
          </div>
          <h2 className="text-2xl font-bold tracking-tight text-white font-sans">
            {t('overlay.noPlugins.title')}
          </h2>
          <p className="text-xs text-slate-300 mt-1 leading-relaxed">
            {t('overlay.noPlugins.subtitle')}
          </p>
        </div>
      </div>

      {/* Rephrased 3 Installation Steps */}
      <div className="space-y-4 mb-8">
        {/* Step 1 */}
        <div className="p-4 rounded-xl bg-slate-900/60 border border-white/10 flex items-start gap-4 hover:border-blue-500/40 transition-colors group">
          <div className="w-8 h-8 rounded-lg bg-blue-600/30 text-blue-400 border border-blue-500/40 font-mono font-bold flex items-center justify-center shrink-0 text-sm">
            1
          </div>
          <div className="flex-1">
            <h3 className="text-sm font-semibold text-white group-hover:text-blue-300 transition-colors">
              {t('overlay.noPlugins.step1.title')}
            </h3>
            <p className="text-xs text-slate-300 mt-0.5">
              {t('overlay.noPlugins.step1.desc')}
            </p>
            <a
              href="https://github.com/FuncOverlay/plugins"
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1.5 text-xs text-blue-400 hover:text-blue-300 mt-2 font-medium transition-colors"
            >
              <Github className="w-3.5 h-3.5" />
              <span>{t('overlay.noPlugins.step1.actionText')}</span>
              <ExternalLink className="w-3 h-3" />
            </a>
          </div>
        </div>

        {/* Step 2 */}
        <div className="p-4 rounded-xl bg-slate-900/60 border border-white/10 flex items-start gap-4 hover:border-blue-500/40 transition-colors group">
          <div className="w-8 h-8 rounded-lg bg-amber-600/30 text-amber-400 border border-amber-500/40 font-mono font-bold flex items-center justify-center shrink-0 text-sm">
            2
          </div>
          <div className="flex-1">
            <h3 className="text-sm font-semibold text-white group-hover:text-amber-300 transition-colors">
              {t('overlay.noPlugins.step2.title')}
            </h3>
            <p className="text-xs text-slate-300 mt-0.5">
              {t('overlay.noPlugins.step2.desc')}
            </p>
            <div className="mt-2 px-3 py-1.5 rounded bg-black/40 border border-white/5 font-mono text-[11px] text-amber-300/90 flex items-center justify-between">
              <span>{t('overlay.noPlugins.step2.pathHint')}</span>
              <button
                onClick={() => openExplorer()}
                className="text-amber-400 hover:underline cursor-pointer flex items-center gap-1"
              >
                <FolderOpen className="w-3 h-3" />
                <span>Explore</span>
              </button>
            </div>
          </div>
        </div>

        {/* Step 3 */}
        <div className="p-4 rounded-xl bg-slate-900/60 border border-white/10 flex items-start gap-4 hover:border-blue-500/40 transition-colors group">
          <div className="w-8 h-8 rounded-lg bg-emerald-600/30 text-emerald-400 border border-emerald-500/40 font-mono font-bold flex items-center justify-center shrink-0 text-sm">
            3
          </div>
          <div className="flex-1">
            <h3 className="text-sm font-semibold text-white group-hover:text-emerald-300 transition-colors">
              {t('overlay.noPlugins.step3.title')}
            </h3>
            <p className="text-xs text-slate-300 mt-0.5">
              {t('overlay.noPlugins.step3.desc')}
            </p>
            <button
              onClick={restartApp}
              className="mt-2 inline-flex items-center gap-1.5 px-3 py-1 rounded bg-emerald-600/20 hover:bg-emerald-600/30 border border-emerald-500/40 text-emerald-300 text-xs font-medium transition-colors cursor-pointer"
            >
              <RotateCw className="w-3.5 h-3.5 text-emerald-400" />
              <span>{t('overlay.noPlugins.step3.actionText')}</span>
            </button>
          </div>
        </div>
      </div>

      {/* Quick Test Sample Generator Action Bar */}
      <div className="pt-4 border-t border-white/10">
        <div className="text-xs font-medium text-slate-400 mb-3 flex items-center gap-2">
          <Sparkles className="w-3.5 h-3.5 text-amber-400" />
          <span>{t('overlay.noPlugins.sampleAction.title')}:</span>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
          <button
            onClick={() => addSamplePlugin('clock', 'python')}
            className="px-3.5 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white font-medium text-xs flex items-center justify-center gap-2 transition-all cursor-pointer shadow-lg active:scale-95"
          >
            <Code2 className="w-4 h-4 text-blue-200" />
            <span>{t('overlay.noPlugins.sampleAction.btnPython')}</span>
          </button>

          <button
            onClick={() => addSamplePlugin('sysinfo', 'csharp')}
            className="px-3.5 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-medium text-xs flex items-center justify-center gap-2 transition-all cursor-pointer shadow-lg active:scale-95"
          >
            <FileCode className="w-4 h-4 text-indigo-200" />
            <span>{t('overlay.noPlugins.sampleAction.btnCsharp')}</span>
          </button>
        </div>
      </div>
    </div>
  );
};

/* ========================================================================= */
/* DRAGGABLE & RESIZABLE FLUENT PLUGIN MODULE WIDGET CARD */
/* ========================================================================= */
const PluginWidgetCard: React.FC<{ pluginFolder: PluginFolder }> = ({ pluginFolder }) => {
  const { updatePluginPosition, updatePluginSize, deletePlugin, openExplorer } = useApp();
  const { metadata, folderName } = pluginFolder;
  const { id, name, description, entryPoint, language, position, size, enabled, widgetType } = metadata;

  const [pos, setPos] = useState({ x: position.x || 100, y: position.y || 100 });
  const [dim, setDim] = useState({ width: size.width || 320, height: size.height || 220 });
  const [isDragging, setIsDragging] = useState(false);
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });

  const widgetRef = useRef<HTMLDivElement>(null);

  // Sync internal position state if metadata updates via external JSON edit
  useEffect(() => {
    setPos({ x: position.x || 100, y: position.y || 100 });
  }, [position.x, position.y]);

  useEffect(() => {
    setDim({ width: size.width || 320, height: size.height || 220 });
  }, [size.width, size.height]);

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
      const newY = Math.max(50, Math.min(window.innerHeight - dim.height - 10, e.clientY - dragOffset.y));
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
      ref={widgetRef}
      onMouseDown={handleMouseDown}
      className={`absolute rounded-2xl fluent-glass border border-white/20 shadow-2xl flex flex-col overflow-hidden text-slate-100 transition-shadow ${
        isDragging ? 'ring-2 ring-blue-400 opacity-95 shadow-2xl scale-[1.01]' : 'hover:border-white/30'
      }`}
      style={{
        left: `${pos.x}px`,
        top: `${pos.y}px`,
        width: `${dim.width}px`,
        height: `${dim.height}px`,
      }}
    >
      {/* Fluent Window Title Bar (Drag Handle) */}
      <div className="h-9 px-3 bg-slate-950/70 border-b border-white/10 flex items-center justify-between cursor-grab active:cursor-grabbing text-xs">
        <div className="flex items-center gap-2 overflow-hidden">
          <GripHorizontal className="w-3.5 h-3.5 text-slate-500 shrink-0" />
          <span className="font-semibold text-white truncate max-w-[160px]">{name}</span>
          <span
            className={`px-1.5 py-0.2 rounded text-[10px] font-mono shrink-0 uppercase border ${
              language === 'python'
                ? 'bg-amber-500/20 text-amber-300 border-amber-500/30'
                : 'bg-indigo-500/20 text-indigo-300 border-indigo-500/30'
            }`}
          >
            {language}
          </span>
        </div>

        {/* Controls */}
        <div className="flex items-center gap-1 no-drag">
          <button
            onClick={() => openExplorer(folderName)}
            className="p-1 rounded hover:bg-white/10 text-slate-400 hover:text-white transition-colors cursor-pointer"
            title="Inspect Plugin Metadata JSON & Code"
          >
            <FileCode className="w-3.5 h-3.5" />
          </button>
          <button
            onClick={() => deletePlugin(id)}
            className="p-1 rounded hover:bg-red-500/20 text-slate-400 hover:text-red-400 transition-colors cursor-pointer"
            title="Disable & Remove Module"
          >
            <X className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Widget Specific Body Content */}
      <div className="flex-1 p-4 overflow-y-auto font-sans flex flex-col">
        {widgetType === 'clock' ? (
          <ClockWidgetBody />
        ) : widgetType === 'sysinfo' ? (
          <SysInfoWidgetBody />
        ) : widgetType === 'notes' ? (
          <QuickNotesWidgetBody />
        ) : widgetType === 'volume' ? (
          <VolumeWidgetBody />
        ) : (
          <CustomPluginBody pluginFolder={pluginFolder} />
        )}
      </div>

      {/* Footer metadata info */}
      <div className="h-6 px-3 bg-black/40 border-t border-white/5 flex items-center justify-between text-[10px] font-mono text-slate-400">
        <div className="flex items-center gap-2">
          <span>{entryPoint}</span>
          <span>·</span>
          <span>
            Pos: {pos.x},{pos.y}
          </span>
        </div>
        <div className="text-slate-500">{folderName}</div>
      </div>
    </div>
  );
};

/* --- SAMPLE WIDGET BODY 1: CLOCK --- */
const ClockWidgetBody: React.FC = () => {
  const [time, setTime] = useState(new Date());

  useEffect(() => {
    const timer = setInterval(() => setTime(new Date()), 1000);
    return () => clearInterval(timer);
  }, []);

  const timeStr = time.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  const dateStr = time.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' });

  return (
    <div className="flex-1 flex flex-col items-center justify-center text-center">
      <div className="flex items-center gap-1.5 text-blue-400 font-mono text-xs mb-1">
        <Clock className="w-3.5 h-3.5" />
        <span>SYSTEM TIME (C# HOOK)</span>
      </div>
      <div className="text-4xl font-bold font-mono tracking-tight text-white drop-shadow-md">
        {timeStr}
      </div>
      <div className="text-xs text-slate-400 mt-1 font-medium">{dateStr}</div>
    </div>
  );
};

/* --- SAMPLE WIDGET BODY 2: SYSTEM HARDWARE MONITOR --- */
const SysInfoWidgetBody: React.FC = () => {
  const [cpu, setCpu] = useState(24);
  const [ram, setRam] = useState(58);
  const [gpu, setGpu] = useState(38);

  useEffect(() => {
    const timer = setInterval(() => {
      setCpu(Math.floor(18 + Math.random() * 25));
      setRam(Math.floor(55 + Math.random() * 8));
      setGpu(Math.floor(30 + Math.random() * 20));
    }, 2000);
    return () => clearInterval(timer);
  }, []);

  return (
    <div className="flex-1 flex flex-col justify-center space-y-3 font-mono text-xs">
      <div>
        <div className="flex justify-between mb-1">
          <span className="text-slate-300 flex items-center gap-1">
            <Activity className="w-3.5 h-3.5 text-blue-400" /> CPU Load
          </span>
          <span className="text-blue-400 font-bold">{cpu}%</span>
        </div>
        <div className="w-full h-2 bg-slate-900 rounded-full overflow-hidden border border-white/10">
          <div className="h-full bg-blue-500 transition-all duration-500" style={{ width: `${cpu}%` }} />
        </div>
      </div>

      <div>
        <div className="flex justify-between mb-1">
          <span className="text-slate-300">RAM Usage</span>
          <span className="text-indigo-400 font-bold">{ram}% (9.3 / 16 GB)</span>
        </div>
        <div className="w-full h-2 bg-slate-900 rounded-full overflow-hidden border border-white/10">
          <div className="h-full bg-indigo-500 transition-all duration-500" style={{ width: `${ram}%` }} />
        </div>
      </div>

      <div>
        <div className="flex justify-between mb-1">
          <span className="text-slate-300">GPU Temp</span>
          <span className="text-emerald-400 font-bold">{gpu}°C</span>
        </div>
        <div className="w-full h-2 bg-slate-900 rounded-full overflow-hidden border border-white/10">
          <div className="h-full bg-emerald-500 transition-all duration-500" style={{ width: `${gpu}%` }} />
        </div>
      </div>
    </div>
  );
};

/* --- SAMPLE WIDGET BODY 3: QUICK NOTES --- */
const QuickNotesWidgetBody: React.FC = () => {
  const [note, setNote] = useState('• Alt+Enter toggles FuncOverlay within 0.25s\n• Add main.py or plugin.dll in ./plugins/');

  return (
    <div className="flex-1 flex flex-col">
      <div className="flex items-center gap-1.5 text-amber-400 text-xs font-semibold mb-2">
        <FileText className="w-3.5 h-3.5" />
        <span>Overlay Scratchpad</span>
      </div>
      <textarea
        value={note}
        onChange={(e) => setNote(e.target.value)}
        className="flex-1 w-full bg-slate-900/80 border border-white/10 rounded-xl p-2.5 text-xs text-slate-200 font-mono resize-none focus:outline-none focus:border-amber-500/50"
        placeholder="Type quick notes here..."
      />
    </div>
  );
};

/* --- SAMPLE WIDGET BODY 4: VOLUME MIXER --- */
const VolumeWidgetBody: React.FC = () => {
  const [master, setMaster] = useState(75);
  const [game, setGame] = useState(90);
  const [discord, setDiscord] = useState(60);

  return (
    <div className="flex-1 flex flex-col justify-center space-y-3 text-xs">
      <div>
        <div className="flex justify-between text-slate-300 font-semibold mb-1">
          <span>Master Volume</span>
          <span className="font-mono text-blue-400">{master}%</span>
        </div>
        <input
          type="range"
          min="0"
          max="100"
          value={master}
          onChange={(e) => setMaster(Number(e.target.value))}
          className="w-full accent-blue-500 cursor-pointer"
        />
      </div>

      <div>
        <div className="flex justify-between text-slate-300 mb-1">
          <span>Active Game</span>
          <span className="font-mono text-indigo-400">{game}%</span>
        </div>
        <input
          type="range"
          min="0"
          max="100"
          value={game}
          onChange={(e) => setGame(Number(e.target.value))}
          className="w-full accent-indigo-500 cursor-pointer"
        />
      </div>

      <div>
        <div className="flex justify-between text-slate-300 mb-1">
          <span>Discord Voice</span>
          <span className="font-mono text-emerald-400">{discord}%</span>
        </div>
        <input
          type="range"
          min="0"
          max="100"
          value={discord}
          onChange={(e) => setDiscord(Number(e.target.value))}
          className="w-full accent-emerald-500 cursor-pointer"
        />
      </div>
    </div>
  );
};

/* --- CUSTOM USER PLUGIN BODY --- */
const CustomPluginBody: React.FC<{ pluginFolder: PluginFolder }> = ({ pluginFolder }) => {
  const { metadata } = pluginFolder;
  return (
    <div className="flex-1 flex flex-col justify-between text-xs">
      <div className="p-3 rounded-xl bg-slate-900/70 border border-white/10 space-y-1.5 font-mono">
        <div className="flex justify-between text-slate-300">
          <span>Entry Point:</span>
          <span className="text-blue-400 font-bold">{metadata.entryPoint}</span>
        </div>
        <div className="flex justify-between text-slate-300">
          <span>Language:</span>
          <span className="text-amber-400 font-bold capitalize">{metadata.language}</span>
        </div>
        <div className="flex justify-between text-slate-300">
          <span>Author:</span>
          <span className="text-slate-200">{metadata.author}</span>
        </div>
      </div>
      <p className="text-slate-300 text-xs mt-2 line-clamp-2">{metadata.description}</p>
    </div>
  );
};
