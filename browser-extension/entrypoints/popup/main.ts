import { getSettings } from '../../lib/storage';
import { sendToBackground } from '../../lib/messages';
import { buildShortUrl } from '../../lib/shortUrl';
import type { Campaign, CreateLinkRequest, LinkResponse, ShortLynxSettings } from '../../lib/types';

const setupPrompt = document.getElementById('setup-prompt') as HTMLDivElement;
const openOptionsBtn = document.getElementById('open-options-btn') as HTMLButtonElement;
const settingsBtn = document.getElementById('settings-btn') as HTMLButtonElement;

const form = document.getElementById('create-form') as HTMLFormElement;
const urlInput = document.getElementById('url') as HTMLInputElement;
const customCodeField = document.getElementById('custom-code-field') as HTMLLabelElement;
const customCodeInput = document.getElementById('custom-code') as HTMLInputElement;
const campaignField = document.getElementById('campaign-field') as HTMLLabelElement;
const campaignSelect = document.getElementById('campaign') as HTMLSelectElement;
const submitBtn = document.getElementById('submit-btn') as HTMLButtonElement;
const formError = document.getElementById('form-error') as HTMLParagraphElement;

const resultBox = document.getElementById('result') as HTMLDivElement;
const resultMessage = document.getElementById('result-message') as HTMLParagraphElement;
const resultUrlInput = document.getElementById('result-url') as HTMLInputElement;
const copyBtn = document.getElementById('copy-btn') as HTMLButtonElement;
const createAnotherBtn = document.getElementById('create-another-btn') as HTMLButtonElement;

let settings: ShortLynxSettings | null = null;

function showFormError(message: string) {
  formError.textContent = message;
  formError.hidden = false;
}

function clearFormError() {
  formError.hidden = true;
  formError.textContent = '';
}

async function prefillDestination() {
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab?.url && /^https?:\/\//.test(tab.url)) {
      urlInput.value = tab.url;
    }
  } catch {
    // No active tab info available (e.g. an internal browser page) -- leave it blank.
  }
}

async function loadCampaigns() {
  const response = await sendToBackground<Campaign[]>({ type: 'listCampaigns' });
  if (!response.ok || response.data.length === 0) {
    // Missing the optional campaigns:read scope, no campaigns yet, or a transient error --
    // campaign assignment is optional, so degrade quietly rather than blocking link creation.
    campaignField.hidden = true;
    return;
  }
  for (const campaign of response.data) {
    const option = document.createElement('option');
    option.value = campaign.id;
    option.textContent = campaign.name;
    campaignSelect.appendChild(option);
  }
  campaignField.hidden = false;
}

function currentMode(): 'Anonymous' | 'UserAttributed' {
  return (new FormData(form).get('mode') as string) === 'UserAttributed' ? 'UserAttributed' : 'Anonymous';
}

function updateModeUi() {
  const isUserAttributed = currentMode() === 'UserAttributed';
  customCodeField.hidden = isUserAttributed;
  if (isUserAttributed) customCodeInput.value = '';
}

function resetForm() {
  form.reset();
  updateModeUi();
  clearFormError();
  resultBox.hidden = true;
  form.hidden = false;
  prefillDestination();
}

async function init() {
  settings = await getSettings();
  if (!settings) {
    setupPrompt.hidden = false;
    return;
  }

  form.hidden = false;
  await prefillDestination();
  await loadCampaigns();
  form.addEventListener('change', updateModeUi);
  updateModeUi();
}

openOptionsBtn.addEventListener('click', () => chrome.runtime.openOptionsPage());
settingsBtn.addEventListener('click', () => chrome.runtime.openOptionsPage());
createAnotherBtn.addEventListener('click', resetForm);

copyBtn.addEventListener('click', async () => {
  await navigator.clipboard.writeText(resultUrlInput.value);
  const original = copyBtn.textContent;
  copyBtn.textContent = 'Copied!';
  setTimeout(() => (copyBtn.textContent = original), 1500);
});

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearFormError();
  submitBtn.disabled = true;
  submitBtn.textContent = 'Creating…';

  try {
    const mode = currentMode();
    const payload: CreateLinkRequest = { url: urlInput.value.trim(), mode };
    if (mode === 'Anonymous' && customCodeInput.value.trim()) {
      payload.customCode = customCodeInput.value.trim();
    }
    if (campaignSelect.value) {
      payload.campaignId = campaignSelect.value;
    }

    const response = await sendToBackground<LinkResponse>({ type: 'createLink', payload });
    if (!response.ok) {
      showFormError(response.error);
      return;
    }

    const shortUrl = settings ? buildShortUrl(settings, response.data) : null;
    if (shortUrl) {
      resultMessage.textContent = 'Your short link is ready:';
      resultUrlInput.value = shortUrl;
      resultUrlInput.parentElement!.hidden = false;
    } else {
      // User-attributed links have no shared short code yet -- recipient codes are provisioned
      // separately from the dashboard, so there's nothing to show as "the" link.
      resultMessage.textContent = 'Link created. Provision recipient codes for it from your ShortLynx dashboard.';
      resultUrlInput.parentElement!.hidden = true;
    }

    form.hidden = true;
    resultBox.hidden = false;
  } finally {
    submitBtn.disabled = false;
    submitBtn.textContent = 'Create link';
  }
});

init();
