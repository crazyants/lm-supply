import { useState, useEffect } from 'react';
import { Loader2, ChevronDown, AlertCircle } from 'lucide-react';
import { api } from '../api/client';

interface ModelItem {
  repoId: string;
  alias: string;
  isCached: boolean;
  sizeMB?: number;
}

interface ModelSelectorProps {
  modelType: 'chat' | 'embed' | 'rerank' | 'transcribe' | 'synthesize';
  value: string;
  onChange: (modelId: string) => void;
  disabled?: boolean;
}

// Map UI model types to backend registry type names
const MODEL_TYPE_BACKEND: Record<string, string> = {
  chat: 'generator',
  embed: 'embedder',
  rerank: 'reranker',
  transcribe: 'transcriber',
  synthesize: 'synthesizer',
};

const MODEL_TYPE_LABELS: Record<string, string> = {
  chat: 'Generator',
  embed: 'Embedder',
  rerank: 'Reranker',
  transcribe: 'Transcriber',
  synthesize: 'Synthesizer',
};

export function ModelSelector({ modelType, value, onChange, disabled }: ModelSelectorProps) {
  const [models, setModels] = useState<ModelItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fetchModels = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const backendType = MODEL_TYPE_BACKEND[modelType];

        // Fetch registry models (includes isCached status) and cached models in parallel
        const [registryData, cachedData] = await Promise.all([
          api.getModelsByType(backendType).catch(() => null),
          fetch(`/api/cache/models/type/${backendType}`)
            .then(r => r.ok ? r.json() as Promise<Array<{ repoId: string; sizeMB: number }>> : [])
            .catch(() => [] as Array<{ repoId: string; sizeMB: number }>),
        ]);

        const cachedMap = new Map(cachedData.map(m => [m.repoId, m.sizeMB]));

        if (registryData?.models?.length) {
          // Registry models: show all with cache status
          const items: ModelItem[] = registryData.models.map(m => ({
            repoId: m.repoId,
            alias: m.alias,
            isCached: m.isCached || cachedMap.has(m.repoId),
            sizeMB: cachedMap.get(m.repoId),
          }));

          // Also include cached models not in registry (user-downloaded custom models)
          for (const [repoId, sizeMB] of cachedMap) {
            if (!items.some(i => i.repoId === repoId)) {
              items.push({ repoId, alias: repoId, isCached: true, sizeMB });
            }
          }

          setModels(items);
        } else {
          // Fallback: cached models only (registry unavailable)
          setModels(cachedData.map(m => ({
            repoId: m.repoId,
            alias: m.repoId,
            isCached: true,
            sizeMB: m.sizeMB,
          })));
        }
      } catch (err) {
        setError((err as Error).message);
      } finally {
        setIsLoading(false);
      }
    };
    fetchModels();
  }, [modelType]);

  // Auto-select first model (prefer cached, fallback to first available)
  useEffect(() => {
    if (models.length > 0 && !value) {
      const cached = models.find(m => m.isCached);
      onChange((cached ?? models[0]).repoId);
    }
  }, [models, value, onChange]);

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        <Loader2 className="w-4 h-4 animate-spin" />
        Loading models...
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center gap-2 text-sm text-destructive">
        <AlertCircle className="w-4 h-4" />
        {error}
      </div>
    );
  }

  if (models.length === 0) {
    return (
      <div className="flex items-center gap-2 text-sm text-amber-600">
        <AlertCircle className="w-4 h-4" />
        No {MODEL_TYPE_LABELS[modelType]} models registered.
      </div>
    );
  }

  const selectedModel = models.find(m => m.repoId === value);

  return (
    <div className="space-y-1">
      <div className="relative">
        <select
          value={value}
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled}
          className="appearance-none px-3 py-1.5 pr-8 bg-muted border border-border rounded text-sm cursor-pointer min-w-[280px] disabled:opacity-50"
        >
          {models.map((model) => (
            <option key={model.repoId} value={model.repoId}>
              {model.alias !== model.repoId ? `${model.alias} — ` : ''}
              {model.repoId}
              {model.isCached && model.sizeMB != null ? ` (${model.sizeMB.toFixed(0)} MB)` : ''}
              {!model.isCached ? ' (on-demand)' : ''}
            </option>
          ))}
        </select>
        <ChevronDown className="absolute right-2 top-1/2 -translate-y-1/2 w-4 h-4 pointer-events-none text-muted-foreground" />
      </div>
      {selectedModel && !selectedModel.isCached && (
        <p className="text-xs text-muted-foreground">
          Model will be downloaded automatically on first use.
        </p>
      )}
    </div>
  );
}
