/// <reference types="cypress" />

// Faz o login OAuth2 (Authorization Code + PKCE) de ponta a ponta pela UI real:
// SPA -> /oauth/authorize -> tela de login do IdP (outra origem) -> /callback -> tokens.
// O resultado fica em cache via cy.session (sessionStorage da SPA), compartilhado entre specs.
Cypress.Commands.add('login', (email: string, password: string) => {
  const identityUrl = Cypress.env('identityUrl') as string;

  cy.session(
    ['oauth-login', email],
    () => {
      cy.visit('/');
      cy.get('[data-cy=login]').click();

      // Agora estamos na origem do Identity Provider: preenche a tela de login hospedada por ele.
      cy.origin(identityUrl, { args: { email, password } }, ({ email, password }) => {
        cy.get('#email').type(email);
        cy.get('#password').type(password, { log: false });
        cy.get('button[type=submit]').click();
      });

      // De volta a SPA: o /callback trocou o authorization code por tokens e redirecionou para '/'.
      cy.get('[data-cy=session-active]', { timeout: 30_000 }).should('be.visible');
    },
    {
      cacheAcrossSpecs: true,
      validate() {
        // Sessao so e valida se a SPA abre autenticada (tokens ainda presentes).
        cy.visit('/');
        cy.get('[data-cy=session-active]', { timeout: 20_000 }).should('be.visible');
      },
    },
  );
});

// EVT-3: o Resumo le a projecao do reporting, que recebe o lancamento alguns segundos depois
// (margem do feed do transactions + polling do worker). Recarrega o Resumo (botao Aplicar) ate o
// texto aparecer no bloco, em vez de esperar um tempo fixo.
Cypress.Commands.add('waitForSummary', (selector: string, text: string, attempts = 30) => {
  cy.get('[data-cy=summary-stats]').should('be.visible');
  cy.get(selector).then(($block) => {
    if ($block.text().includes(text)) {
      return;
    }

    if (attempts <= 1) {
      throw new Error(`O Resumo nao refletiu "${text}" em ${selector} (projecao atrasada?).`);
    }

    cy.wait(1_000);
    cy.get('[data-cy=summary-apply]').click();
    cy.waitForSummary(selector, text, attempts - 1);
  });
});

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace Cypress {
    interface Chainable {
      /** Autentica o usuario via fluxo OAuth2 + PKCE pela UI, com cache de sessao. */
      login(email: string, password: string): Chainable<void>;
      /** Recarrega o Resumo ate `text` aparecer em `selector` (espera a projecao do reporting). */
      waitForSummary(selector: string, text: string, attempts?: number): Chainable<void>;
    }
  }
}

export {};
