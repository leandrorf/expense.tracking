import { readFileSync } from 'node:fs';
import { inflateRawSync } from 'node:zlib';
import { defineConfig } from 'cypress';

/**
 * CTB-7: abre um ZIP baixado e devolve cada arquivo como texto, sem biblioteca: cabecalho local de
 * 30 bytes + nome + extra, depois o conteudo (deflate ou guardado). O mesmo leitor do smoke test.
 */
function unzip(path: string): Record<string, string> {
  const zip = readFileSync(path);
  const files: Record<string, string> = {};
  for (let at = 0; zip.readUInt32LE(at) === 0x04034b50; ) {
    const [nameLength, extraLength, size] = [zip.readUInt16LE(at + 26), zip.readUInt16LE(at + 28), zip.readUInt32LE(at + 18)];
    const start = at + 30 + nameLength + extraLength;
    const body = zip.subarray(start, start + size);
    files[zip.subarray(at + 30, at + 30 + nameLength).toString()] =
      (zip.readUInt16LE(at + 8) === 8 ? inflateRawSync(body) : body).toString('utf8');
    at = start + size;
  }
  return files;
}

// Testes EndToEnd da SPA contra o stack real (docker compose up).
// Portas default: web 5173, identity 5080, gateway 8080 — sobrescreva por env se mudar.
export default defineConfig({
  e2e: {
    baseUrl: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    specPattern: 'cypress/e2e/**/*.cy.ts',
    supportFile: 'cypress/support/e2e.ts',
    defaultCommandTimeout: 10_000,
    requestTimeout: 15_000,
    responseTimeout: 15_000,
    video: false,
    setupNodeEvents(on) {
      on('task', { unzip });
    },
    retries: { runMode: 1, openMode: 0 },
    env: {
      // Origem do Identity Provider (tela de login hospedada pelo proprio IdP).
      identityUrl: process.env.E2E_IDENTITY_URL ?? 'http://localhost:5080',
      // Gateway das APIs, para testes que mudam dados por fora da tela (ex.: outra aba, LNC-5).
      gatewayUrl: process.env.E2E_GATEWAY_URL ?? 'http://localhost:8080',
      // NTF-8: API do Mailpit (SMTP de desenvolvimento), onde o E2E le os e-mails dos avisos.
      mailpitUrl: process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025',
      // Credenciais do usuario demo semeado pelo identity (IdentitySeeder).
      demoEmail: process.env.E2E_DEMO_EMAIL ?? 'demo@expense.local',
      demoPassword: process.env.E2E_DEMO_PASSWORD ?? 'Password123!',
    },
  },
});
