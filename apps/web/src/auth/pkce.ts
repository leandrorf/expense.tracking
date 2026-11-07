// Helpers de PKCE (RFC 7636) usando a Web Crypto API nativa do navegador.

function base64UrlEncode(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function randomBase64Url(numberOfBytes: number): string {
  const bytes = new Uint8Array(numberOfBytes);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes);
}

/** code_verifier de alta entropia (RFC 7636 recomenda 43-128 chars). */
export function createCodeVerifier(): string {
  return randomBase64Url(32);
}

/** Parametro state anti-CSRF. */
export function createState(): string {
  return randomBase64Url(16);
}

/** code_challenge = BASE64URL(SHA256(ASCII(code_verifier))). */
export async function createCodeChallengeS256(codeVerifier: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(codeVerifier));
  return base64UrlEncode(new Uint8Array(digest));
}
