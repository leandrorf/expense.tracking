import { getTokens, refresh } from '../auth/oauth';

// Base das APIs de recurso — por padrao o gateway (entrada unica sob /api/**).
const apiBase = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8080';

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    /** Campos extras do ProblemDetails (ex.: `lines` na confirmação da importação). */
    public readonly extensions: Record<string, unknown> = {},
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  [extension: string]: unknown;
}

/**
 * Chamada autenticada às APIs. Adiciona o Bearer token e, em caso de 401,
 * tenta renovar uma vez (rotação de refresh token) e repete.
 */
export async function apiFetch<T>(path: string, options: RequestInit = {}): Promise<T> {
  const response = await authorizedFetch(path, options);

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  const data = text ? JSON.parse(text) : undefined;

  if (!response.ok) {
    throw problemOf(response, data);
  }

  return data as T;
}

/** CTB-4: baixa um arquivo (ex.: o ZIP da exportação contábil) com a mesma autenticação. */
export async function apiDownload(path: string): Promise<Blob> {
  const response = await authorizedFetch(path);
  if (!response.ok) {
    const text = await response.text();
    throw problemOf(response, text ? JSON.parse(text) : undefined);
  }

  return response.blob();
}

function problemOf(response: Response, data: unknown): ApiError {
  const { title, detail, ...extensions } = (data ?? {}) as ProblemDetails;
  return new ApiError(response.status, title ?? 'error', detail ?? response.statusText, extensions);
}

async function authorizedFetch(path: string, options: RequestInit = {}): Promise<Response> {
  const send = async (): Promise<Response> => {
    const tokens = getTokens();
    const headers = new Headers(options.headers);
    if (tokens) {
      headers.set('Authorization', `Bearer ${tokens.accessToken}`);
    }
    // FormData (upload): o navegador define o multipart com a fronteira.
    if (options.body && !(options.body instanceof FormData) && !headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json');
    }
    return fetch(`${apiBase}${path}`, { ...options, headers });
  };

  let response = await send();
  if (response.status === 401 && (await refresh())) {
    response = await send();
  }

  return response;
}
