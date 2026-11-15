import React, { useState, useRef, useEffect, useCallback, useMemo } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { PluginFolder, WidgetAnchor } from '../types/plugin';
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
  Settings,
  Lock,
} from 'lucide-react';

interface RectBounds {
  left: number;
  top: number;
  width: number;
  height: number;
}

export function normalizeAnchor(raw?: string): WidgetAnchor {
  if (!raw) return 'top-left';
  const s = raw.trim().toLowerCase().replace(/_/g, '-').replace(/\s+/g, '-');
  switch (s) {
    case 'top-left':
    case 'left-top':
    case 'upper-left':
    case 'left-upper':
      return 'top-left';
    case 'top-center':
    case 'center-top':
    case 'upper-center':
    case 'top':
      return 'top-center';
    case 'top-right':
    case 'right-top':
    case 'upper-right':
    case 'right-upper':
      return 'top-right';
    case 'center-left':
    case 'left-center':
    case 'middle-left':
    case 'left':
      return 'center-left';
    case 'center':
    case 'middle':
    case 'center-center':
      return 'center';
    case 'center-right':
    case 'right-center':
    case 'middle-right':
    case 'right':
      return 'center-right';
    case 'bottom-left':
    case 'left-bottom':
    case 'lower-left':
    case 'left-lower':
      return 'bottom-left';
    case 'bottom-center':
    case 'center-bottom':
    case 'lower-center':
    case 'bottom':
      return 'bottom-center';
    case 'bottom-right':
    case 'right-bottom':
    case 'lower-right':
    case 'right-lower':
      return 'bottom-right';
    default:
      return 'top-left';
  }
}

function splitAnchor(raw?: string): { vAlign: 'top' | 'center' | 'bottom'; hAlign: 'left' | 'center' | 'right' } {
  const norm = normalizeAnchor(raw);
  if (norm === 'center') return { vAlign: 'center', hAlign: 'center' };
  const [v, h] = norm.split('-') as ['top' | 'center' | 'bottom', 'left' | 'center' | 'right'];
  return { vAlign: v || 'top', hAlign: h || 'left' };
}

function getPointOnRect(
  left: number,
  top: number,
  width: number,
  height: number,
  anchor?: string
): { x: number; y: number } {
  const { vAlign, hAlign } = splitAnchor(anchor);
  const x = hAlign === 'center' ? left + width / 2 : hAlign === 'right' ? left + width : left;
  const y = vAlign === 'center' ? top + height / 2 : vAlign === 'bottom' ? top + height : top;
  return { x, y };
}

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
  const [viewport, setViewport] = useState<{ width: number; height: number }>(() => ({
    width: typeof window !== 'undefined' ? window.innerWidth : 1280,
    height: typeof window !== 'undefined' ? window.innerHeight : 720,
  }));
  const [measuredSizes, setMeasuredSizes] = useState<Record<string, { width: number; height: number }>>({});
  const [activeDrag, setActiveDrag] = useState<{ id: string; left: number; top: number } | null>(null);

  useEffect(() => {
    const onResize = () => {
      setViewport({ width: window.innerWidth, height: window.innerHeight });
    };
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);

  const handleCardMeasure = useCallback((id: string, width: number, height: number) => {
    setMeasuredSizes((prev) => {
      const cur = prev[id];
      if (cur && Math.abs(cur.width - width) < 0.5 && Math.abs(cur.height - height) < 0.5) {
        return prev;
      }
      return { ...prev, [id]: { width, height } };
    });
  }, []);

  const enabledPlugins = useMemo(
    () => plugins.filter((pf) => pf.metadata.enabled),
    [plugins]
  );

  const resolvedBoundsMap = useMemo(() => {
    const byId = new Map<string, PluginFolder>();
    enabledPlugins.forEach((pf) => byId.set(pf.metadata.id, pf));

    const resolved = new Map<string, RectBounds>();
    const visiting = new Set<string>();

    const getCardDim = (pf: PluginFolder) => {
      const m = measuredSizes[pf.metadata.id];
      const isObs = pf.metadata.widgetType === 'obs_shortcuts' || pf.metadata.id === 'obs_shortcuts';
      const w = m?.width || pf.metadata.size.width || (isObs ? 252 : 320);
      const h = m?.height || pf.metadata.size.height || (isObs ? 96 : 220);
      return { width: w, height: h };
    };

    const resolveOne = (id: string): RectBounds => {
      const existing = resolved.get(id);
      if (existing) return existing;

      const pf = byId.get(id);
      if (!pf) return { left: 0, top: 0, width: 280, height: 96 };

      const { width: w, height: h } = getCardDim(pf);

      if (activeDrag && activeDrag.id === id) {
        const dragBounds = { left: activeDrag.left, top: activeDrag.top, width: w, height: h };
        resolved.set(id, dragBounds);
        return dragBounds;
      }

      const pos = pf.metadata.position || { x: 80, y: 80 };
      const wAnchor = normalizeAnchor(pos.anchor);
      const targetId = (pos.anchorTarget || 'screen').trim();

      let ax: number;
      let ay: number;

      if (
        targetId &&
        targetId.toLowerCase() !== 'screen' &&
        targetId !== id &&
        byId.has(targetId) &&
        !visiting.has(targetId)
      ) {
        visiting.add(id);
        const tBounds = resolveOne(targetId);
        visiting.delete(id);

        const tAnchor = normalizeAnchor(pos.targetAnchor || wAnchor);
        const pt = getPointOnRect(tBounds.left, tBounds.top, tBounds.width, tBounds.height, tAnchor);
        ax = pt.x;
        ay = pt.y;
      } else {
        const pt = getPointOnRect(0, 0, viewport.width, viewport.height, wAnchor);
        ax = pt.x;
        ay = pt.y;
      }

      const { vAlign, hAlign } = splitAnchor(wAnchor);
      const ox = pos.x ?? 0;
      const oy = pos.y ?? 0;

      const left =
        hAlign === 'center' ? ax + ox - w / 2 : hAlign === 'right' ? ax - ox - w : ax + ox;
      const top =
        vAlign === 'center' ? ay + oy - h / 2 : vAlign === 'bottom' ? ay - oy - h : ay + oy;

      const bounds = { left, top, width: w, height: h };
      resolved.set(id, bounds);
      return bounds;
    };

    enabledPlugins.forEach((pf) => resolveOne(pf.metadata.id));
    return resolved;
  }, [activeDrag, enabledPlugins, measuredSizes, viewport.height, viewport.width]);

  const computeOffsetFromTopLeft = useCallback(
    (pf: PluginFolder, left: number, top: number): { x: number; y: number } => {
      const m = measuredSizes[pf.metadata.id];
      const isObs = pf.metadata.widgetType === 'obs_shortcuts' || pf.metadata.id === 'obs_shortcuts';
      const w = m?.width || pf.metadata.size.width || (isObs ? 252 : 320);
      const h = m?.height || pf.metadata.size.height || (isObs ? 96 : 220);

      const pos = pf.metadata.position || { x: 80, y: 80 };
      const wAnchor = normalizeAnchor(pos.anchor);
      const targetId = (pos.anchorTarget || 'screen').trim();

      let ax: number;
      let ay: number;

      if (
        targetId &&
        targetId.toLowerCase() !== 'screen' &&
        targetId !== pf.metadata.id &&
        resolvedBoundsMap.has(targetId)
      ) {
        const tBounds = resolvedBoundsMap.get(targetId)!;
        const tAnchor = normalizeAnchor(pos.targetAnchor || wAnchor);
        const pt = getPointOnRect(tBounds.left, tBounds.top, tBounds.width, tBounds.height, tAnchor);
        ax = pt.x;
        ay = pt.y;
      } else {
        const pt = getPointOnRect(0, 0, viewport.width, viewport.height, wAnchor);
        ax = pt.x;
        ay = pt.y;
      }

      const { vAlign, hAlign } = splitAnchor(wAnchor);
      const x =
        hAlign === 'center' ? left + w / 2 - ax : hAlign === 'right' ? ax - (left + w) : left - ax;
      const y =
        vAlign === 'center' ? top + h / 2 - ay : vAlign === 'bottom' ? ay - (top + h) : top - ay;

      return { x: Math.round(x), y: Math.round(y) };
    },
    [measuredSizes, resolvedBoundsMap, viewport.height, viewport.width]
  );

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
          {plugins.map((pluginFolder) => {
            const bounds = resolvedBoundsMap.get(pluginFolder.metadata.id);
            return (
              <PluginWidgetCard
                key={pluginFolder.metadata.id}
                pluginFolder={pluginFolder}
                resolvedBounds={bounds}
                viewport={viewport}
                onMeasure={handleCardMeasure}
                onDragMove={(left, top) => setActiveDrag({ id: pluginFolder.metadata.id, left, top })}
                onDragEnd={(left, top) => {
                  setActiveDrag(null);
                  const offset = computeOffsetFromTopLeft(pluginFolder, left, top);
                  return offset;
                }}
              />
            );
          })}
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
interface PluginWidgetCardProps {
  pluginFolder: PluginFolder;
  resolvedBounds?: RectBounds;
  viewport: { width: number; height: number };
  onMeasure: (id: string, width: number, height: number) => void;
  onDragMove: (left: number, top: number) => void;
  onDragEnd: (left: number, top: number) => { x: number; y: number };
}

const PluginWidgetCard: React.FC<PluginWidgetCardProps> = ({
  pluginFolder,
  resolvedBounds,
  viewport,
  onMeasure,
  onDragMove,
  onDragEnd,
}) => {
  const { updatePluginPosition, deletePlugin, openExplorer } = useApp();
  const { metadata, folderName } = pluginFolder;
  const { id, name, entryPoint, language, position, size, enabled, locked, widgetType } = metadata;
  const isObsShortcuts = widgetType === 'obs_shortcuts' || id === 'obs_shortcuts';
  const isSimpleCalc = widgetType === 'simple_calc' || id === 'simple_calc';
  const isCompactPlugin = isObsShortcuts || isSimpleCalc;

  const cardRef = useRef<HTMLDivElement | null>(null);
  const [dim] = useState({
    width: size.width || (isObsShortcuts ? 252 : isSimpleCalc ? 256 : 320),
    height: size.height || (isObsShortcuts ? 96 : isSimpleCalc ? 396 : 220),
  });
  const [isDragging, setIsDragging] = useState(false);
  const dragOffsetRef = useRef({ x: 0, y: 0 });
  const livePosRef = useRef({
    left: resolvedBounds?.left ?? position.x ?? 100,
    top: resolvedBounds?.top ?? position.y ?? 100,
  });

  const leftPos = resolvedBounds?.left ?? position.x ?? 100;
  const topPos = resolvedBounds?.top ?? position.y ?? 100;

  useEffect(() => {
    if (!isDragging) {
      livePosRef.current = { left: leftPos, top: topPos };
    }
  }, [isDragging, leftPos, topPos]);

  useEffect(() => {
    const el = cardRef.current;
    if (!el || !enabled) return;
    const measureNow = () => {
      onMeasure(id, el.offsetWidth, el.offsetHeight);
    };
    measureNow();
    const ro = new ResizeObserver(() => measureNow());
    ro.observe(el);
    return () => ro.disconnect();
  }, [enabled, id, onMeasure]);

  const handleMouseDown = (e: React.MouseEvent) => {
    if (locked) return;
    if ((e.target as HTMLElement).closest('.no-drag')) return;
    setIsDragging(true);
    dragOffsetRef.current = {
      x: e.clientX - leftPos,
      y: e.clientY - topPos,
    };
    livePosRef.current = { left: leftPos, top: topPos };
  };

  useEffect(() => {
    const handleMouseMove = (e: MouseEvent) => {
      if (!isDragging || locked) return;
      const cardHeight = cardRef.current?.offsetHeight || dim.height;
      const newLeft = Math.max(
        8,
        Math.min(Math.max(8, viewport.width - dim.width - 8), e.clientX - dragOffsetRef.current.x)
      );
      const newTop = Math.max(
        8,
        Math.min(Math.max(8, viewport.height - cardHeight - 8), e.clientY - dragOffsetRef.current.y)
      );
      livePosRef.current = { left: newLeft, top: newTop };
      onDragMove(newLeft, newTop);
    };

    const handleMouseUp = () => {
      if (isDragging) {
        setIsDragging(false);
        const offset = onDragEnd(livePosRef.current.left, livePosRef.current.top);
        updatePluginPosition(id, offset.x, offset.y);
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
  }, [dim.height, dim.width, id, isDragging, locked, onDragEnd, onDragMove, updatePluginPosition, viewport.height, viewport.width]);

  if (!enabled) return null;

  return (
    <div
      ref={cardRef}
      onMouseDown={handleMouseDown}
      onClick={(e) => e.stopPropagation()}
      className={`absolute rounded-xl bg-zinc-900/95 border border-zinc-700/70 shadow-2xl flex flex-col overflow-hidden text-zinc-100 transition-shadow ${
        isDragging ? 'ring-1 ring-zinc-400 opacity-95' : 'hover:border-zinc-600'
      }`}
      style={{
        left: `${leftPos}px`,
        top: `${topPos}px`,
        width: `${dim.width}px`,
        height: isCompactPlugin ? 'auto' : `${dim.height}px`,
      }}
    >
      {/* Compact Gray Title Bar */}
      <div
        className={`h-8 px-2.5 bg-zinc-800/80 border-b border-zinc-700/70 flex items-center justify-between text-xs ${
          locked ? 'cursor-default' : 'cursor-grab active:cursor-grabbing'
        }`}
      >
        <div className="flex items-center gap-1.5 overflow-hidden">
          {locked ? (
            <Lock className="w-3 h-3 text-zinc-400 shrink-0" />
          ) : (
            <GripHorizontal className="w-3.5 h-3.5 text-zinc-400 shrink-0" />
          )}
          <span className="font-semibold text-zinc-100 truncate max-w-[140px] text-[11px]">{name}</span>
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
      <div className={`${isCompactPlugin ? 'p-2.5' : 'flex-1 p-4 overflow-y-auto'} font-sans flex flex-col no-drag`}>
        {isObsShortcuts ? (
          <ObsShortcutsWidgetBody pluginFolder={pluginFolder} />
        ) : isSimpleCalc ? (
          <SimpleCalcWidgetBody pluginFolder={pluginFolder} />
        ) : widgetType === 'clock' ? (
          <ClockWidgetBody />
        ) : widgetType === 'sysinfo' ? (
          <SysInfoWidgetBody />
        ) : (
          <CustomPluginBody pluginFolder={pluginFolder} />
        )}
      </div>

      {/* Footer omitted for compact plugins to avoid unused bottom space */}
      {!isCompactPlugin && (
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

/* --- SIMPLE CALC UNIFIED GRAY WIDGET (Exact Rational + Mixed Fraction Engine) --- */
interface BigFrac {
  n: bigint;
  d: bigint;
}

type CalcVal =
  | { exact: true; frac: BigFrac; flt: number }
  | { exact: false; frac: null; flt: number };

function absBig(x: bigint): bigint {
  return x < 0n ? -x : x;
}

function gcdBig(a: bigint, b: bigint): bigint {
  let x = absBig(a);
  let y = absBig(b);
  while (y !== 0n) {
    const t = x % y;
    x = y;
    y = t;
  }
  return x === 0n ? 1n : x;
}

function normFrac(n: bigint, d: bigint): BigFrac {
  if (d === 0n) throw new Error('Division by zero');
  if (n === 0n) return { n: 0n, d: 1n };
  let num = n;
  let den = d;
  if (den < 0n) {
    num = -num;
    den = -den;
  }
  const g = gcdBig(num, den);
  return { n: num / g, d: den / g };
}

function parseDecimalToFrac(str: string): BigFrac {
  const s = str === '.' ? '0' : str;
  const dotIdx = s.indexOf('.');
  if (dotIdx < 0) return normFrac(BigInt(s), 1n);
  const intPart = s.slice(0, dotIdx) || '0';
  const fracPart = s.slice(dotIdx + 1);
  const den = 10n ** BigInt(fracPart.length);
  const num = BigInt(intPart) * den + BigInt(fracPart || '0');
  return normFrac(num, den);
}

function intSqrtExact(n: bigint): bigint | null {
  if (n < 0n) return null;
  if (n < 2n) return n;
  let x = n;
  let y = (x + 1n) >> 1n;
  while (y < x) {
    x = y;
    y = (x + n / x) >> 1n;
  }
  return x * x === n ? x : null;
}

function makeExact(f: BigFrac): CalcVal {
  return { exact: true, frac: f, flt: Number(f.n) / Number(f.d) };
}

function makeApprox(flt: number): CalcVal {
  return { exact: false, frac: null, flt };
}

function evaluateCalcExpression(rawExpr: string): CalcVal {
  let expr = rawExpr.trim();
  while (expr.length > 0 && '÷×−+/*-'.includes(expr[expr.length - 1])) {
    expr = expr.slice(0, -1);
  }
  if (!expr) return makeExact({ n: 0n, d: 1n });

  let balance = 0;
  for (const ch of expr) {
    if (ch === '(') balance++;
    else if (ch === ')') balance--;
  }
  if (balance > 0) expr += ')'.repeat(balance);

  type Tok = { k: 'NUM' | 'OP' | 'LPAREN' | 'RPAREN' | 'SQRT'; v: string };
  const rawTokens: Tok[] = [];
  let i = 0;
  while (i < expr.length) {
    const ch = expr[i];
    if (/\s/.test(ch)) {
      i++;
      continue;
    }
    if ((ch >= '0' && ch <= '9') || ch === '.') {
      const start = i;
      let hasDot = ch === '.';
      i++;
      while (i < expr.length && ((expr[i] >= '0' && expr[i] <= '9') || (expr[i] === '.' && !hasDot))) {
        if (expr[i] === '.') hasDot = true;
        i++;
      }
      rawTokens.push({ k: 'NUM', v: expr.slice(start, i) });
      continue;
    }
    if (ch === '/' || ch === '÷') {
      rawTokens.push({ k: 'OP', v: '÷' });
      i++;
    } else if (ch === '*' || ch === '×') {
      rawTokens.push({ k: 'OP', v: '×' });
      i++;
    } else if (ch === '-' || ch === '−') {
      rawTokens.push({ k: 'OP', v: '−' });
      i++;
    } else if (ch === '+') {
      rawTokens.push({ k: 'OP', v: '+' });
      i++;
    } else if (ch === '(') {
      rawTokens.push({ k: 'LPAREN', v: '(' });
      i++;
    } else if (ch === ')') {
      rawTokens.push({ k: 'RPAREN', v: ')' });
      i++;
    } else if (ch === '√') {
      rawTokens.push({ k: 'SQRT', v: '√' });
      i++;
    } else {
      throw new Error(`Unexpected token ${ch}`);
    }
  }

  const tokens: Tok[] = [];
  rawTokens.forEach((tok, idx) => {
    if (idx > 0) {
      const prev = rawTokens[idx - 1].k;
      if (
        ((prev === 'NUM' || prev === 'RPAREN') && (tok.k === 'LPAREN' || tok.k === 'SQRT')) ||
        (prev === 'RPAREN' && tok.k === 'NUM')
      ) {
        tokens.push({ k: 'OP', v: '×' });
      }
    }
    tokens.push(tok);
  });

  let pos = 0;
  const peek = () => (pos < tokens.length ? tokens[pos] : null);
  const consume = () => tokens[pos++];

  const parseExpr = (): CalcVal => {
    let left = parseTerm();
    while (peek()?.k === 'OP' && (peek()!.v === '+' || peek()!.v === '−')) {
      const op = consume().v;
      const right = parseTerm();
      if (left.exact && right.exact) {
        left = makeExact(
          op === '+'
            ? normFrac(left.frac.n * right.frac.d + right.frac.n * left.frac.d, left.frac.d * right.frac.d)
            : normFrac(left.frac.n * right.frac.d - right.frac.n * left.frac.d, left.frac.d * right.frac.d)
        );
      } else {
        left = makeApprox(op === '+' ? left.flt + right.flt : left.flt - right.flt);
      }
    }
    return left;
  };

  const parseTerm = (): CalcVal => {
    let left = parseUnary();
    while (peek()?.k === 'OP' && (peek()!.v === '×' || peek()!.v === '÷')) {
      const op = consume().v;
      const right = parseUnary();
      if (op === '÷' && (right.exact ? right.frac.n === 0n : right.flt === 0)) {
        throw new Error('Division by zero');
      }
      if (left.exact && right.exact) {
        left = makeExact(
          op === '×'
            ? normFrac(left.frac.n * right.frac.n, left.frac.d * right.frac.d)
            : normFrac(left.frac.n * right.frac.d, left.frac.d * right.frac.n)
        );
      } else {
        left = makeApprox(op === '×' ? left.flt * right.flt : left.flt / right.flt);
      }
    }
    return left;
  };

  const parseUnary = (): CalcVal => {
    const tok = peek();
    if (!tok) throw new Error('Unexpected end');
    if (tok.k === 'OP' && tok.v === '−') {
      consume();
      const v = parseUnary();
      return v.exact ? makeExact({ n: -v.frac.n, d: v.frac.d }) : makeApprox(-v.flt);
    }
    if (tok.k === 'OP' && tok.v === '+') {
      consume();
      return parseUnary();
    }
    if (tok.k === 'SQRT') {
      consume();
      const v = parseUnary();
      if (v.exact) {
        if (v.frac.n < 0n) throw new Error('Domain error');
        const sn = intSqrtExact(v.frac.n);
        const sd = intSqrtExact(v.frac.d);
        if (sn !== null && sd !== null) {
          return makeExact(normFrac(sn, sd));
        }
      }
      if (v.flt < 0) throw new Error('Domain error');
      return makeApprox(Math.sqrt(v.flt));
    }
    return parsePrimary();
  };

  const parsePrimary = (): CalcVal => {
    const tok = peek();
    if (!tok) throw new Error('Unexpected end');
    if (tok.k === 'NUM') {
      consume();
      return makeExact(parseDecimalToFrac(tok.v));
    }
    if (tok.k === 'LPAREN') {
      consume();
      const v = parseExpr();
      if (peek()?.k === 'RPAREN') consume();
      return v;
    }
    throw new Error('Invalid expression');
  };

  const res = parseExpr();
  if (pos < tokens.length) throw new Error('Unexpected trailing input');
  return res;
}

function formatBigFractionDecimal(f: BigFrac, maxDecimals: number): string {
  const neg = f.n < 0n;
  const num = absBig(f.n);
  const den = f.d;
  if (den === 1n) return (neg ? '-' : '') + num.toString();

  const scale = 10n ** BigInt(maxDecimals);
  // Half-up rounding of num/den * scale
  const scaled = (num * scale * 2n + den) / (2n * den);
  const intPart = scaled / scale;
  const rem = scaled % scale;
  let fracStr = rem.toString().padStart(maxDecimals, '0').replace(/0+$/, '');
  const sign = neg && (intPart > 0n || fracStr.length > 0) ? '-' : '';
  return fracStr ? `${sign}${intPart.toString()}.${fracStr}` : `${sign}${intPart.toString()}`;
}

interface CalcFractionDisplay {
  whole: string;
  num: string;
  den: string;
}

const SIMPLE_CALC_KEYPAD: Array<Array<{ id: string; label: string; variant: 'operator' | 'digit' | 'equals'; icon?: 'sqrt' | 'backspace' }>> = [
  [
    { id: 'ac', label: 'AC', variant: 'operator' },
    { id: 'parens', label: '()', variant: 'operator' },
    { id: 'sqrt', label: '√', icon: 'sqrt', variant: 'operator' },
    { id: 'div', label: '÷', variant: 'operator' },
  ],
  [
    { id: '7', label: '7', variant: 'digit' },
    { id: '8', label: '8', variant: 'digit' },
    { id: '9', label: '9', variant: 'digit' },
    { id: 'mul', label: '×', variant: 'operator' },
  ],
  [
    { id: '4', label: '4', variant: 'digit' },
    { id: '5', label: '5', variant: 'digit' },
    { id: '6', label: '6', variant: 'digit' },
    { id: 'sub', label: '−', variant: 'operator' },
  ],
  [
    { id: '1', label: '1', variant: 'digit' },
    { id: '2', label: '2', variant: 'digit' },
    { id: '3', label: '3', variant: 'digit' },
    { id: 'add', label: '+', variant: 'operator' },
  ],
  [
    { id: '0', label: '0', variant: 'digit' },
    { id: 'dot', label: '.', variant: 'digit' },
    { id: 'backspace', label: '⌫', icon: 'backspace', variant: 'digit' },
    { id: 'equals', label: '=', variant: 'equals' },
  ],
];

const SimpleCalcWidgetBody: React.FC<{ pluginFolder: PluginFolder }> = ({ pluginFolder }) => {
  const showFraction = pluginFolder.metadata.settings?.show_fraction !== false;
  const maxDecimals = Math.max(1, Math.min(18, Number(pluginFolder.metadata.settings?.max_decimals ?? 15)));

  const [expression, setExpression] = useState<string>('');
  const [resultText, setResultText] = useState<string>('');
  const [fractionParts, setFractionParts] = useState<CalcFractionDisplay | null>(null);
  const [justEvaluated, setJustEvaluated] = useState<boolean>(false);
  const [hasError, setHasError] = useState<boolean>(false);

  const handleAction = (action: string) => {
    if (action === 'ac') {
      setExpression('');
      setResultText('');
      setFractionParts(null);
      setJustEvaluated(false);
      setHasError(false);
      return;
    }

    if (action === 'backspace') {
      if (hasError) {
        setExpression('');
        setResultText('');
        setFractionParts(null);
        setJustEvaluated(false);
        setHasError(false);
        return;
      }
      if (justEvaluated) {
        setJustEvaluated(false);
        setResultText('');
        setFractionParts(null);
        return;
      }
      setExpression((prev) => (prev.endsWith('√(') ? prev.slice(0, -2) : prev.slice(0, -1)));
      return;
    }

    if (/^[0-9]$/.test(action)) {
      if (justEvaluated || hasError) {
        setExpression(action);
        setResultText('');
        setFractionParts(null);
        setJustEvaluated(false);
        setHasError(false);
      } else {
        setExpression((prev) => (prev === '0' ? action : prev + action));
      }
      return;
    }

    if (action === 'dot') {
      if (justEvaluated || hasError) {
        setExpression('0.');
        setResultText('');
        setFractionParts(null);
        setJustEvaluated(false);
        setHasError(false);
        return;
      }
      const segment = expression.split(/[÷×−+()√]/).pop() || '';
      if (segment.includes('.')) return;
      if (!expression || '÷×−+(√'.includes(expression[expression.length - 1])) {
        setExpression((prev) => prev + '0.');
      } else if (expression[expression.length - 1] === ')') {
        setExpression((prev) => prev + '×0.');
      } else {
        setExpression((prev) => prev + '.');
      }
      return;
    }

    if (action === 'div' || action === 'mul' || action === 'sub' || action === 'add') {
      const opChar = { div: '÷', mul: '×', sub: '−', add: '+' }[action];
      let base = expression;
      if (hasError) {
        setExpression(opChar === '−' ? '−' : '0' + opChar);
        setResultText('');
        setFractionParts(null);
        setJustEvaluated(false);
        setHasError(false);
        return;
      }
      if (justEvaluated) {
        base = resultText.replace(/-/g, '−');
        setResultText('');
        setFractionParts(null);
        setJustEvaluated(false);
      }
      if (!base) {
        setExpression(opChar === '−' ? '−' : '0' + opChar);
      } else if (base[base.length - 1] === '(') {
        if (opChar === '−') setExpression(base + '−');
      } else if ('÷×−+'.includes(base[base.length - 1])) {
        if (opChar === '−' && '÷×'.includes(base[base.length - 1])) {
          setExpression(base + '−');
        } else if (
          base === '−' ||
          (base.length >= 2 && base[base.length - 1] === '−' && '÷×('.includes(base[base.length - 2]))
        ) {
          setExpression(base);
        } else {
          setExpression(base.slice(0, -1) + opChar);
        }
      } else {
        setExpression(base + opChar);
      }
      return;
    }

    if (action === 'parens') {
      if (justEvaluated || hasError) {
        setExpression('(');
        setResultText('');
        setFractionParts(null);
        setJustEvaluated(false);
        setHasError(false);
        return;
      }
      if (!expression || expression === '0') {
        setExpression('(');
        return;
      }
      let balance = 0;
      for (const ch of expression) {
        if (ch === '(') balance++;
        else if (ch === ')') balance--;
      }
      const last = expression[expression.length - 1];
      if ('÷×−+(√'.includes(last)) {
        setExpression(expression + '(');
      } else if (balance > 0 && (/\d/.test(last) || last === ')')) {
        setExpression(expression + ')');
      } else {
        setExpression(expression + '(');
      }
      return;
    }

    if (action === 'sqrt') {
      if (justEvaluated || hasError) {
        setExpression('√(');
        setResultText('');
        setFractionParts(null);
        setJustEvaluated(false);
        setHasError(false);
        return;
      }
      setExpression((prev) => (!prev || prev === '0' ? '√(' : prev + '√('));
      return;
    }

    if (action === 'equals') {
      const targetExpr = expression || '0';
      if (!expression) setExpression('0');
      try {
        const val = evaluateCalcExpression(targetExpr);
        let formattedDec = '';
        let nextFrac: CalcFractionDisplay | null = null;

        if (val.exact) {
          formattedDec = formatBigFractionDecimal(val.frac, maxDecimals);
          if (showFraction && val.frac.d !== 1n && val.frac.d <= 100000n) {
            const neg = val.frac.n < 0n;
            const absN = absBig(val.frac.n);
            const whole = absN / val.frac.d;
            const rem = absN % val.frac.d;
            const wholeStr = whole > 0n ? `${neg ? '-' : ''}${whole.toString()}` : neg ? '-' : '';
            nextFrac = { whole: wholeStr, num: rem.toString(), den: val.frac.d.toString() };
          }
        } else {
          if (!Number.isFinite(val.flt)) throw new Error('Overflow');
          formattedDec = parseFloat(val.flt.toFixed(maxDecimals)).toString();
        }

        setResultText(formattedDec);
        setFractionParts(nextFrac);
        setJustEvaluated(true);
        setHasError(false);
      } catch {
        setResultText('Error');
        setFractionParts(null);
        setJustEvaluated(true);
        setHasError(true);
      }
    }
  };

  const primaryDisplay = justEvaluated ? resultText || '0' : expression || '0';
  const primaryTextClass =
    primaryDisplay.length <= 9
      ? 'text-[28px]'
      : primaryDisplay.length <= 13
      ? 'text-[22px]'
      : primaryDisplay.length <= 18
      ? 'text-[17px]'
      : 'text-[14px]';

  return (
    <div className="flex flex-col select-none">
      {/* Two-Line Display Area (stable height so keypad never shifts on '=') */}
      <div className="h-[60px] px-1 pt-0.5 pb-2.5 flex flex-col justify-between items-end">
        <div
          className={`w-full text-right font-sans font-normal text-zinc-100 leading-tight truncate ${primaryTextClass}`}
        >
          {primaryDisplay}
        </div>
        <div className="h-6 flex items-center justify-end text-zinc-300">
          {justEvaluated && showFraction && fractionParts && (
            <span className="inline-flex items-baseline gap-1 font-sans">
              {fractionParts.whole === '-' ? (
                <span className="text-[15px]">−</span>
              ) : fractionParts.whole ? (
                <span className="text-[16px]">{fractionParts.whole}</span>
              ) : null}
              <span className="inline-flex items-center leading-none">
                <sup className="text-[10.5px] font-medium -top-1 relative">{fractionParts.num}</sup>
                <span className="text-[13px] text-zinc-400 mx-[1px]">/</span>
                <sub className="text-[10.5px] font-medium -bottom-0.5 relative">{fractionParts.den}</sub>
              </span>
            </span>
          )}
        </div>
      </div>

      {/* 5x4 Circular Button Keypad */}
      <div className="flex flex-col gap-1.5">
        {SIMPLE_CALC_KEYPAD.map((row, rIdx) => (
          <div key={rIdx} className="grid grid-cols-4 gap-1.5">
            {row.map((btn) => {
              const colorClass =
                btn.variant === 'equals'
                  ? 'bg-[#CFD4E8] hover:bg-[#DCE0F0] active:bg-[#B8BED6] text-zinc-900 font-semibold'
                  : btn.variant === 'operator'
                  ? 'bg-[#3A3D49] hover:bg-[#484C5A] active:bg-[#565A6A] text-zinc-100'
                  : 'bg-[#2B2C30] hover:bg-[#383A40] active:bg-[#464850] text-zinc-100';

              return (
                <button
                  key={btn.id}
                  type="button"
                  onClick={() => handleAction(btn.id)}
                  className={`w-[52px] h-[52px] mx-auto rounded-full flex items-center justify-center transition-colors cursor-pointer ${colorClass}`}
                >
                  {btn.icon === 'sqrt' ? (
                    <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M3 13.5 H6.2 L9.8 19.5 L14 5.5 H21" />
                    </svg>
                  ) : btn.icon === 'backspace' ? (
                    <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M20 6 H9.5 L4 12 L9.5 18 H20 A2 2 0 0 0 22 16 V8 A2 2 0 0 0 20 6 Z" />
                      <path d="M12.2 9.8 L16.8 14.2 M16.8 9.8 L12.2 14.2" />
                    </svg>
                  ) : (
                    <span
                      className={
                        btn.label === 'AC'
                          ? 'text-[16px] leading-none'
                          : btn.label === '()'
                          ? 'text-[18px] leading-none'
                          : btn.variant === 'operator' || btn.variant === 'equals'
                          ? 'text-[21px] leading-none'
                          : 'text-[19px] leading-none'
                      }
                    >
                      {btn.label}
                    </span>
                  )}
                </button>
              );
            })}
          </div>
        ))}
      </div>
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
