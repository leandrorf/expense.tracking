import { getTokens, refresh } from '../auth/oauth';

// Base das APIs de recurso — por padrao o gateway (entrada unica sob /api/**).
const apiBase = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8080';

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

interface ProblemDetails {
  title?: string;
  detail?: string;
}

/**
 * Chamada autenticada às APIs. Adiciona o Bearer token e, em caso de 401,
 * tenta renovar uma vez (rotação de refresh token) e repete.
 */
export async function apiFetch<T>(path: string, options: RequestInit = {}): Promise<T> {
  const send = async (): Promise<Response> => {
    const tokens = getTokens();
    const headers = new Headers(options.headers);
    if (tokens) {
      headers.set('Authorization', `Bearer ${tokens.accessToken}`);
    }
    if (options.body && !headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json');
    }
    return fetch(`${apiBase}${path}`, { ...options, headers });
  };

  let response = await send();
  if (response.status === 401 && (await refresh())) {
    response = await send();
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  const data = text ? JSON.parse(text) : undefined;

  if (!response.ok) {
    const problem = (data ?? {}) as ProblemDetails;
    throw new ApiError(response.status, problem.title ?? 'error', problem.detail ?? response.statusText);
  }

  return data as T;
}
