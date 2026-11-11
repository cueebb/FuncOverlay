/**
 * @license
 * SPDX-License-Identifier: Apache-2.0
 */

import React from 'react';
import { AppProvider } from './context/AppContext';
import { DesktopSimulator } from './components/DesktopSimulator';
import { OverlayCanvas } from './components/OverlayCanvas';
import { SystemTrayMenu } from './components/SystemTrayMenu';
import { SettingsModal } from './components/SettingsModal';
import { PluginsExplorerModal } from './components/PluginsExplorerModal';
import { CsharpDevGuideModal } from './components/CsharpDevGuideModal';

export default function App() {
  return (
    <AppProvider>
      <div className="relative w-screen h-screen overflow-hidden select-none bg-slate-950 font-sans">
        {/* Windows 11 Desktop Background Canvas */}
        <DesktopSimulator />

        {/* 0.25s Dimming Overlay Canvas with Plugin Modules / Installation Steps */}
        <OverlayCanvas />

        {/* Windows 11 Taskbar & System Tray Menu (Bottom Right) */}
        <SystemTrayMenu />

        {/* Windows 11 Settings Modal Window */}
        <SettingsModal />

        {/* Virtual ./plugins/ File Explorer Modal */}
        <PluginsExplorerModal />

        {/* C# Developer Companion & Architecture Guide */}
        <CsharpDevGuideModal />
      </div>
    </AppProvider>
  );
}
