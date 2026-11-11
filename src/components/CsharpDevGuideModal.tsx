import React, { useState } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { Terminal, Copy, Check, X, Code2, Cpu, FileCode, Layers, Play } from 'lucide-react';

export const CsharpDevGuideModal: React.FC = () => {
  const { isCsharpSpecOpen, closeCsharpSpec, showToast } = useApp();
  const [activeSnippet, setActiveSnippet] = useState<'main' | 'pluginManager' | 'tray' | 'interface'>('main');
  const [copied, setCopied] = useState(false);

  if (!isCsharpSpecOpen) return null;

  const snippets = {
    main: `// =========================================================================
// FuncOverlay - MainWindow.xaml.cs (C# .NET 8 WPF / WinUI 3)
// Handles: Win32 Global Keybind (Alt+Enter), 0.25s Opacity Backdrop Animation, WS_EX_TOOLWINDOW
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

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainWindow()
        {
            InitializeComponent();
            
            // Set window properties for overlay
            this.Topmost = true;
            this.ShowInTaskbar = false; // Hide from taskbar, present in tray only
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = System.Windows.Media.Brushes.Transparent;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            
            // Register global keybind Alt+Enter
            bool registered = RegisterHotKey(handle, HOTKEY_ID, MOD_ALT, VK_RETURN);
            if (!registered)
            {
                MessageBox.Show("Failed to register Alt+Enter global keybind.", "FuncOverlay Warning");
            }

            // Hook Win32 message loop
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

            // 0.25 seconds smooth opacity fade transition
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
// Scans ./plugins/ subfolders for main.py (Python) or plugin.dll (C# Assembly) & parses metadata.json
// =========================================================================
using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Diagnostics;

namespace FuncOverlay.Core
{
    public class PluginMetadata
    {
        public string id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public string entryPoint { get; set; } // "main.py" or "plugin.dll"
        public string language { get; set; } // "python" or "csharp"
        public Position position { get; set; }
        public Size size { get; set; }
        public bool enabled { get; set; }
    }

    public class Position { public int x { get; set; } public int y { get; set; } }
    public class Size { public int width { get; set; } public int height { get; set; } }

    public class PluginManager
    {
        public string PluginsDirectory { get; private set; }
        public List<PluginMetadata> LoadedPlugins { get; private set; } = new List<PluginMetadata>();

        public PluginManager()
        {
            // Root path ./plugins/
            PluginsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            if (!Directory.Exists(PluginsDirectory))
            {
                Directory.CreateDirectory(PluginsDirectory);
            }
        }

        public void LoadAllPlugins()
        {
            LoadedPlugins.Clear();
            string[] subdirectories = Directory.GetDirectories(PluginsDirectory);

            foreach (string folderPath in subdirectories)
            {
                string metaFile = Path.Combine(folderPath, "metadata.json");
                if (File.Exists(metaFile))
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(metaFile);
                        var meta = JsonSerializer.Deserialize<PluginMetadata>(jsonContent);
                        
                        if (meta != null && meta.enabled)
                        {
                            LoadedPlugins.Add(meta);
                            ExecutePluginEntryPoint(folderPath, meta);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error loading plugin in {folderPath}]: {ex.Message}");
                    }
                }
            }
        }

        private void ExecutePluginEntryPoint(string folderPath, PluginMetadata meta)
        {
            if (meta.entryPoint.EndsWith(".py"))
            {
                // Execute Python process
                string scriptPath = Path.Combine(folderPath, meta.entryPoint);
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "python.exe",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            else if (meta.entryPoint.EndsWith(".dll"))
            {
                // Load C# Assembly via Reflection
                string dllPath = Path.Combine(folderPath, meta.entryPoint);
                System.Reflection.Assembly asm = System.Reflection.Assembly.LoadFrom(dllPath);
                Console.WriteLine($"Loaded C# Plugin Assembly: {asm.FullName}");
            }
        }
    }
}`,

    tray: `// =========================================================================
// FuncOverlay - TrayIconManager.cs (Win11 System Tray Control)
// Handles: System Tray icon, Context Menu (Toggle, Settings, Restart, Exit)
// =========================================================================
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FuncOverlay.Tray
{
    public class TrayIconManager
    {
        private NotifyIcon _notifyIcon;
        private Action _toggleCallback;
        private Action _openSettingsCallback;
        private Action _restartCallback;

        public void Initialize(Action toggle, Action openSettings, Action restart)
        {
            _toggleCallback = toggle;
            _openSettingsCallback = openSettings;
            _restartCallback = restart;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Toggle Overlay (Alt+Enter)", null, (s, e) => _toggleCallback?.Invoke());
            menu.Items.Add("Settings...", null, (s, e) => _openSettingsCallback?.Invoke());
            menu.Items.Add("Open 'plugins' Folder", null, (s, e) => OpenPluginsFolder());
            menu.Items.Add("-");
            menu.Items.Add("Restart Application", null, (s, e) => _restartCallback?.Invoke());
            menu.Items.Add("Exit FuncOverlay", null, (s, e) => Application.Exit());

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "FuncOverlay - Active in Tray",
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => _toggleCallback?.Invoke();
        }

        private void OpenPluginsFolder()
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            System.Diagnostics.Process.Start("explorer.exe", path);
        }
    }
}`,

    interface: `// =========================================================================
// FuncOverlay SDK - IFuncOverlayPlugin.cs (For C# .dll Plugins)
// Create a C# Class Library targeting .NET 8 and implement this interface!
// =========================================================================
namespace FuncOverlay.Sdk
{
    public interface IFuncOverlayPlugin
    {
        /// <summary>
        /// Called when FuncOverlay initializes the plugin on boot or hotkey trigger.
        /// </summary>
        void OnLoad(IOverlayContext context);

        /// <summary>
        /// Called every frame tick for overlay drawing or UI updates.
        /// </summary>
        void OnRender();

        /// <summary>
        /// Called when the user disables the module or exits the app.
        /// </summary>
        void OnUnload();
    }

    public interface IOverlayContext
    {
        string PluginDirectory { get; }
        void LogMessage(string message);
        void UpdateMetadata(string key, object value);
    }
}`,
  };

  const handleCopy = (code: string) => {
    navigator.clipboard.writeText(code);
    setCopied(true);
    showToast('C# Boilerplate copied to clipboard!');
    setTimeout(() => setCopied(false), 2500);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 backdrop-blur-md select-none animate-in fade-in duration-150">
      <div className="w-[880px] max-w-[96vw] h-[620px] max-h-[94vh] rounded-2xl fluent-glass border border-white/20 flex flex-col overflow-hidden text-slate-100 shadow-2xl">
        {/* Title Bar */}
        <div className="h-12 px-5 bg-slate-950/90 border-b border-white/10 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Terminal className="w-4 h-4 text-emerald-400" />
            <span className="font-semibold text-sm">FuncOverlay - C# (.NET 8) Host Code Blueprint</span>
          </div>
          <button
            onClick={closeCsharpSpec}
            className="p-1 rounded hover:bg-white/10 text-slate-400 hover:text-white transition-colors cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Snippet Tabs */}
        <div className="h-10 bg-slate-900/80 border-b border-white/10 px-4 flex items-center justify-between text-xs font-mono">
          <div className="flex gap-2">
            <button
              onClick={() => setActiveSnippet('main')}
              className={`px-3 py-1.5 rounded-t-lg transition-colors cursor-pointer ${
                activeSnippet === 'main'
                  ? 'bg-slate-950 text-emerald-300 border-t-2 border-emerald-400 font-bold'
                  : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              MainWindow.xaml.cs (0.25s Fade + Keybind)
            </button>
            <button
              onClick={() => setActiveSnippet('pluginManager')}
              className={`px-3 py-1.5 rounded-t-lg transition-colors cursor-pointer ${
                activeSnippet === 'pluginManager'
                  ? 'bg-slate-950 text-amber-300 border-t-2 border-amber-400 font-bold'
                  : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              PluginManager.cs (plugins/ Loader)
            </button>
            <button
              onClick={() => setActiveSnippet('tray')}
              className={`px-3 py-1.5 rounded-t-lg transition-colors cursor-pointer ${
                activeSnippet === 'tray'
                  ? 'bg-slate-950 text-blue-300 border-t-2 border-blue-400 font-bold'
                  : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              TrayIconManager.cs (System Tray)
            </button>
            <button
              onClick={() => setActiveSnippet('interface')}
              className={`px-3 py-1.5 rounded-t-lg transition-colors cursor-pointer ${
                activeSnippet === 'interface'
                  ? 'bg-slate-950 text-indigo-300 border-t-2 border-indigo-400 font-bold'
                  : 'text-slate-400 hover:text-slate-200'
              }`}
            >
              IFuncOverlayPlugin.cs (SDK)
            </button>
          </div>

          <button
            onClick={() => handleCopy(snippets[activeSnippet])}
            className="px-3 py-1 rounded bg-emerald-600 hover:bg-emerald-500 text-white font-sans text-xs flex items-center gap-1.5 cursor-pointer shadow transition-all active:scale-95"
          >
            {copied ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
            <span>{copied ? 'Copied!' : 'Copy Code'}</span>
          </button>
        </div>

        {/* Code Output Viewport */}
        <div className="flex-1 p-4 bg-slate-950 overflow-auto font-mono text-xs text-slate-200 leading-relaxed">
          <pre className="selection:bg-emerald-800">{snippets[activeSnippet]}</pre>
        </div>

        {/* Footer */}
        <div className="h-10 px-4 bg-slate-950 border-t border-white/10 flex items-center justify-between text-[11px] font-mono text-slate-400">
          <span>Target Platform: C# WPF / WinUI 3 (.NET 8.0 Windows Desktop)</span>
          <span className="text-emerald-400 font-bold">FuncOverlay Host Architecture</span>
        </div>
      </div>
    </div>
  );
};
