import { create } from 'zustand';
import type { ApiKeyResponse, ApiKeyCreatedResponse, ApiKeyStats } from '../api/types';
import { api } from '../api/client';

interface ApiKeyState {
  keys: ApiKeyResponse[];
  stats: ApiKeyStats | null;
  statsDays: number;
  isLoading: boolean;
  error: string | null;

  fetchKeys: () => Promise<void>;
  createKey: (name: string) => Promise<ApiKeyCreatedResponse>;
  deleteKey: (id: string) => Promise<void>;
  fetchStats: (days?: number) => Promise<void>;
}

export const useApiKeyStore = create<ApiKeyState>((set, get) => ({
  keys: [],
  stats: null,
  statsDays: 7,
  isLoading: false,
  error: null,

  fetchKeys: async () => {
    set({ isLoading: true, error: null });
    try {
      const keys = await api.listApiKeys();
      set({ keys, isLoading: false });
    } catch (e) {
      set({ error: String(e), isLoading: false });
    }
  },

  createKey: async (name: string) => {
    const created = await api.createApiKey(name);
    await get().fetchKeys(); // refresh list
    return created;
  },

  deleteKey: async (id: string) => {
    await api.deleteApiKey(id);
    set(s => ({ keys: s.keys.filter(k => k.id !== id) }));
  },

  fetchStats: async (days = 7) => {
    set({ statsDays: days });
    try {
      const stats = await api.getApiKeyStats(days);
      set({ stats });
    } catch {
      // stats failure is non-critical
    }
  },
}));
