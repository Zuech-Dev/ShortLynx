import { defineConfig } from 'wxt';

// Chromium-only for now (Chrome, Edge, Brave, Opera, Vivaldi all install Manifest V3 extensions
// the same way) -- DuckDuckGo's own desktop browser has no third-party extension support to target.
export default defineConfig({
  manifest: {
    name: 'ShortLynx',
    description: "Create ShortLynx short links from any page, without leaving the tab.",
    permissions: ['storage', 'activeTab'],
    // The server is self-hosted at a URL only the user knows, so we can't list it up front. We
    // request the specific origin at runtime (via chrome.permissions.request) once they save it in
    // the options page, instead of asking for a blanket host permission at install time.
    optional_host_permissions: ['*://*/*'],
  },
});
