import type { ShortLynxSettings } from './types';

const STORAGE_KEY = 'shortlynx-settings';

export async function getSettings(): Promise<ShortLynxSettings | null> {
  const result = await chrome.storage.local.get(STORAGE_KEY);
  return (result[STORAGE_KEY] as ShortLynxSettings | undefined) ?? null;
}

export async function setSettings(settings: ShortLynxSettings): Promise<void> {
  await chrome.storage.local.set({ [STORAGE_KEY]: settings });
}
