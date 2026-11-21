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

// BDG-9: o acompanhamento dos orcamentos tambem le a projecao. Recarrega o mes (botao
// "atualizar", esperando a resposta do /budgets/status) ate o consumido do orcamento `category`
// no escopo `scope` chegar a `spent` (texto como aparece na tela, ex.: '150,00').
Cypress.Commands.add(
  'waitForBudget',
  (scope: 'personal' | 'business', category: string, spent: string, attempts = 30) => {
    const row = `[data-cy=budget-status-${scope}] [data-cy=budget-status-row][data-category="${category}"]`;
    cy.get(row).then(($row) => {
      if ($row.find('[data-cy=budget-status-spent]').text().includes(spent)) {
        return;
      }

      if (attempts <= 1) {
        throw new Error(`O orcamento "${category}" (${scope}) nao chegou a ${spent} consumido (projecao atrasada?).`);
      }

      cy.wait(1_000);
      cy.intercept('GET', '**/api/budgets/status*').as('budgetStatus');
      cy.get('[data-cy=budget-month-refresh]').click();
      cy.wait('@budgetStatus');
      cy.waitForBudget(scope, category, spent, attempts - 1);
    });
  },
);

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace Cypress {
    interface Chainable {
      /** Autentica o usuario via fluxo OAuth2 + PKCE pela UI, com cache de sessao. */
      login(email: string, password: string): Chainable<void>;
      /** Recarrega o Resumo ate `text` aparecer em `selector` (espera a projecao do reporting). */
      waitForSummary(selector: string, text: string, attempts?: number): Chainable<void>;
      /** Recarrega o acompanhamento do mes ate o consumido do orcamento chegar a `spent`. */
      waitForBudget(scope: 'personal' | 'business', category: string, spent: string, attempts?: number): Chainable<void>;
    }
  }
}

export {};
