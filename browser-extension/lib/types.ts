export interface ShortLynxSettings {
  /** The ShortLynx Core API base URL, e.g. "https://api.short.ly". No trailing slash. */
  serverUrl: string;
  apiKey: string;
  /**
   * Public redirect domain, if different from serverUrl (Core and the redirect app can be
   * different origins in a self-hosted deployment). Defaults to serverUrl when unset.
   */
  shortLinkDomain?: string;
  /** Mirrors the server's ShortCode__CustomRoutePrefix (default "c") -- only matters for custom codes. */
  customRoutePrefix?: string;
}

export type LinkMode = 'Anonymous' | 'UserAttributed';

export interface CreateLinkRequest {
  url: string;
  customCode?: string;
  mode?: LinkMode;
  campaignId?: string;
}

export interface LinkResponse {
  id: string;
  url: string;
  mode: LinkMode;
  shortCode: string;
  createdAt: string;
  expiresAt: string | null;
  campaignId: string | null;
  isCustom: boolean;
  customDomainId: string | null;
  folderId: string | null;
  nickname: string | null;
}

export interface Campaign {
  id: string;
  name: string;
  description: string | null;
  utmSource: string | null;
  utmMedium: string | null;
  utmCampaign: string | null;
  linkCount: number;
  createdAt: string;
}
