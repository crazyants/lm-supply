import { useEffect, useState } from 'react';
import { useSystemStore } from '../stores/systemStore';
import { formatBytes, formatDate } from '../lib/utils';
import { Trash2, RefreshCw, Power, Download, Search, Loader2, ChevronDown, ChevronRight, Check, Play, Settings } from 'lucide-react';
import { api } from '../api/client';
import type { ModelCheckResult, ModelLoadRequest } from '../api/types';

export function Models() {
  const {
    cachedModels,
    loadedModels,
    downloadingModels,
    modelRegistry,
    isLoading,
    fetchModels,
    fetchLoadedModels,
    fetchModelRegistry,
    deleteModel,
    unloadModel,
    loadModel,
    startDownload,
    isDownloading: checkIsDownloading,
  } = useSystemStore();

  // Download form state
  const [repoId, setRepoId] = useState('');
  const [checkResult, setCheckResult] = useState<ModelCheckResult | null>(null);
  const [isChecking, setIsChecking] = useState(false);
  const [checkError, setCheckError] = useState<string | null>(null);
  const [selectedGgufFile, setSelectedGgufFile] = useState<string>('');
  const [expandedTypes, setExpandedTypes] = useState<Set<string>>(new Set());

  // Load model form state
  const [loadType, setLoadType] = useState('generator');
  const [loadModelId, setLoadModelId] = useState('default');
  const [loadProvider, setLoadProvider] = useState('');
  const [loadThreadCount, setLoadThreadCount] = useState('');
  const [loadMaxCtxLen, setLoadMaxCtxLen] = useState('');
  const [loadMaxConcurrent, setLoadMaxConcurrent] = useState('');
  const [loadMaxSeqLen, setLoadMaxSeqLen] = useState('');
  const [showLoadOptions, setShowLoadOptions] = useState(false);
  const [isLoadingModel, setIsLoadingModel] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Get current download state for the form input
  const currentDownload = downloadingModels.get(repoId);
  const isDownloading = currentDownload !== undefined && !currentDownload.error && currentDownload.progress?.status !== 'Completed';
  const downloadProgress = currentDownload?.progress ?? null;
  const downloadError = currentDownload?.error ?? null;

  useEffect(() => {
    fetchModels();
    fetchLoadedModels();
    fetchModelRegistry();
  }, [fetchModels, fetchLoadedModels, fetchModelRegistry]);

  const toggleExpand = (type: string) => {
    const newExpanded = new Set(expandedTypes);
    if (newExpanded.has(type)) {
      newExpanded.delete(type);
    } else {
      newExpanded.add(type);
    }
    setExpandedTypes(newExpanded);
  };

  const handleDownloadFromRegistry = (repoId: string) => {
    startDownload(repoId);
  };

  const handleRefresh = () => {
    fetchModels();
    fetchLoadedModels();
    fetchModelRegistry();
  };

  const handleDelete = async (repoId: string) => {
    if (confirm(`Delete model "${repoId}"? This cannot be undone.`)) {
      await deleteModel(repoId);
    }
  };

  const handleUnload = async (key: string) => {
    await unloadModel(key);
  };

  const handleLoadModel = async () => {
    setIsLoadingModel(true);
    setLoadError(null);
    try {
      const request: ModelLoadRequest = {
        type: loadType,
        modelId: loadModelId || 'default',
      };
      if (loadProvider) request.provider = loadProvider;
      if (loadThreadCount) request.threadCount = parseInt(loadThreadCount);
      if (loadMaxCtxLen) request.maxContextLength = parseInt(loadMaxCtxLen);
      if (loadMaxConcurrent) request.maxConcurrentRequests = parseInt(loadMaxConcurrent);
      if (loadMaxSeqLen) request.maxSequenceLength = parseInt(loadMaxSeqLen);
      const success = await loadModel(request);
      if (!success) setLoadError('Failed to load model');
    } catch (err) {
      setLoadError((err as Error).message);
    } finally {
      setIsLoadingModel(false);
    }
  };

  const handleCheck = async () => {
    if (!repoId.trim()) return;
    setIsChecking(true);
    setCheckResult(null);
    setCheckError(null);
    setSelectedGgufFile('');
    try {
      const result = await api.checkModel(repoId.trim());
      setCheckResult(result);
    } catch (err) {
      setCheckError((err as Error).message);
    } finally {
      setIsChecking(false);
    }
  };

  const handleDownload = async () => {
    if (!repoId.trim()) return;
    const fileName = selectedGgufFile || undefined;
    startDownload(repoId.trim(), fileName);
  };

  // Build a set of cached repoIds for quick lookup
  const cachedRepoIds = new Set(cachedModels.map(m => m.repoId));

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Model Management</h1>
        <button
          onClick={handleRefresh}
          className="px-3 py-2 bg-secondary rounded-lg flex items-center gap-2 hover:bg-secondary/80"
        >
          <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
          Refresh
        </button>
      </div>

      {/* ================================================================
          Cached Models — always visible at top
          ================================================================ */}
      <div className="bg-card border border-border rounded-lg p-4">
        <h2 className="text-lg font-semibold mb-4">
          Cached Models ({cachedModels.length})
        </h2>
        {cachedModels.length === 0 ? (
          <p className="text-muted-foreground">
            No models in cache. Use the download section below or the model registry to add models.
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-border text-left">
                  <th className="p-2 font-medium">Repository</th>
                  <th className="p-2 font-medium">Type</th>
                  <th className="p-2 font-medium">Size</th>
                  <th className="p-2 font-medium">Files</th>
                  <th className="p-2 font-medium">Status</th>
                  <th className="p-2 font-medium">Actions</th>
                </tr>
              </thead>
              <tbody>
                {cachedModels.map((model) => {
                  const downloading = checkIsDownloading(model.repoId);
                  const loaded = loadedModels.some(m => m.modelId === model.repoId);
                  return (
                    <tr key={model.repoId} className="border-b border-border/50">
                      <td className="p-2">
                        <p className="font-medium">{model.repoId}</p>
                        <p className="text-xs text-muted-foreground truncate max-w-xs" title={model.localPath}>
                          {model.localPath}
                        </p>
                      </td>
                      <td className="p-2">
                        <span className="px-2 py-1 bg-muted rounded text-xs">
                          {model.detectedType}
                        </span>
                      </td>
                      <td className="p-2 text-sm">{formatBytes(model.sizeBytes)}</td>
                      <td className="p-2 text-sm">{model.fileCount}</td>
                      <td className="p-2">
                        {downloading ? (
                          <span className="px-2 py-1 bg-blue-500/20 text-blue-500 rounded text-xs flex items-center gap-1 w-fit">
                            <Loader2 className="w-3 h-3 animate-spin" />
                            Downloading
                          </span>
                        ) : loaded ? (
                          <span className="px-2 py-1 bg-green-500/20 text-green-500 rounded text-xs">
                            Loaded
                          </span>
                        ) : (
                          <span className="px-2 py-1 bg-muted text-muted-foreground rounded text-xs">
                            Ready
                          </span>
                        )}
                      </td>
                      <td className="p-2">
                        <button
                          onClick={() => handleDelete(model.repoId)}
                          disabled={downloading || loaded}
                          className="p-2 hover:bg-destructive/20 rounded text-destructive disabled:opacity-30 disabled:cursor-not-allowed"
                          title={downloading ? "Cannot delete while downloading" : loaded ? "Unload model first" : "Delete model"}
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* ================================================================
          Downloading — shown when active
          ================================================================ */}
      {downloadingModels.size > 0 && (
        <div className="bg-card border border-border rounded-lg p-4">
          <h2 className="text-lg font-semibold mb-4 flex items-center gap-2">
            <Loader2 className="w-5 h-5 animate-spin text-primary" />
            Downloading ({downloadingModels.size})
          </h2>
          <div className="space-y-3">
            {Array.from(downloadingModels.values()).map((dl) => (
              <div key={dl.repoId} className="p-3 bg-muted rounded-lg">
                <div className="flex justify-between items-center mb-2">
                  <span className="font-medium">{dl.repoId}</span>
                  {dl.error ? (
                    <span className="text-destructive text-sm">Failed</span>
                  ) : dl.progress?.status === 'Completed' ? (
                    <span className="text-green-500 text-sm">✓ Completed</span>
                  ) : (
                    <span className="text-primary text-sm">
                      {dl.progress?.percentComplete?.toFixed(1) ?? 0}%
                    </span>
                  )}
                </div>
                {dl.error ? (
                  <p className="text-sm text-destructive">{dl.error}</p>
                ) : dl.progress && dl.progress.status !== 'Completed' ? (
                  <>
                    <div className="w-full bg-secondary rounded-full h-2 mb-1">
                      <div
                        className="bg-primary h-2 rounded-full transition-all"
                        style={{ width: `${dl.progress.percentComplete}%` }}
                      />
                    </div>
                    <p className="text-xs text-muted-foreground truncate">
                      {dl.progress.fileName} - {formatBytes(dl.progress.bytesDownloaded)} / {formatBytes(dl.progress.totalBytes)}
                    </p>
                  </>
                ) : null}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ================================================================
          Download Section
          ================================================================ */}
      <div className="bg-card border border-border rounded-lg p-4">
        <h2 className="text-lg font-semibold mb-4">Download Model from HuggingFace</h2>
        <div className="space-y-4">
          <div className="flex gap-4">
            <div className="flex-1">
              <label className="block text-sm font-medium mb-1">Repository ID</label>
              <input
                type="text"
                value={repoId}
                onChange={(e) => setRepoId(e.target.value)}
                placeholder="e.g., BAAI/bge-small-en-v1.5"
                className="w-full px-3 py-2 bg-muted border border-border rounded"
                disabled={isDownloading}
              />
            </div>
            <div className="flex items-end gap-2">
              <button
                onClick={handleCheck}
                disabled={isChecking || isDownloading || !repoId.trim()}
                className="px-4 py-2 bg-secondary rounded flex items-center gap-2 disabled:opacity-50"
              >
                {isChecking ? (
                  <Loader2 className="w-4 h-4 animate-spin" />
                ) : (
                  <Search className="w-4 h-4" />
                )}
                Check
              </button>
              <button
                onClick={handleDownload}
                disabled={isChecking || isDownloading || !repoId.trim()}
                className="px-4 py-2 bg-primary text-primary-foreground rounded flex items-center gap-2 disabled:opacity-50"
              >
                {isDownloading ? (
                  <Loader2 className="w-4 h-4 animate-spin" />
                ) : (
                  <Download className="w-4 h-4" />
                )}
                Download
              </button>
            </div>
          </div>

          {/* Check Result */}
          {checkResult && (
            <div
              className={`p-3 rounded ${
                checkResult.exists
                  ? 'bg-green-500/10 border border-green-500/30'
                  : 'bg-destructive/10 border border-destructive/30'
              }`}
            >
              {checkResult.exists ? (
                <div className="space-y-3">
                  <div className="space-y-1">
                    <p className="font-medium text-green-500">✓ Model found</p>
                    <p className="text-sm">
                      Type: <span className="font-medium">{checkResult.detectedType}</span> |{' '}
                      Files: {checkResult.fileCount} |{' '}
                      Size: {formatBytes(checkResult.totalSizeBytes)}
                    </p>
                  </div>
                  {/* GGUF file selection dropdown */}
                  {checkResult.ggufFiles && checkResult.ggufFiles.length > 0 && (
                    <div>
                      <label className="block text-sm font-medium mb-1">GGUF File</label>
                      <select
                        value={selectedGgufFile}
                        onChange={(e) => setSelectedGgufFile(e.target.value)}
                        className="w-full px-3 py-2 bg-muted border border-border rounded text-sm"
                        disabled={isDownloading}
                      >
                        <option value="">Auto (system spec based)</option>
                        {checkResult.ggufFiles.map((f) => (
                          <option key={f.fileName} value={f.fileName}>
                            {f.fileName} ({formatBytes(f.sizeBytes)})
                          </option>
                        ))}
                      </select>
                    </div>
                  )}
                </div>
              ) : (
                <p className="text-destructive">✗ Model not found: {checkResult.error}</p>
              )}
            </div>
          )}

          {/* Download Progress */}
          {downloadProgress && isDownloading && (
            <div className="p-3 bg-muted rounded space-y-2">
              <div className="flex justify-between text-sm">
                <span className="truncate max-w-md">{downloadProgress.fileName}</span>
                <span>{downloadProgress.percentComplete.toFixed(1)}%</span>
              </div>
              <div className="w-full bg-secondary rounded-full h-2">
                <div
                  className="bg-primary h-2 rounded-full transition-all"
                  style={{ width: `${downloadProgress.percentComplete}%` }}
                />
              </div>
              <p className="text-xs text-muted-foreground">
                {formatBytes(downloadProgress.bytesDownloaded)} / {formatBytes(downloadProgress.totalBytes)}
              </p>
            </div>
          )}

          {/* Download Complete */}
          {downloadProgress?.status === 'Completed' && !isDownloading && (
            <div className="p-3 bg-green-500/10 border border-green-500/30 rounded">
              <p className="text-green-500 font-medium">✓ Download completed successfully</p>
            </div>
          )}

          {/* Error */}
          {(checkError || downloadError) && (
            <div className="p-3 bg-destructive/10 border border-destructive/30 rounded">
              <p className="text-destructive">Error: {checkError || downloadError}</p>
            </div>
          )}
        </div>
      </div>

      {/* ================================================================
          Loaded Models — always visible
          ================================================================ */}
      <div className="bg-card border border-border rounded-lg p-4">
        <h2 className="text-lg font-semibold mb-4">
          Loaded Models ({loadedModels.length})
        </h2>
        {loadedModels.length === 0 ? (
          <p className="text-muted-foreground mb-4">
            No models currently loaded in memory. Models are loaded on-demand when you use them, or you can pre-load one below.
          </p>
        ) : (
          <div className="space-y-2 mb-4">
            {loadedModels.map((model) => (
              <div
                key={`${model.modelType}:${model.modelId}`}
                className="flex items-center justify-between p-3 bg-muted rounded-lg"
              >
                <div>
                  <p className="font-medium">{model.modelId}</p>
                  <p className="text-sm text-muted-foreground">
                    Type: {model.modelType} | Loaded: {formatDate(model.loadedAt)} | Last used:{' '}
                    {formatDate(model.lastUsedAt)}
                  </p>
                </div>
                <button
                  onClick={() => handleUnload(`${model.modelType}:${model.modelId}`)}
                  className="p-2 hover:bg-destructive/20 rounded text-destructive"
                  title="Unload model from memory"
                >
                  <Power className="w-4 h-4" />
                </button>
              </div>
            ))}
          </div>
        )}

        {/* Load Model Form */}
        <div className="border-t border-border pt-4">
          <h3 className="font-medium mb-3 flex items-center gap-2">
            <Play className="w-4 h-4" />
            Pre-load Model
          </h3>
          <div className="space-y-3">
            <div className="flex gap-3 flex-wrap">
              <div>
                <label className="block text-xs font-medium mb-1">Type</label>
                <select
                  value={loadType}
                  onChange={(e) => setLoadType(e.target.value)}
                  className="px-3 py-1.5 bg-muted border border-border rounded text-sm"
                  disabled={isLoadingModel}
                >
                  <option value="generator">Generator</option>
                  <option value="embedder">Embedder</option>
                  <option value="reranker">Reranker</option>
                  <option value="transcriber">Transcriber</option>
                  <option value="synthesizer">Synthesizer</option>
                  <option value="translator">Translator</option>
                  <option value="captioner">Captioner</option>
                  <option value="ocr">OCR</option>
                  <option value="detector">Detector</option>
                  <option value="segmenter">Segmenter</option>
                  <option value="imagegen">Image Generator</option>
                </select>
              </div>
              <div className="flex-1 min-w-[200px]">
                <label className="block text-xs font-medium mb-1">Model ID</label>
                <input
                  type="text"
                  value={loadModelId}
                  onChange={(e) => setLoadModelId(e.target.value)}
                  placeholder="default"
                  className="w-full px-3 py-1.5 bg-muted border border-border rounded text-sm"
                  disabled={isLoadingModel}
                />
              </div>
              <div className="flex items-end gap-2">
                <button
                  onClick={() => setShowLoadOptions(!showLoadOptions)}
                  className={`p-1.5 rounded border border-border ${showLoadOptions ? 'bg-primary/20 text-primary' : 'bg-muted'}`}
                  title="Advanced options"
                >
                  <Settings className="w-4 h-4" />
                </button>
                <button
                  onClick={handleLoadModel}
                  disabled={isLoadingModel || !loadModelId.trim()}
                  className="px-4 py-1.5 bg-primary text-primary-foreground rounded text-sm flex items-center gap-2 disabled:opacity-50"
                >
                  {isLoadingModel ? (
                    <Loader2 className="w-4 h-4 animate-spin" />
                  ) : (
                    <Play className="w-4 h-4" />
                  )}
                  Load
                </button>
              </div>
            </div>

            {/* Advanced Options (collapsible) */}
            {showLoadOptions && (
              <div className="p-3 bg-muted/50 border border-border rounded space-y-3">
                <p className="text-xs text-muted-foreground">Advanced loading options (leave empty for defaults)</p>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                  <div>
                    <label className="block text-xs font-medium mb-1">Provider</label>
                    <select
                      value={loadProvider}
                      onChange={(e) => setLoadProvider(e.target.value)}
                      className="w-full px-2 py-1.5 bg-muted border border-border rounded text-sm"
                      disabled={isLoadingModel}
                    >
                      <option value="">Auto</option>
                      <option value="Cuda">CUDA</option>
                      <option value="DirectML">DirectML</option>
                      <option value="CoreML">CoreML</option>
                      <option value="Cpu">CPU</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-xs font-medium mb-1">Threads</label>
                    <input
                      type="number"
                      value={loadThreadCount}
                      onChange={(e) => setLoadThreadCount(e.target.value)}
                      placeholder="auto"
                      min="1"
                      className="w-full px-2 py-1.5 bg-muted border border-border rounded text-sm"
                      disabled={isLoadingModel}
                    />
                  </div>
                  {loadType === 'generator' && (
                    <>
                      <div>
                        <label className="block text-xs font-medium mb-1">Max Context</label>
                        <input
                          type="number"
                          value={loadMaxCtxLen}
                          onChange={(e) => setLoadMaxCtxLen(e.target.value)}
                          placeholder="auto"
                          min="256"
                          className="w-full px-2 py-1.5 bg-muted border border-border rounded text-sm"
                          disabled={isLoadingModel}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-medium mb-1">Concurrency</label>
                        <input
                          type="number"
                          value={loadMaxConcurrent}
                          onChange={(e) => setLoadMaxConcurrent(e.target.value)}
                          placeholder="1"
                          min="1"
                          className="w-full px-2 py-1.5 bg-muted border border-border rounded text-sm"
                          disabled={isLoadingModel}
                        />
                      </div>
                    </>
                  )}
                  {loadType === 'embedder' && (
                    <div>
                      <label className="block text-xs font-medium mb-1">Max Seq Length</label>
                      <input
                        type="number"
                        value={loadMaxSeqLen}
                        onChange={(e) => setLoadMaxSeqLen(e.target.value)}
                        placeholder="512"
                        min="32"
                        className="w-full px-2 py-1.5 bg-muted border border-border rounded text-sm"
                        disabled={isLoadingModel}
                      />
                    </div>
                  )}
                </div>
              </div>
            )}

            {loadError && (
              <div className="p-2 bg-destructive/10 text-destructive text-sm rounded">
                {loadError}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* ================================================================
          Model Registry — Browse by Type
          ================================================================ */}
      <div className="bg-card border border-border rounded-lg p-4">
        <h2 className="text-lg font-semibold mb-4">
          Model Registry ({modelRegistry.length} types)
        </h2>
        <p className="text-sm text-muted-foreground mb-4">
          Browse available model aliases. Click a type to expand. Cached models are marked with a checkmark.
        </p>
        <div className="space-y-2">
          {modelRegistry.map((typeInfo) => (
            <div key={typeInfo.type} className="border border-border rounded-lg overflow-hidden">
              <button
                onClick={() => toggleExpand(typeInfo.type)}
                className="w-full flex items-center justify-between p-3 bg-muted hover:bg-muted/80 transition-colors"
              >
                <div className="flex items-center gap-3">
                  {expandedTypes.has(typeInfo.type) ? (
                    <ChevronDown className="w-4 h-4" />
                  ) : (
                    <ChevronRight className="w-4 h-4" />
                  )}
                  <div className="text-left">
                    <span className="font-medium">{typeInfo.displayName}</span>
                    <span className="text-sm text-muted-foreground ml-2">({typeInfo.type})</span>
                  </div>
                </div>
                <span className="text-sm text-muted-foreground">
                  {typeInfo.models.length} models
                </span>
              </button>

              {expandedTypes.has(typeInfo.type) && (
                <div className="p-3 border-t border-border">
                  <p className="text-sm text-muted-foreground mb-3">{typeInfo.description}</p>
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead>
                        <tr className="border-b border-border/50 text-left">
                          <th className="pb-2 font-medium">Alias</th>
                          <th className="pb-2 font-medium">Repository ID</th>
                          <th className="pb-2 font-medium">Description</th>
                          <th className="pb-2 font-medium text-center">Status</th>
                          <th className="pb-2 font-medium text-center">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {typeInfo.models.map((model) => {
                          const downloading = checkIsDownloading(model.repoId);
                          const downloadState = downloadingModels.get(model.repoId);
                          const isCached = model.isCached || cachedRepoIds.has(model.repoId);
                          return (
                            <tr key={model.aliasName} className="border-b border-border/30">
                              <td className="py-2">
                                <span className="px-2 py-1 bg-primary/10 text-primary rounded text-xs font-medium">
                                  {model.aliasName}
                                </span>
                              </td>
                              <td className="py-2">
                                <code className="text-xs bg-muted px-2 py-1 rounded">
                                  {model.repoId}
                                </code>
                              </td>
                              <td className="py-2 text-muted-foreground">
                                {model.description}
                              </td>
                              <td className="py-2 text-center">
                                {isCached ? (
                                  <span className="inline-flex items-center gap-1 px-2 py-1 bg-green-500/20 text-green-500 rounded text-xs">
                                    <Check className="w-3 h-3" />
                                    Cached
                                  </span>
                                ) : downloading ? (
                                  <span className="inline-flex items-center gap-1 px-2 py-1 bg-blue-500/20 text-blue-500 rounded text-xs">
                                    <Loader2 className="w-3 h-3 animate-spin" />
                                    {downloadState?.progress?.percentComplete?.toFixed(0) ?? 0}%
                                  </span>
                                ) : (
                                  <span className="px-2 py-1 bg-muted text-muted-foreground rounded text-xs">
                                    Not downloaded
                                  </span>
                                )}
                              </td>
                              <td className="py-2 text-center">
                                {!isCached && !downloading && (
                                  <button
                                    onClick={() => handleDownloadFromRegistry(model.repoId)}
                                    className="p-1.5 hover:bg-primary/20 rounded text-primary"
                                    title={`Download ${model.repoId}`}
                                  >
                                    <Download className="w-4 h-4" />
                                  </button>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
