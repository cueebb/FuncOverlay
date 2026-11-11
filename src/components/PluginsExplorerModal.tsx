import React, { useState, useEffect } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { PluginFolder, PluginVirtualFile } from '../types/plugin';
import {
  Folder,
  FileCode,
  FileText,
  X,
  Save,
  Plus,
  Trash2,
  FolderPlus,
  Code2,
  Check,
  AlertCircle,
  Copy,
} from 'lucide-react';

export const PluginsExplorerModal: React.FC = () => {
  const {
    isExplorerOpen,
    closeExplorer,
    plugins,
    selectedExplorerFolder,
    saveMetadataJsonFile,
    addCustomPluginFolder,
    deletePlugin,
  } = useApp();

  const [activeFolder, setActiveFolder] = useState<string | null>(null);
  const [activeFile, setActiveFile] = useState<PluginVirtualFile | null>(null);
  const [fileContent, setFileContent] = useState<string>('');
  const [saveStatus, setSaveStatus] = useState<string | null>(null);

  // New Folder Creation Form state
  const [isCreatingNew, setIsCreatingNew] = useState(false);
  const [newFolderName, setNewFolderName] = useState('');
  const [newLanguage, setNewLanguage] = useState<'python' | 'csharp'>('python');
  const [newName, setNewName] = useState('');
  const [newDesc, setNewDesc] = useState('');

  // Sync active folder selection
  useEffect(() => {
    if (selectedExplorerFolder && plugins.some((p) => p.folderName === selectedExplorerFolder)) {
      setActiveFolder(selectedExplorerFolder);
    } else if (plugins.length > 0 && !activeFolder) {
      setActiveFolder(plugins[0].folderName);
    }
  }, [selectedExplorerFolder, plugins, activeFolder]);

  // Sync active file when activeFolder changes
  useEffect(() => {
    if (!activeFolder) return;
    const currentFolderObj = plugins.find((p) => p.folderName === activeFolder);
    if (currentFolderObj && currentFolderObj.files.length > 0) {
      const metaFile = currentFolderObj.files.find((f) => f.isMetadata) || currentFolderObj.files[0];
      setActiveFile(metaFile);
      setFileContent(metaFile.content);
    }
  }, [activeFolder, plugins]);

  if (!isExplorerOpen) return null;

  const currentFolderObj = plugins.find((p) => p.folderName === activeFolder);

  const handleSelectFile = (file: PluginVirtualFile) => {
    setActiveFile(file);
    setFileContent(file.content);
    setSaveStatus(null);
  };

  const handleSaveFile = () => {
    if (!activeFolder || !activeFile) return;
    if (activeFile.isMetadata) {
      const success = saveMetadataJsonFile(activeFolder, fileContent);
      if (success) {
        setSaveStatus('Saved metadata.json successfully!');
        setTimeout(() => setSaveStatus(null), 3000);
      }
    } else {
      setSaveStatus('File preview saved');
      setTimeout(() => setSaveStatus(null), 2000);
    }
  };

  const handleCreateFolderSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newFolderName) return;
    addCustomPluginFolder(newFolderName, newLanguage, newName, newDesc);
    setActiveFolder(newFolderName.replace(/[^a-zA-Z0-9_-]/g, ''));
    setIsCreatingNew(false);
    setNewFolderName('');
    setNewName('');
    setNewDesc('');
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-md select-none animate-in fade-in duration-150">
      <div className="w-[840px] max-w-[96vw] h-[580px] max-h-[92vh] rounded-2xl fluent-glass border border-white/20 flex flex-col overflow-hidden text-slate-100 shadow-2xl">
        {/* Title Bar */}
        <div className="h-12 px-5 bg-slate-950/90 border-b border-white/10 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Folder className="w-4 h-4 text-amber-400" />
            <span className="font-semibold text-sm">{t('explorer.title')}</span>
            <span className="font-mono text-xs text-amber-300/80">({t('explorer.pluginsRoot')})</span>
          </div>
          <button
            onClick={closeExplorer}
            className="p-1 rounded hover:bg-white/10 text-slate-400 hover:text-white transition-colors cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Directory Explorer Body */}
        <div className="flex-1 flex overflow-hidden">
          {/* Left Column: Plugin Folders List */}
          <div className="w-64 bg-slate-950/60 border-r border-white/10 p-3 flex flex-col justify-between overflow-y-auto">
            <div className="space-y-1">
              <div className="px-2 py-1 text-[11px] font-mono text-slate-400 flex items-center justify-between mb-1">
                <span>PLUGINS SUBFOLDERS</span>
                <span className="text-amber-400 font-bold">{plugins.length}</span>
              </div>

              {plugins.length === 0 ? (
                <div className="p-4 text-center text-slate-500 text-xs italic">
                  No plugin folders found in ./plugins/
                </div>
              ) : (
                plugins.map((pf) => (
                  <button
                    key={pf.folderName}
                    onClick={() => setActiveFolder(pf.folderName)}
                    className={`w-full flex items-center justify-between px-3 py-2 rounded-xl text-xs transition-colors cursor-pointer ${
                      activeFolder === pf.folderName
                        ? 'bg-amber-500/20 text-amber-300 border border-amber-500/30 font-medium'
                        : 'text-slate-300 hover:bg-white/5'
                    }`}
                  >
                    <div className="flex items-center gap-2 overflow-hidden">
                      <Folder className="w-3.5 h-3.5 text-amber-400 shrink-0" />
                      <span className="truncate">{pf.folderName}</span>
                    </div>
                    <span
                      className={`text-[9px] px-1 rounded uppercase font-mono border ${
                        pf.metadata.language === 'python'
                          ? 'bg-amber-500/20 text-amber-300 border-amber-500/30'
                          : 'bg-indigo-500/20 text-indigo-300 border-indigo-500/30'
                      }`}
                    >
                      {pf.metadata.language}
                    </span>
                  </button>
                ))
              )}
            </div>

            {/* Create Plugin Folder Action */}
            <div className="pt-3 border-t border-white/10">
              <button
                onClick={() => setIsCreatingNew((prev) => !prev)}
                className="w-full py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white font-medium text-xs flex items-center justify-center gap-2 transition-all cursor-pointer shadow-md"
              >
                <FolderPlus className="w-4 h-4" />
                <span>+ Create Plugin Folder</span>
              </button>
            </div>
          </div>

          {/* Center Column: Files inside selected folder */}
          <div className="w-48 bg-slate-900/40 border-r border-white/10 p-3 overflow-y-auto">
            {currentFolderObj ? (
              <div className="space-y-1 text-xs">
                <div className="px-2 py-1 text-[10px] font-mono text-slate-400">
                  FILES IN /{currentFolderObj.folderName}
                </div>
                {currentFolderObj.files.map((file) => (
                  <button
                    key={file.name}
                    onClick={() => handleSelectFile(file)}
                    className={`w-full flex items-center gap-2 px-2.5 py-2 rounded-lg text-xs transition-colors cursor-pointer ${
                      activeFile?.name === file.name
                        ? 'bg-blue-600 text-white font-medium shadow'
                        : 'text-slate-300 hover:bg-white/5'
                    }`}
                  >
                    {file.isMetadata ? (
                      <FileCode className="w-3.5 h-3.5 text-amber-400" />
                    ) : (
                      <Code2 className="w-3.5 h-3.5 text-blue-400" />
                    )}
                    <span className="truncate">{file.name}</span>
                  </button>
                ))}

                <div className="pt-4">
                  <button
                    onClick={() => {
                      if (confirm(`Delete plugin directory './plugins/${currentFolderObj.folderName}'?`)) {
                        deletePlugin(currentFolderObj.metadata.id);
                        setActiveFolder(null);
                      }
                    }}
                    className="w-full py-1.5 px-2 rounded-lg bg-red-500/20 hover:bg-red-500/30 border border-red-500/30 text-red-300 text-[11px] font-medium flex items-center justify-center gap-1 cursor-pointer transition-colors"
                  >
                    <Trash2 className="w-3 h-3 text-red-400" />
                    <span>Delete Folder</span>
                  </button>
                </div>
              </div>
            ) : (
              <div className="text-slate-500 text-xs italic text-center py-8">Select a folder</div>
            )}
          </div>

          {/* Right Column: Code Editor & Metadata Inspector */}
          <div className="flex-1 p-4 bg-slate-950/80 flex flex-col justify-between overflow-hidden">
            {isCreatingNew ? (
              /* CREATE NEW PLUGIN SUBFOLDER FORM */
              <form onSubmit={handleCreateFolderSubmit} className="space-y-4 overflow-y-auto p-2 text-xs">
                <h3 className="text-sm font-semibold text-white flex items-center gap-2">
                  <FolderPlus className="w-4 h-4 text-blue-400" />
                  <span>Create New Plugin Subfolder</span>
                </h3>

                <div className="space-y-1">
                  <label className="text-slate-300 font-medium">Folder Name (in ./plugins/):</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. MyCustomWidget"
                    value={newFolderName}
                    onChange={(e) => setNewFolderName(e.target.value)}
                    className="w-full bg-slate-900 border border-white/10 rounded-lg p-2 font-mono text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div className="space-y-1">
                  <label className="text-slate-300 font-medium">Module Language & Main File:</label>
                  <div className="flex gap-2">
                    <button
                      type="button"
                      onClick={() => setNewLanguage('python')}
                      className={`flex-1 py-2 rounded-lg border text-xs font-semibold cursor-pointer ${
                        newLanguage === 'python' ? 'bg-amber-600 text-white border-amber-500' : 'bg-slate-900 text-slate-400 border-white/10'
                      }`}
                    >
                      Python (main.py)
                    </button>
                    <button
                      type="button"
                      onClick={() => setNewLanguage('csharp')}
                      className={`flex-1 py-2 rounded-lg border text-xs font-semibold cursor-pointer ${
                        newLanguage === 'csharp' ? 'bg-indigo-600 text-white border-indigo-500' : 'bg-slate-900 text-slate-400 border-white/10'
                      }`}
                    >
                      C# (.NET plugin.dll)
                    </button>
                  </div>
                </div>

                <div className="space-y-1">
                  <label className="text-slate-300 font-medium">Display Name:</label>
                  <input
                    type="text"
                    placeholder="e.g. My Custom Plugin"
                    value={newName}
                    onChange={(e) => setNewName(e.target.value)}
                    className="w-full bg-slate-900 border border-white/10 rounded-lg p-2 text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div className="space-y-1">
                  <label className="text-slate-300 font-medium">Description:</label>
                  <input
                    type="text"
                    placeholder="e.g. Overlay module description..."
                    value={newDesc}
                    onChange={(e) => setNewDesc(e.target.value)}
                    className="w-full bg-slate-900 border border-white/10 rounded-lg p-2 text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div className="flex gap-2 pt-2">
                  <button
                    type="submit"
                    className="px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white font-medium cursor-pointer"
                  >
                    Generate Subfolder
                  </button>
                  <button
                    type="button"
                    onClick={() => setIsCreatingNew(false)}
                    className="px-4 py-2 rounded-xl bg-slate-800 text-slate-300 cursor-pointer"
                  >
                    Cancel
                  </button>
                </div>
              </form>
            ) : activeFile ? (
              /* FILE EDITOR / METADATA JSON INSPECTOR */
              <div className="flex-1 flex flex-col overflow-hidden">
                <div className="flex items-center justify-between mb-2">
                  <div className="flex items-center gap-2 font-mono text-xs text-blue-400">
                    <span>{activeFile.path}</span>
                    {activeFile.isMetadata && (
                      <span className="px-2 py-0.5 rounded bg-amber-500/20 text-amber-300 text-[10px] border border-amber-500/30">
                        Live Metadata JSON
                      </span>
                    )}
                  </div>
                  {saveStatus && (
                    <span className="text-xs text-emerald-400 font-medium flex items-center gap-1 animate-in fade-in">
                      <Check className="w-3.5 h-3.5" />
                      {saveStatus}
                    </span>
                  )}
                </div>

                <textarea
                  value={fileContent}
                  onChange={(e) => setFileContent(e.target.value)}
                  className="flex-1 w-full bg-slate-900 border border-white/10 rounded-xl p-3 font-mono text-xs text-emerald-300 leading-relaxed resize-none focus:outline-none focus:border-blue-500/60"
                  spellCheck={false}
                />

                <div className="mt-3 flex items-center justify-between">
                  <span className="text-[11px] text-slate-500 font-mono">
                    {activeFile.isMetadata ? 'Edit JSON position (x, y) & size to reposition overlay window' : 'Read-only code preview'}
                  </span>
                  <button
                    onClick={handleSaveFile}
                    className="px-4 py-1.5 rounded-lg bg-blue-600 hover:bg-blue-500 text-white font-medium text-xs flex items-center gap-1.5 cursor-pointer shadow transition-all active:scale-95"
                  >
                    <Save className="w-3.5 h-3.5" />
                    <span>Save {activeFile.name}</span>
                  </button>
                </div>
              </div>
            ) : (
              <div className="flex-1 flex items-center justify-center text-slate-500 text-xs italic">
                Select a file to view or edit
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
