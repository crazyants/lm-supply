import { create } from 'zustand';
import type { SystemStatus, CachedModelInfo, LoadedModelInfo, DownloadProgress, ModelTypeInfo, ModelLoadRequest, UpdateCheckResult, UpdateProgress } from '../api/types';
import { api } from '../api/client';

export interface DownloadingModel {
  repoId: string;
  progress: DownloadProgress | null;
  error: string | null;
}

export type UpdateStatus = 'idle' | 'checking' | 'available' | 'updating' | 'restarting' | 'error';

interface SystemState {
  status: SystemStatus | null;
  cachedModels: CachedModelInfo[];
  loadedModels: LoadedModelInfo[];
  downloadingModels: Map<string, DownloadingModel>;
  modelRegistry: ModelTypeInfo[];
  isLoading: boolean;
  error: string | null;

  // Update state
  currentVersion: string | null;
  updateCheck: UpdateCheckResult | null;
  updateStatus: UpdateStatus;
  updateProgress: UpdateProgress | null;
  updateError: string | null;

  fetchStatus: () => Promise<void>;
  fetchModels: () => Promise<void>;
  fetchLoadedModels: () => Promise<void>;
  fetchModelRegistry: () => Promise<void>;
  deleteModel: (repoId: string) => Promise<boolean>;
  unloadModel: (key: string) => Promise<boolean>;
  startDownload: (repoId: string, fileName?: string) => Promise<void>;
  isDownloading: (repoId: string) => boolean;
  getDownloadProgress: (repoId: string) => DownloadingModel | undefined;
  loadModel: (request: ModelLoadRequest) => Promise<boolean>;
  checkForUpdate: () => Promise<void>;
  applyUpdate: () => Promise<void>;
}

export const useSystemStore = create<SystemState>((set, get) => ({
  status: null,
  cachedModels: [],
  loadedModels: [],
  downloadingModels: new Map(),
  modelRegistry: [],
  isLoading: false,
  error: null,

  // Update state
  currentVersion: null,
  updateCheck: null,
  updateStatus: 'idle' as UpdateStatus,
  updateProgress: null,
  updateError: null,

  fetchStatus: async () => {
    try {
      const response = await api.getSystemStatus();
      set({ status: response.status, error: null });
    } catch (e) {
      set({ error: (e as Error).message });
    }
  },

  fetchModels: async () => {
    set({ isLoading: true });
    try {
      const response = await api.getCachedModels();
      set({ cachedModels: response.models, isLoading: false, error: null });
    } catch (e) {
      set({ isLoading: false, error: (e as Error).message });
    }
  },

  fetchLoadedModels: async () => {
    try {
      const loadedModels = await api.getLoadedModels();
      set({ loadedModels, error: null });
    } catch (e) {
      set({ error: (e as Error).message });
    }
  },

  fetchModelRegistry: async () => {
    try {
      const response = await api.getModelRegistry();
      set({ modelRegistry: response.modelTypes, error: null });
    } catch (e) {
      set({ error: (e as Error).message });
    }
  },

  deleteModel: async (repoId: string) => {
    try {
      const response = await api.deleteModel(repoId);
      if (response.ok) {
        await get().fetchModels();
        return true;
      }
      return false;
    } catch {
      return false;
    }
  },

  unloadModel: async (key: string) => {
    try {
      const response = await api.unloadModel(key);
      if (response.ok) {
        await get().fetchLoadedModels();
        return true;
      }
      return false;
    } catch {
      return false;
    }
  },

  loadModel: async (request: ModelLoadRequest) => {
    try {
      await api.loadModel(request);
      await get().fetchLoadedModels();
      return true;
    } catch {
      return false;
    }
  },

  startDownload: async (repoId: string, fileName?: string) => {
    const downloading = new Map(get().downloadingModels);
    downloading.set(repoId, { repoId, progress: null, error: null });
    set({ downloadingModels: downloading });

    try {
      for await (const progress of api.downloadModel(repoId, fileName)) {
        const updated = new Map(get().downloadingModels);
        const current = updated.get(repoId);
        if (current) {
          current.progress = progress;
          if (progress.status === 'Failed') {
            current.error = progress.error || 'Download failed';
          }
          set({ downloadingModels: updated });
        }

        if (progress.status === 'Completed' || progress.status === 'Failed') {
          // Remove from downloading after a short delay
          setTimeout(() => {
            const final = new Map(get().downloadingModels);
            final.delete(repoId);
            set({ downloadingModels: final });
          }, progress.status === 'Completed' ? 2000 : 5000);

          if (progress.status === 'Completed') {
            await get().fetchModels();
          }
          break;
        }
      }
    } catch (e) {
      const updated = new Map(get().downloadingModels);
      const current = updated.get(repoId);
      if (current) {
        current.error = (e as Error).message;
        set({ downloadingModels: updated });
      }
      // Remove after delay
      setTimeout(() => {
        const final = new Map(get().downloadingModels);
        final.delete(repoId);
        set({ downloadingModels: final });
      }, 5000);
    }
  },

  isDownloading: (repoId: string) => {
    return get().downloadingModels.has(repoId);
  },

  getDownloadProgress: (repoId: string) => {
    return get().downloadingModels.get(repoId);
  },

  checkForUpdate: async () => {
    set({ updateStatus: 'checking' });
    try {
      const [versionInfo, checkResult] = await Promise.all([
        api.getVersion(),
        api.checkUpdate(),
      ]);
      set({
        currentVersion: versionInfo.version,
        updateCheck: checkResult,
        updateStatus: checkResult.updateAvailable ? 'available' : 'idle',
        updateError: checkResult.error || null,
      });
    } catch (e) {
      set({
        updateStatus: 'error',
        updateError: (e as Error).message,
      });
    }
  },

  applyUpdate: async () => {
    set({ updateStatus: 'updating', updateProgress: null, updateError: null });

    try {
      for await (const progress of api.applyUpdate()) {
        set({ updateProgress: progress });

        if (progress.status === 'Error') {
          set({ updateStatus: 'error', updateError: progress.error || 'Update failed' });
          return;
        }

        if (progress.status === 'Restarting') {
          set({ updateStatus: 'restarting' });

          // Poll until server comes back, then reload
          const poll = async () => {
            for (let i = 0; i < 30; i++) {
              await new Promise(r => setTimeout(r, 2000));
              try {
                await api.getVersion();
                window.location.reload();
                return;
              } catch {
                // Server not ready yet
              }
            }
            set({ updateStatus: 'error', updateError: 'Server did not restart in time' });
          };
          poll();
          return;
        }
      }
    } catch {
      // Connection dropped during restart - expected
      if (get().updateStatus === 'restarting') return;

      set({ updateStatus: 'restarting' });
      // Poll until server comes back
      const poll = async () => {
        for (let i = 0; i < 30; i++) {
          await new Promise(r => setTimeout(r, 2000));
          try {
            await api.getVersion();
            window.location.reload();
            return;
          } catch {
            // Server not ready yet
          }
        }
        set({ updateStatus: 'error', updateError: 'Server did not restart in time' });
      };
      poll();
    }
  },
}));
