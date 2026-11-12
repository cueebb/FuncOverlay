import React, { useState, useEffect } from 'react';
import { useApp } from '../context/AppContext';
import { t } from '../i18n';
import { PluginVirtualFile } from '../types/plugin';
import {
  Folder,
  FileCode,
  X,
  Save,
  Trash2,
  FolderPlus,
  Code2,
  Check,
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

  const [isCreatingNew, setIsCreatingNew] = useState(false);
  const [newFolderName, setNewFolderName] = useState('');
  const [newLanguage, setNewLanguage] = useState<'python' | 'csharp'>('python');
  const [newName, setNewName] = useState('');
  const [newDesc, setNewDesc] = useState('');

  useEffect(() => {
    if (selectedExplorerFolder && plugins.some((p) => p.folderName === selectedExplorerFolder)) {
      setActiveFolder(selectedExplorerFolder);
    } else if (plugins.length > 0 && !activeFolder) {
      setActiveFolder(plugins[0].folderName);
    }
  }, [selectedExplorerFolder, plugins, activeFolder]);

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
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-md select-none animate-in fade-in duration-150">
      <div className="w-[800px] max-w-[96vw] h-[540px] max-h-[92vh] rounded-2xl bg-zinc-950 border border-zinc-800 flex flex-col overflow-hidden text-zinc-100 shadow-2xl font-sans">
        {/* Title Bar */}
        <div className="h-11 px-5 bg-zinc-900 border-b border-zinc-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Folder className="w-4 h-4 text-zinc-400" />
            <span className="font-semibold text-xs text-white">{t('explorer.title')}</span>
            <span className="font-mono text-xs text-zinc-400">({t('explorer.pluginsRoot')})</span>
          </div>
          <button
            onClick={closeExplorer}
            className="p-1 rounded hover:bg-zinc-800 text-zinc-400 hover:text-white transition-colors cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Directory Explorer Body */}
        <div className="flex-1 flex overflow-hidden">
          {/* Folders List */}
          <div className="w-60 bg-zinc-950 border-r border-zinc-800 p-3 flex flex-col justify-between overflow-y-auto">
            <div className="space-y-1">
              <div className="px-2 py-1 text-[11px] font-mono text-zinc-500 flex items-center justify-between mb-1">
                <span>PLUGINS FOLDERS</span>
                <span className="text-zinc-300 font-bold">{plugins.length}</span>
              </div>

              {plugins.length === 0 ? (
                <div className="p-4 text-center text-zinc-500 text-xs italic">
                  No plugin folders found in ./plugins/
                </div>
              ) : (
                plugins.map((pf) => (
                  <button
                    key={pf.folderName}
                    onClick={() => setActiveFolder(pf.folderName)}
                    className={`w-full flex items-center justify-between px-3 py-2 rounded-lg text-xs transition-colors cursor-pointer ${
                      activeFolder === pf.folderName
                        ? 'bg-zinc-800 text-white border border-zinc-700 font-medium'
                        : 'text-zinc-400 hover:bg-zinc-900'
                    }`}
                  >
                    <div className="flex items-center gap-2 truncate">
                      <Folder className="w-3.5 h-3.5 text-zinc-400 shrink-0" />
                      <span className="truncate">{pf.folderName}</span>
                    </div>
                    <span className="text-[9px] px-1 rounded uppercase font-mono bg-zinc-900 text-zinc-400 border border-zinc-800">
                      {pf.metadata.language}
                    </span>
                  </button>
                ))
              )}
            </div>

            <div className="pt-3 border-t border-zinc-800">
              <button
                onClick={() => setIsCreatingNew((prev) => !prev)}
                className="w-full py-2 rounded-lg bg-zinc-800 hover:bg-zinc-700 border border-zinc-700 text-white font-medium text-xs flex items-center justify-center gap-2 transition-colors cursor-pointer"
              >
                <FolderPlus className="w-4 h-4" />
                <span>+ Create Folder</span>
              </button>
            </div>
          </div>

          {/* Files List */}
          <div className="w-48 bg-zinc-900/40 border-r border-zinc-800 p-3 overflow-y-auto">
            {currentFolderObj ? (
              <div className="space-y-1 text-xs">
                <div className="px-2 py-1 text-[10px] font-mono text-zinc-500">
                  FILES IN /{currentFolderObj.folderName}
                </div>
                {currentFolderObj.files.map((file) => (
                  <button
                    key={file.name}
                    onClick={() => handleSelectFile(file)}
                    className={`w-full flex items-center gap-2 px-2.5 py-1.5 rounded text-xs transition-colors cursor-pointer ${
                      activeFile?.name === file.name
                        ? 'bg-zinc-800 text-white font-medium border border-zinc-700'
                        : 'text-zinc-400 hover:bg-zinc-900'
                    }`}
                  >
                    <Code2 className="w-3.5 h-3.5 text-zinc-400" />
                    <span className="truncate">{file.name}</span>
                  </button>
                ))}

                <div className="pt-4">
                  <button
                    onClick={() => {
                      if (confirm(`Delete folder './plugins/${currentFolderObj.folderName}'?`)) {
                        deletePlugin(currentFolderObj.metadata.id);
                        setActiveFolder(null);
                      }
                    }}
                    className="w-full py-1.5 px-2 rounded bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 text-zinc-400 hover:text-white text-[11px] flex items-center justify-center gap-1 cursor-pointer transition-colors"
                  >
                    <Trash2 className="w-3 h-3" />
                    <span>Delete Folder</span>
                  </button>
                </div>
              </div>
            ) : (
              <div className="text-zinc-500 text-xs italic text-center py-8">Select a folder</div>
            )}
          </div>

          {/* Code / Metadata Editor */}
          <div className="flex-1 p-4 bg-zinc-950 flex flex-col justify-between overflow-hidden">
            {isCreatingNew ? (
              <form onSubmit={handleCreateFolderSubmit} className="space-y-3.5 overflow-y-auto p-1 text-xs">
                <h3 className="text-xs font-bold text-white uppercase tracking-wider">
                  Create Plugin Folder
                </h3>

                <div className="space-y-1">
                  <label className="text-zinc-400">Folder Name (in ./plugins/):</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. MyPlugin"
                    value={newFolderName}
                    onChange={(e) => setNewFolderName(e.target.value)}
                    className="w-full bg-zinc-900 border border-zinc-800 rounded p-2 font-mono text-white focus:outline-none focus:border-zinc-600"
                  />
                </div>

                <div className="space-y-1">
                  <label className="text-zinc-400">Main File Type:</label>
                  <div className="flex gap-2">
                    <button
                      type="button"
                      onClick={() => setNewLanguage('python')}
                      className={`flex-1 py-1.5 rounded border text-xs font-semibold cursor-pointer ${
                        newLanguage === 'python' ? 'bg-zinc-800 text-white border-zinc-600' : 'bg-zinc-900 text-zinc-400 border-zinc-800'
                      }`}
                    >
                      Python (main.py)
                    </button>
                    <button
                      type="button"
                      onClick={() => setNewLanguage('csharp')}
                      className={`flex-1 py-1.5 rounded border text-xs font-semibold cursor-pointer ${
                        newLanguage === 'csharp' ? 'bg-zinc-800 text-white border-zinc-600' : 'bg-zinc-900 text-zinc-400 border-zinc-800'
                      }`}
                    >
                      C# (plugin.dll)
                    </button>
                  </div>
                </div>

                <div className="space-y-1">
                  <label className="text-zinc-400">Plugin Name:</label>
                  <input
                    type="text"
                    placeholder="Plugin Name"
                    value={newName}
                    onChange={(e) => setNewName(e.target.value)}
                    className="w-full bg-zinc-900 border border-zinc-800 rounded p-2 text-white focus:outline-none"
                  />
                </div>

                <div className="flex gap-2 pt-2">
                  <button
                    type="submit"
                    className="px-4 py-1.5 rounded bg-zinc-200 text-zinc-950 font-semibold cursor-pointer"
                  >
                    Create
                  </button>
                  <button
                    type="button"
                    onClick={() => setIsCreatingNew(false)}
                    className="px-4 py-1.5 rounded bg-zinc-800 text-zinc-300 cursor-pointer"
                  >
                    Cancel
                  </button>
                </div>
              </form>
            ) : activeFile ? (
              <div className="flex-1 flex flex-col overflow-hidden">
                <div className="flex items-center justify-between mb-2">
                  <div className="font-mono text-xs text-zinc-300">{activeFile.path}</div>
                  {saveStatus && (
                    <span className="text-xs text-white font-medium flex items-center gap-1">
                      <Check className="w-3.5 h-3.5 text-zinc-400" />
                      {saveStatus}
                    </span>
                  )}
                </div>

                <textarea
                  value={fileContent}
                  onChange={(e) => setFileContent(e.target.value)}
                  className="flex-1 w-full bg-black border border-zinc-800 rounded p-3 font-mono text-xs text-zinc-200 leading-relaxed resize-none focus:outline-none focus:border-zinc-700"
                  spellCheck={false}
                />

                <div className="mt-3 flex items-center justify-between">
                  <span className="text-[11px] text-zinc-500 font-mono">metadata.json editor</span>
                  <button
                    onClick={handleSaveFile}
                    className="px-4 py-1.5 rounded bg-zinc-200 hover:bg-white text-zinc-950 font-medium text-xs flex items-center gap-1.5 cursor-pointer"
                  >
                    <Save className="w-3.5 h-3.5" />
                    <span>Save {activeFile.name}</span>
                  </button>
                </div>
              </div>
            ) : (
              <div className="flex-1 flex items-center justify-center text-zinc-500 text-xs italic">
                Select a file to view
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
