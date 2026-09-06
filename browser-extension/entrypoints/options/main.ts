import { getSettings, setSettings } from '../../lib/storage';
import { sendToBackground } from '../../lib/messages';
import type { Campaign, ShortLynxSettings } from '../../lib/types';

const form = document.getElementById('settings-form') as HTMLFormElement;
const serverUrlInput = document.getElementById('server-url') as HTMLInputElement;
const apiKeyInput = document.getElementById('api-key') as HTMLInputElement;
const shortLinkDomainInput = document.getElementById('short-link-domain') as HTMLInputElement;
const customRoutePrefixInput = document.getElementById('custom-route-prefix') as HTMLInputElement;
const saveBtn = document.getElementById('save-btn') as HTMLButtonElement;
const statusEl = document.getElementById('status') as HTMLParagraphElement;

function showStatus(message: string, kind: 'success' | 'error' | 'info') {
  statusEl.textContent = message;
  statusEl.className = `status ${kind}`;
  statusEl.hidden = false;
}

// Normalizes to an origin the extension can request host permission for ("https://host:port/*"),
// or null if the value isn't a valid absolute http(s) URL.
function toOrigin(rawUrl: string): string | null {
  try {
    const parsed = new URL(rawUrl);
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null;
    return `${parsed.origin}/*`;
  } catch {
    return null;
  }
}

async function loadExisting() {
  const settings = await getSettings();
  if (!settings) return;
  serverUrlInput.value = settings.serverUrl;
  apiKeyInput.value = settings.apiKey;
  shortLinkDomainInput.value = settings.shortLinkDomain ?? '';
  customRoutePrefixInput.value = settings.customRoutePrefix ?? '';
}

form.addEventListener('submit', async (e) => {
  e.preventDefault();

  const serverUrl = serverUrlInput.value.trim().replace(/\/+$/, '');
  const origin = toOrigin(serverUrl);
  if (!origin) {
    showStatus('Enter a valid server URL, starting with http:// or https://.', 'error');
    return;
  }

  const apiKey = apiKeyInput.value.trim();
  if (!apiKey) {
    showStatus('Enter an API key.', 'error');
    return;
  }

  saveBtn.disabled = true;
  saveBtn.textContent = 'Saving…';

  try {
    const granted = await chrome.permissions.request({ origins: [origin] });
    if (!granted) {
      showStatus('Permission to reach that server was declined, so nothing was saved.', 'error');
      return;
    }

    const settings: ShortLynxSettings = {
      serverUrl,
      apiKey,
      shortLinkDomain: shortLinkDomainInput.value.trim() || undefined,
      customRoutePrefix: customRoutePrefixInput.value.trim() || undefined,
    };
    await setSettings(settings);

    const result = await sendToBackground<Campaign[]>({ type: 'listCampaigns' });
    if (result.ok) {
      showStatus(
        result.data.length > 0
          ? `Saved and connected -- found ${result.data.length} campaign(s).`
          : 'Saved and connected.',
        'success',
      );
    } else if (result.status === 403) {
      showStatus(
        "Saved and connected. This key doesn't have the optional campaigns:read scope, so the " +
          'campaign picker will be unavailable -- that\'s fine if you don\'t need it.',
        'success',
      );
    } else {
      showStatus(`Saved, but couldn't verify the connection: ${result.error}`, 'error');
    }
  } finally {
    saveBtn.disabled = false;
    saveBtn.textContent = 'Save';
  }
});

loadExisting();
