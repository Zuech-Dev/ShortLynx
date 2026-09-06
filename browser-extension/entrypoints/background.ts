import { defineBackground } from 'wxt/utils/define-background';
import { createLink, listCampaigns, ApiRequestError } from '../lib/api';
import { getSettings } from '../lib/storage';
import type { BackgroundRequest, BackgroundResult } from '../lib/messages';

export default defineBackground(() => {
  chrome.runtime.onMessage.addListener((message: BackgroundRequest, _sender, sendResponse) => {
    handle(message).then(sendResponse);
    return true; // keep the message channel open for the async response
  });
});

async function handle(message: BackgroundRequest): Promise<BackgroundResult<unknown>> {
  const settings = await getSettings();
  if (!settings) {
    return { ok: false, error: 'Set up your ShortLynx server in the extension options first.', status: 0 };
  }

  try {
    switch (message.type) {
      case 'createLink':
        return { ok: true, data: await createLink(settings.serverUrl, settings.apiKey, message.payload) };
      case 'listCampaigns':
        // Also doubles as the options page's post-save connectivity check: any endpoint under
        // API-key auth 401s on a bad key regardless of scope, and a 403 here just means the key
        // lacks the optional campaigns:read scope, which callers treat as "connected, no campaigns."
        return { ok: true, data: await listCampaigns(settings.serverUrl, settings.apiKey) };
    }
  } catch (err) {
    if (err instanceof ApiRequestError) {
      return { ok: false, error: err.message, status: err.status };
    }
    return { ok: false, error: 'Something unexpected went wrong.', status: 0 };
  }
}
