import React, { useState } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { Terminal, Copy, Check, X } from 'lucide-react';

export const CsharpDevGuideModal: React.FC = () => {
  const { isCsharpSpecOpen, closeCsharpSpec, showToast } = useApp();
  const [activeSnippet, setActiveSnippet] = useState<'workflow' | 'nsis' | 'app' | 'settings' | 'main' | 'pluginManager' | 'tray' | 'interface'>('workflow');
  const [copied, setCopied] = useState(false);

  if (!isCsharpSpecOpen) return null;

  const snippets = {
    workflow: `# =========================================================================
# .github/workflows/dev-build.yml
# GitHub Actions: Automatically builds C# dev build & creates NSIS installer
# Uploads FuncOverlay-Setup-x64.exe to GitHub Actions Workflow Artifacts
# =========================================================================
name: Dev Build & NSIS Installer

on:
  push:
    branches: [ "main", "master", "dev" ]
  pull_request:
    branches: [ "main", "master" ]
  workflow_dispatch:

jobs:
  build-and-package:
    name: Build C# App & Generate NSIS Installer
    runs-on: windows-latest

    steps:
      - name: Checkout Source Code
        uses: actions/checkout@v4

      - name: Setup .NET 8.0 SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore Dependencies
        run: dotnet restore FuncOverlay/FuncOverlay.csproj

      - name: Publish C# Overlay (Release win-x64)
        run: >
          dotnet publish FuncOverlay/FuncOverlay.csproj
          -c Release
          -r win-x64
          --self-contained true
          -p:PublishSingleFile=false
          -o ./publish

      - name: Install Nullsoft Scriptable Install System (NSIS)
        run: choco install nsis -y

      - name: Create Artifacts Output Folder
        run: mkdir artifacts

      - name: Compile Installer with NSIS (makensis)
        run: |
          & "C:\\Program Files (x86)\\NSIS\\makensis.exe" installer/FuncOverlay.nsi

      - name: Upload Installer to GitHub Actions Artifacts
        uses: actions/upload-artifact@v4
        with:
          name: FuncOverlay-DevSetup-x64
          path: artifacts/FuncOverlay-Setup-x64.exe
          if-no-files-found: error
          retention-days: 30`,

    nsis: `; =========================================================================
; installer/FuncOverlay.nsi
; Nullsoft Scriptable Install System (NSIS) Script for FuncOverlay
; Generates: FuncOverlay-Setup-x64.exe with {FuncOverlay_folder}\\plugins directory
; =========================================================================

!define PRODUCT_NAME "FuncOverlay"
!define PRODUCT_VERSION "1.0.0-dev"
!define PRODUCT_PUBLISHER "FuncOverlay"
!define PRODUCT_WEB_SITE "https://github.com/FuncOverlay/plugins"
!define PRODUCT_DIR_REGKEY "Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\FuncOverlay.exe"
!define PRODUCT_UNINST_KEY "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKCU"

; Modern UI (MUI2)
!include "MUI2.nsh"
!define MUI_ABORTWARNING

; Installer Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

; Option to launch on finish
!define MUI_FINISHPAGE_RUN "$INSTDIR\\FuncOverlay.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch FuncOverlay"
!insertmacro MUI_PAGE_FINISH

; Uninstaller Pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; Language
!insertmacro MUI_LANGUAGE "English"

Name "\${PRODUCT_NAME} \${PRODUCT_VERSION}"
OutFile "..\\artifacts\\FuncOverlay-Setup-x64.exe"
InstallDir "$LOCALAPPDATA\\FuncOverlay"
InstallDirRegKey \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_DIR_REGKEY}" ""
ShowInstDetails show
ShowUnInstDetails show
RequestExecutionLevel user

Section "MainSection" SEC01
  SetOutPath "$INSTDIR"
  SetOverwrite ifnewer

  ; Copy all published C# binaries and runtime dependencies
  File /r "..\\publish\\*.*"

  ; Create the plugins folder: {FuncOverlay_folder}\\plugins\\
  CreateDirectory "$INSTDIR\\plugins"

  ; Readme inside plugins folder
  FileOpen $0 "$INSTDIR\\plugins\\README.txt" w
  FileWrite $0 "FuncOverlay Plugins Directory$\r$\n"
  FileWrite $0 "================================$\r$\n"
  FileWrite $0 "Extract plugin folders here:$\r$\n"
  FileWrite $0 "{FuncOverlay_folder}\\plugins\\{plugin_folder}\\$\r$\n"
  FileWrite $0 "Each plugin folder must contain metadata.json and main.py or plugin.dll.$\r$\n"
  FileClose $0

  ; Create Start Menu Shortcuts
  CreateDirectory "$SMPROGRAMS\\FuncOverlay"
  CreateShortCut "$SMPROGRAMS\\FuncOverlay\\FuncOverlay.lnk" "$INSTDIR\\FuncOverlay.exe"
  CreateShortCut "$SMPROGRAMS\\FuncOverlay\\Uninstall FuncOverlay.lnk" "$INSTDIR\\uninst.exe"
  CreateShortCut "$SMPROGRAMS\\FuncOverlay\\Plugins Directory.lnk" "$WINDIR\\explorer.exe" "$INSTDIR\\plugins"

  ; Add/Remove Programs Registry Entries
  WriteRegStr \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_UNINST_KEY}" "DisplayName" "$(^Name)"
  WriteRegStr \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\\uninst.exe"
  WriteRegStr \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\\FuncOverlay.exe"
  WriteRegStr \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_UNINST_KEY}" "DisplayVersion" "\${PRODUCT_VERSION}"
  WriteRegStr \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_UNINST_KEY}" "URLInfoAbout" "\${PRODUCT_WEB_SITE}"
  WriteRegStr \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_UNINST_KEY}" "Publisher" "\${PRODUCT_PUBLISHER}"
  WriteRegStr \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\\FuncOverlay.exe"
SectionEnd

Section -Post
  WriteUninstaller "$INSTDIR\\uninst.exe"
SectionEnd

Section Uninstall
  Delete "$SMPROGRAMS\\FuncOverlay\\FuncOverlay.lnk"
  Delete "$SMPROGRAMS\\FuncOverlay\\Uninstall FuncOverlay.lnk"
  Delete "$SMPROGRAMS\\FuncOverlay\\Plugins Directory.lnk"
  RMDir "$SMPROGRAMS\\FuncOverlay"

  Delete "$INSTDIR\\*.*"
  RMDir /r "$INSTDIR\\plugins"
  RMDir "$INSTDIR"

  DeleteRegKey \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_UNINST_KEY}"
  DeleteRegKey \${PRODUCT_UNINST_ROOT_KEY} "\${PRODUCT_DIR_REGKEY}"
SectionEnd`,

    app: `// =========================================================================
// FuncOverlay - App.xaml.cs & FuncOverlay.csproj
// Resolves CS0104 'Application' ambiguity between WPF and Windows Forms.
// =========================================================================
using System;
using System.Windows;
using FuncOverlay.Tray;
using FuncOverlay.Core;

namespace FuncOverlay
{
    // Explicit System.Windows.Application avoids ambiguity with WinForms Application
    public partial class App : System.Windows.Application
    {
        private MainWindow? _mainWindow;
        private TrayIconManager? _trayManager;
        private PluginManager? _pluginManager;

        private void Application_Startup(object sender, System.Windows.StartupEventArgs e)
        {
            _pluginManager = new PluginManager();
            _pluginManager.LoadAllPlugins();

            _mainWindow = new MainWindow(_pluginManager);
            _mainWindow.Show();

            _trayManager = new TrayIconManager();
            _trayManager.Initialize(
                toggle: () => _mainWindow.ToggleOverlayWithAnimation(),
                openSettings: () => _mainWindow.OpenSettingsWindow(),
                restart: () => RestartApplication()
            );
        }

        private void RestartApplication()
        {
            _pluginManager?.LoadAllPlugins();
            _mainWindow?.ReloadOverlayContent();
        }

        private void Application_Exit(object sender, System.Windows.ExitEventArgs e)
        {
            _trayManager?.Dispose();
        }
    }
}`,

    settings: `// =========================================================================
// FuncOverlay - SettingsWindow.xaml.cs (C# .NET 8 WPF)
// Implements: General (Transition ms, Overlay Alpha, Hotkey Recorder Alt+Space),
// Plugins (Open folder, Restart, Install .zip archive, Dynamic metadata settings)
// =========================================================================
using System;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using FuncOverlay.Core;

using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;

namespace FuncOverlay
{
    public partial class SettingsWindow : Window
    {
        private readonly PluginManager _pluginManager;
        private readonly MainWindow _mainWindow;
        private bool _isRecordingKeybind = false;

        public SettingsWindow(PluginManager pluginManager, MainWindow mainWindow)
        {
            _pluginManager = pluginManager;
            _mainWindow = mainWindow;
            InitializeComponent();

            TransitionSlider.Value = _mainWindow.FadeDurationMs;
            AlphaSlider.Value = Math.Round(_mainWindow.OverlayOpacity * 100);
            HotkeyTextBox.Text = _mainWindow.HotkeyDisplayString;
            RefreshInstalledPluginsList();
        }

        private void InstallPlugin_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "Select Plugin Archive (.zip)",
                Filter = "Plugin Archive (*.zip)|*.zip|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                _pluginManager.ExtractPluginArchive(dlg.FileName);
                _mainWindow.ReloadOverlayContent();
                RefreshInstalledPluginsList();
            }
        }
    }
}`,

    main: `// =========================================================================
// FuncOverlay - MainWindow.xaml.cs (C# .NET 8 WPF / WinUI 3)
// Handles: Win32 Global Keybind (Alt+Enter), 0.25s Opacity Backdrop Animation
// =========================================================================
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace FuncOverlay
{
    public partial class MainWindow : Window
    {
        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001; // Alt Key
        private const uint VK_RETURN = 0x0D; // Enter Key

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        public MainWindow()
        {
            InitializeComponent();
            
            this.Topmost = true;
            this.ShowInTaskbar = false;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = System.Windows.Media.Brushes.Transparent;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            RegisterHotKey(handle, HOTKEY_ID, MOD_ALT, VK_RETURN);

            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(HwndHook);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleOverlayWithAnimation();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void ToggleOverlayWithAnimation()
        {
            double targetOpacity = (this.Visibility == Visibility.Visible && this.Opacity > 0) ? 0.0 : 0.40;
            
            if (targetOpacity > 0)
            {
                this.Visibility = Visibility.Visible;
            }

            // 0.25 seconds opacity fade animation
            DoubleAnimation fadeAnim = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromSeconds(0.25),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            fadeAnim.Completed += (s, e) =>
            {
                if (targetOpacity == 0)
                {
                    this.Visibility = Visibility.Collapsed;
                }
            };

            this.BeginAnimation(Window.OpacityProperty, fadeAnim);
        }
    }
}`,

    pluginManager: `// =========================================================================
// FuncOverlay - PluginManager.cs
// Scans {FuncOverlay_folder}/plugins/ subfolders for main.py (Python) or plugin.dll (C#)
// =========================================================================
using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;

namespace FuncOverlay.Core
{
    public class PluginMetadata
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public string entryPoint { get; set; } = "main.py"; // "main.py" or "plugin.dll"
        public string language { get; set; } = "python"; // "python" or "csharp"
        public Position position { get; set; } = new Position();
        public Size size { get; set; } = new Size();
        public bool enabled { get; set; } = true;
    }

    public class Position { public int x { get; set; } public int y { get; set; } }
    public class Size { public int width { get; set; } public int height { get; set; } }

    public class PluginManager
    {
        public string PluginsDirectory { get; private set; }

        public PluginManager()
        {
            PluginsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            if (!Directory.Exists(PluginsDirectory))
            {
                Directory.CreateDirectory(PluginsDirectory);
            }
        }
    }
}`,

    tray: `// =========================================================================
// FuncOverlay - TrayIconManager.cs (Custom Black & White WPF Tray Popup)
// Zero default Windows ContextMenuStrip shell UI
// =========================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;

namespace FuncOverlay.Tray
{
    public class TrayIconManager : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private TrayMenuWindow? _trayMenuWindow;

        public void Initialize(
            Action toggle,
            Action openSettings,
            Action restart,
            Func<bool> isOverlayVisible,
            Func<string> getHotkeyDisplay)
        {
            _trayMenuWindow = new TrayMenuWindow(
                toggleAction: toggle,
                openSettingsAction: openSettings,
                openPluginsAction: OpenPluginsFolder,
                restartAction: restart,
                exitAction: () => System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown())
            );

            _notifyIcon = new NotifyIcon
            {
                Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico")),
                Text = "FuncOverlay",
                Visible = true
            };

            _notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Left)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        _trayMenuWindow?.ToggleMenu(isOverlayVisible(), getHotkeyDisplay()));
                }
            };
        }
    }
}`,

    interface: `// =========================================================================
// FuncOverlay SDK - IFuncOverlayPlugin.cs (For C# .dll Plugins)
// =========================================================================
namespace FuncOverlay.Sdk
{
    public interface IFuncOverlayPlugin
    {
        void OnLoad(IOverlayContext context);
        void OnRender();
        void OnUnload();
    }

    public interface IOverlayContext
    {
        string PluginDirectory { get; }
    }
}`,
  };

  const handleCopy = (code: string) => {
    navigator.clipboard.writeText(code);
    setCopied(true);
    showToast('Code copied to clipboard');
    setTimeout(() => setCopied(false), 2500);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-md select-none animate-in fade-in duration-150">
      <div className="w-[880px] max-w-[96vw] h-[600px] max-h-[94vh] rounded-2xl bg-zinc-950 border border-zinc-800 flex flex-col overflow-hidden text-zinc-100 shadow-2xl font-sans">
        {/* Title Bar */}
        <div className="h-11 px-5 bg-zinc-900 border-b border-zinc-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Terminal className="w-4 h-4 text-zinc-400" />
            <span className="font-semibold text-xs text-white">FuncOverlay - C#, GitHub Actions & NSIS Blueprint</span>
          </div>
          <button
            onClick={closeCsharpSpec}
            className="p-1 rounded hover:bg-zinc-800 text-zinc-400 hover:text-white transition-colors cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Snippet Tabs */}
        <div className="h-10 bg-zinc-950 border-b border-zinc-800 px-4 flex items-center justify-between text-xs font-mono overflow-x-auto">
          <div className="flex gap-1.5 shrink-0">
            <button
              onClick={() => setActiveSnippet('workflow')}
              className={`px-2.5 py-1.5 rounded-t transition-colors cursor-pointer whitespace-nowrap ${
                activeSnippet === 'workflow'
                  ? 'bg-zinc-900 text-white font-bold border-t-2 border-zinc-300'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              dev-build.yml (GitHub Action)
            </button>
            <button
              onClick={() => setActiveSnippet('nsis')}
              className={`px-2.5 py-1.5 rounded-t transition-colors cursor-pointer whitespace-nowrap ${
                activeSnippet === 'nsis'
                  ? 'bg-zinc-900 text-white font-bold border-t-2 border-zinc-300'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              FuncOverlay.nsi (NSIS)
            </button>
            <button
              onClick={() => setActiveSnippet('app')}
              className={`px-2.5 py-1.5 rounded-t transition-colors cursor-pointer whitespace-nowrap ${
                activeSnippet === 'app'
                  ? 'bg-zinc-900 text-white font-bold border-t-2 border-zinc-300'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              App.xaml.cs
            </button>
            <button
              onClick={() => setActiveSnippet('settings')}
              className={`px-2.5 py-1.5 rounded-t transition-colors cursor-pointer whitespace-nowrap ${
                activeSnippet === 'settings'
                  ? 'bg-zinc-900 text-white font-bold border-t-2 border-zinc-300'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              SettingsWindow.xaml.cs
            </button>
            <button
              onClick={() => setActiveSnippet('main')}
              className={`px-2.5 py-1.5 rounded-t transition-colors cursor-pointer whitespace-nowrap ${
                activeSnippet === 'main'
                  ? 'bg-zinc-900 text-white font-bold border-t-2 border-zinc-300'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              MainWindow.xaml.cs
            </button>
            <button
              onClick={() => setActiveSnippet('pluginManager')}
              className={`px-2.5 py-1.5 rounded-t transition-colors cursor-pointer whitespace-nowrap ${
                activeSnippet === 'pluginManager'
                  ? 'bg-zinc-900 text-white font-bold border-t-2 border-zinc-300'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              PluginManager.cs
            </button>
            <button
              onClick={() => setActiveSnippet('tray')}
              className={`px-2.5 py-1.5 rounded-t transition-colors cursor-pointer whitespace-nowrap ${
                activeSnippet === 'tray'
                  ? 'bg-zinc-900 text-white font-bold border-t-2 border-zinc-300'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              TrayIconManager.cs
            </button>
            <button
              onClick={() => setActiveSnippet('interface')}
              className={`px-2.5 py-1.5 rounded-t transition-colors cursor-pointer whitespace-nowrap ${
                activeSnippet === 'interface'
                  ? 'bg-zinc-900 text-white font-bold border-t-2 border-zinc-300'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              IFuncOverlayPlugin.cs
            </button>
          </div>

          <button
            onClick={() => handleCopy(snippets[activeSnippet])}
            className="px-3 py-1 rounded bg-zinc-200 hover:bg-white text-zinc-950 font-sans text-xs flex items-center gap-1.5 cursor-pointer shrink-0 ml-2"
          >
            {copied ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
            <span>{copied ? 'Copied' : 'Copy'}</span>
          </button>
        </div>

        {/* Code Output Viewport */}
        <div className="flex-1 p-4 bg-black overflow-auto font-mono text-xs text-zinc-300 leading-relaxed">
          <pre>{snippets[activeSnippet]}</pre>
        </div>

        {/* Footer */}
        <div className="h-9 px-4 bg-zinc-900 border-t border-zinc-800 flex items-center justify-between text-[11px] font-mono text-zinc-500">
          <span>Dev Build: NSIS Installer (.exe) generated on push and uploaded to artifacts</span>
          <span>FuncOverlay Architecture</span>
        </div>
      </div>
    </div>
  );
};
