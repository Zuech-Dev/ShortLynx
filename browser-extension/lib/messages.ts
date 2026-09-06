import type { CreateLinkRequest } from './types';

// Requests the popup/options pages send to the background service worker, which owns every fetch()
// to the user's ShortLynx server -- extension-privileged contexts (background, popup, options) are
// exempt from CORS when host_permissions cover the target origin, unlike a content script running
// in a page's own context.
export type BackgroundRequest =
  | { type: 'createLink'; payload: CreateLinkRequest }
  | { type: 'listCampaigns' };

export type BackgroundResult<T> = { ok: true; data: T } | { ok: false; error: string; status: number };

// The caller names the response type it expects (e.g. sendToBackground<LinkResponse>(...)) --
// chrome.runtime.sendMessage itself is untyped, so this is a cast, not a checked mapping.
export async function sendToBackground<T>(request: BackgroundRequest): Promise<BackgroundResult<T>> {
  return chrome.runtime.sendMessage(request);
}
