import { defineConfig } from 'cypress';

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
    retries: { runMode: 1, openMode: 0 },
    env: {
      // Origem do Identity Provider (tela de login hospedada pelo proprio IdP).
      identityUrl: process.env.E2E_IDENTITY_URL ?? 'http://localhost:5080',
      // Credenciais do usuario demo semeado pelo identity (IdentitySeeder).
      demoEmail: process.env.E2E_DEMO_EMAIL ?? 'demo@expense.local',
      demoPassword: process.env.E2E_DEMO_PASSWORD ?? 'Password123!',
    },
  },
});
