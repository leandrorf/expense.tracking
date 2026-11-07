import { createCodeChallengeS256, createCodeVerifier, createState } from './pkce';

// Cliente OAuth2 (Authorization Code + PKCE) implementado sem bibliotecas de terceiros.

export interface AuthConfig {
  authority: string;
  clientId: string;
  redirectUri: string;
  scope: string;
}

export const authConfig: AuthConfig = {
  authority: import.meta.env.VITE_IDENTITY_AUTHORITY ?? 'http://localhost:5080',
  clientId: import.meta.env.VITE_CLIENT_ID ?? 'expense-tracking-spa',
  redirectUri: import.meta.env.VITE_REDIRECT_URI ?? 'http://localhost:5173/callback',
  scope: import.meta.env.VITE_SCOPE ?? 'openid profile expenses.read offline_access',
};

interface TokenSet {
  accessToken: string;
  refreshToken?: string;
  expiresAt: number; // epoch ms
  scope: string;
}

interface TokenResponse {
  access_token: string;
  token_type: string;
  expires_in: number;
  scope: string;
  refresh_token?: string | null;
}

const TOKENS_KEY = 'expense.tokens';
const PKCE_KEY = 'expense.pkce';

export function getTokens(): TokenSet | null {
  try {
    const raw = sessionStorage.getItem(TOKENS_KEY);
    return raw ? (JSON.parse(raw) as TokenSet) : null;
  } catch {
    return null;
  }
}

export function isAuthenticated(): boolean {
  return getTokens() !== null;
}

export function clearTokens(): void {
  sessionStorage.removeItem(TOKENS_KEY);
}

function store(response: TokenResponse): void {
  const tokens: TokenSet = {
    accessToken: response.access_token,
    refreshToken: response.refresh_token ?? undefined,
    expiresAt: Date.now() + response.expires_in * 1000,
    scope: response.scope,
  };
  sessionStorage.setItem(TOKENS_KEY, JSON.stringify(tokens));
}

async function postToken(body: URLSearchParams): Promise<Response> {
  return fetch(`${authConfig.authority}/oauth/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body,
  });
}

/** Passo 1: redireciona o navegador ao endpoint de autorizacao com PKCE. */
export async function beginLogin(): Promise<void> {
  const codeVerifier = createCodeVerifier();
  const state = createState();
  const codeChallenge = await createCodeChallengeS256(codeVerifier);

  sessionStorage.setItem(PKCE_KEY, JSON.stringify({ codeVerifier, state }));

  const params = new URLSearchParams({
    response_type: 'code',
    client_id: authConfig.clientId,
    redirect_uri: authConfig.redirectUri,
    scope: authConfig.scope,
    state,
    code_challenge: codeChallenge,
    code_challenge_method: 'S256',
  });

  window.location.assign(`${authConfig.authority}/oauth/authorize?${params.toString()}`);
}

/** Passo 2: no /callback, valida o state e troca o code por tokens. */
export async function completeLogin(): Promise<void> {
  const url = new URL(window.location.href);
  const code = url.searchParams.get('code');
  const state = url.searchParams.get('state');
  const error = url.searchParams.get('error');

  if (error) {
    throw new Error(`Autorizacao negada: ${error}`);
  }

  const rawPkce = sessionStorage.getItem(PKCE_KEY);
  if (!code || !rawPkce) {
    throw new Error('Callback invalido: faltam code ou dados do PKCE.');
  }

  const { codeVerifier, state: expectedState } = JSON.parse(rawPkce) as {
    codeVerifier: string;
    state: string;
  };

  if (state !== expectedState) {
    throw new Error('State invalido (possivel CSRF).');
  }

  const body = new URLSearchParams({
    grant_type: 'authorization_code',
    code,
    redirect_uri: authConfig.redirectUri,
    client_id: authConfig.clientId,
    code_verifier: codeVerifier,
  });

  const response = await postToken(body);
  if (!response.ok) {
    throw new Error('Falha ao trocar o authorization code por tokens.');
  }

  store((await response.json()) as TokenResponse);
  sessionStorage.removeItem(PKCE_KEY);
}

/** Renova o access token usando o refresh token (com rotacao no servidor). */
export async function refresh(): Promise<boolean> {
  const tokens = getTokens();
  if (!tokens?.refreshToken) {
    return false;
  }

  const body = new URLSearchParams({
    grant_type: 'refresh_token',
    refresh_token: tokens.refreshToken,
    client_id: authConfig.clientId,
  });

  const response = await postToken(body);
  if (!response.ok) {
    clearTokens();
    return false;
  }

  store((await response.json()) as TokenResponse);
  return true;
}

export interface UserInfo {
  sub: string | null;
  client_id: string | null;
  scope: string | null;
}

/** Chama o endpoint protegido /connect/userinfo, renovando o token se necessario. */
export async function getUserInfo(): Promise<UserInfo> {
  const request = async (): Promise<Response> => {
    const tokens = getTokens();
    if (!tokens) {
      throw new Error('Nao autenticado.');
    }
    return fetch(`${authConfig.authority}/connect/userinfo`, {
      headers: { Authorization: `Bearer ${tokens.accessToken}` },
    });
  };

  let response = await request();
  if (response.status === 401 && (await refresh())) {
    response = await request();
  }

  if (!response.ok) {
    throw new Error('Falha ao obter userinfo.');
  }

  return (await response.json()) as UserInfo;
}

export function logout(): void {
  clearTokens();
  window.location.assign('/');
}
