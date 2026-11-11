export const en = {
  app: {
    name: "FuncOverlay",
    tagline: "Modular Windows 11 Desktop Overlay Platform",
    status: {
      active: "Overlay Active",
      hidden: "Overlay Standby (Press Hotkey)",
      restarting: "Restarting FuncOverlay service...",
      restarted: "FuncOverlay restarted successfully",
      ready: "System Tray Running",
    },
  },

  hotkey: {
    defaultBind: "Alt+Enter",
    pressToToggle: "Press {hotkey} to toggle overlay",
    pressPrompt: "Press hotkey combination to activate or dismiss",
    listening: "Press key combination...",
    changeHotkey: "Change Hotkey",
    shortcutLabel: "Global Shortcut",
    currentKey: "Current Keybind",
  },

  overlay: {
    backdropAlt: "Overlay Dim Screen",
    noPlugins: {
      badge: "No Plugins Detected",
      title: "How to Install Plugins",
      subtitle: "FuncOverlay is running, but your plugins folder is empty. Follow these simple steps to add custom C# (.dll) or Python (main.py) modules:",
      step1: {
        number: "1",
        title: "Download a Plugin",
        desc: "Get community or official plugins from our project GitHub repository.",
        actionText: "Visit Official GitHub Repository",
        githubUrl: "https://github.com/FuncOverlay/plugins",
      },
      step2: {
        number: "2",
        title: "Place in Plugins Directory",
        desc: "Extract the downloaded plugin archive and place its main folder into the 'plugins' directory located at the root of FuncOverlay.",
        pathHint: "Root Path: ./plugins/{plugin_name}/",
      },
      step3: {
        number: "3",
        title: "Restart Application",
        desc: "Restart FuncOverlay via the System Tray icon in the taskbar for the newly added modules to take effect.",
        actionText: "Restart App Now",
      },
      sampleAction: {
        title: "Test with Sample Plugins",
        btnPython: "+ Add Sample Python Plugin (main.py)",
        btnCsharp: "+ Add Sample C# Plugin (plugin.dll)",
        btnExplorer: "Open Plugins Directory Explorer",
      },
    },
    widget: {
      dragHandle: "Drag to move",
      minimize: "Minimize",
      settings: "Module Settings",
      close: "Disable Module",
      entryPoint: "Entry Point",
      size: "Dimensions",
      position: "Position",
      typePython: "Python Script (main.py)",
      typeCsharp: "C# Assembly (plugin.dll)",
      author: "Author",
      version: "Version",
    },
  },

  tray: {
    tooltip: "FuncOverlay - Active in Tray",
    title: "FuncOverlay System Tray",
    headerDesc: "Background service active",
    toggleOverlay: "Toggle Overlay",
    openSettings: "Settings...",
    openPluginsFolder: "Open 'plugins' Folder",
    csharpGuide: "C# App Blueprint & Code",
    restartApp: "Restart Application",
    exitApp: "Exit FuncOverlay",
  },

  settings: {
    title: "FuncOverlay Settings",
    subtitle: "Configure global hotkeys, visual transition timers, and plugin directories",
    tabs: {
      general: "General & Appearance",
      hotkey: "Hotkey & Trigger",
      plugins: "Plugins Directory",
      csharpDev: "C# Host Blueprint",
    },
    general: {
      themeLabel: "Application Theme",
      themeDark: "Dark Mode (Fluent Dark)",
      themeLight: "Light Mode (Fluent Light)",
      themeSystem: "System Default",
      fadeDurationLabel: "Overlay Fade Duration",
      fadeDurationDesc: "Time taken for screen dimming effect (Default: 0.25s)",
      dimOpacityLabel: "Screen Dimming Opacity",
      dimOpacityDesc: "Darkness level of backdrop overlay (Default: 40%)",
      languageLabel: "Interface Language",
      languageEn: "English (US)",
      autostartLabel: "Run on Windows Startup",
      autostartDesc: "Launch FuncOverlay minimized to System Tray when Windows boots",
    },
    hotkeySection: {
      title: "Global Shortcut Keybind",
      description: "Pressing this key combination anywhere in Windows toggles the FuncOverlay screen within 0.25 seconds.",
      pressPrompt: "Click button below and press desired keys...",
      resetDefault: "Reset to Default (Alt+Enter)",
    },
    pluginsSection: {
      title: "Plugin Directory Management",
      directoryLabel: "Plugins Folder Path",
      directoryDesc: "Root directory scanned for main.py or plugin.dll modules",
      totalInstalled: "Installed Plugins",
      refreshList: "Scan & Reload Plugins",
      openFolder: "View Virtual Directory Files",
    },
    csharpDev: {
      title: "C# (.NET 8) Host Application Architecture",
      desc: "FuncOverlay is designed to run as a native C# WPF/WinUI 3 lightweight process using Windows API hooks for global keybinds and transparent window overlays.",
      snippetTitle: "Core C# Overlay Implementation Snippet",
      copySuccess: "C# Code copied to clipboard!",
      copyBtn: "Copy C# Boilerplate Code",
    },
    saveChanges: "Save Settings",
    close: "Close",
  },

  explorer: {
    title: "FuncOverlay Virtual File Explorer",
    subtitle: "Explore ./plugins directory structure and edit metadata.json files",
    folderTree: "Directory Tree",
    pluginsRoot: "FuncOverlay/plugins/",
    fileContent: "File Content",
    editMetadata: "Live Edit metadata.json",
    saveMetadata: "Save metadata.json",
    createNewPlugin: "Create New Plugin Folder",
    newPluginName: "Plugin Name",
    selectLanguage: "Module Language",
    createBtn: "Generate Module Folder",
    metadataInfo: "Metadata JSON controls screen position (x, y), window size, entry file, and module configuration.",
  },

  samplePlugins: {
    clock: {
      name: "Digital Clock & Date Widget",
      desc: "Sleek Fluent digital timepiece displaying system local time and date.",
    },
    sysinfo: {
      name: "System Hardware Monitor",
      desc: "Live CPU usage, RAM stats, and network latency monitor widget.",
    },
    quicknotes: {
      name: "Overlay Quick Notes",
      desc: "Persistent scratchpad note module for capturing ideas while gaming or working.",
    },
    volume: {
      name: "Audio Volume Mixer",
      desc: "Quick volume slider control module for individual Windows applications.",
    },
  },

  common: {
    enabled: "Enabled",
    disabled: "Disabled",
    apply: "Apply",
    cancel: "Cancel",
    reset: "Reset",
    success: "Success",
    warning: "Warning",
    error: "Error",
    path: "Path",
    none: "None",
  },
};

export type Translations = typeof en;
