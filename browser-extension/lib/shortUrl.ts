import type { LinkResponse, ShortLynxSettings } from './types';

/**
 * Builds the public short URL for a just-created link, client-side -- mirrors
 * ShortLynx.Admin's DashboardOptions.BuildShortUrl, which exists for the exact same reason: a
 * brand-new link can't have a custom domain pinned yet (that's a follow-up action in the
 * dashboard), so there's no need for a DB round trip to resolve one. Custom codes resolve only
 * under the custom route prefix (default "c"), never at the root -- see LinkResponse.IsCustom's
 * doc comment on the API side.
 *
 * Returns null for UserAttributed links: they have no shared ShortCode (each recipient gets
 * their own, provisioned separately), so there's no single short URL to show yet.
 */
export function buildShortUrl(settings: ShortLynxSettings, link: LinkResponse): string | null {
  if (link.mode === 'UserAttributed' || !link.shortCode) return null;

  const domain = (settings.shortLinkDomain?.trim() || settings.serverUrl).replace(/\/+$/, '');
  const prefix = (settings.customRoutePrefix?.trim() || 'c').replace(/^\/+|\/+$/g, '');
  return link.isCustom ? `${domain}/${prefix}/${link.shortCode}` : `${domain}/${link.shortCode}`;
}
