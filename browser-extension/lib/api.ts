import type { Campaign, CreateLinkRequest, LinkResponse } from './types';

export class ApiRequestError extends Error {
  status: number;

  constructor(message: string, status: number) {
    super(message);
    this.status = status;
  }
}

async function request<T>(serverUrl: string, apiKey: string, path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${serverUrl}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${apiKey}`,
        ...init?.headers,
      },
    });
  } catch {
    throw new ApiRequestError(`Couldn't reach ${serverUrl}. Check the server URL and that it's running.`, 0);
  }

  if (!response.ok) {
    if (response.status === 401) {
      throw new ApiRequestError("That API key isn't valid for this server.", 401);
    }

    let message = `Request failed (${response.status}).`;
    try {
      const body = await response.json();
      if (typeof body?.error === 'string') message = body.error;
    } catch {
      // Body wasn't JSON -- keep the generic message.
    }
    throw new ApiRequestError(message, response.status);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export function createLink(serverUrl: string, apiKey: string, body: CreateLinkRequest): Promise<LinkResponse> {
  return request<LinkResponse>(serverUrl, apiKey, '/links', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function listCampaigns(serverUrl: string, apiKey: string): Promise<Campaign[]> {
  return request<Campaign[]>(serverUrl, apiKey, '/campaigns');
}
