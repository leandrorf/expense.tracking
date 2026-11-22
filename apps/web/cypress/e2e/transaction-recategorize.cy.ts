// LNC-7: recategorizar varios lancamentos de uma vez pela lista. Cada um vai com a versao que a lista
// leu; o que mudou em outra aba (simulada com um PUT direto no gateway) e informado e continua
// marcado, sem ser sobrescrito.
describe('Recategorizar em lote', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const today = new Date();
  const iso = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  const row = (description: string) => cy.contains('[data-cy=tx-row]', description);

  const register = (description: string, category: string) => {
    cy.get('[data-cy=tx-payment-method]').select('Pix');
    cy.get('[data-cy=tx-description]').type(description);
    cy.get('[data-cy=tx-category]').type(category);
    cy.get('[data-cy=tx-amount]').type('25');
    cy.get('[data-cy=tx-date]').clear().type(iso);
    cy.get('[data-cy=tx-submit]').click();
    row(description).should('contain', category);
  };

  /** Outra aba: troca a categoria direto na API, com a versao atual. */
  const recategorizeElsewhere = (description: string, category: string) =>
    cy.window().then((win) => {
      const token = JSON.parse(win.sessionStorage.getItem('expense.tokens')!).accessToken as string;
      const api = `${Cypress.env('gatewayUrl')}/api/transactions`;
      const headers = { Authorization: `Bearer ${token}` };
      cy.request({ url: api, headers }).then(({ body }) => {
        const tx = (body as Array<Record<string, unknown>>).find((t) => t.description === description)!;
        cy.request({ method: 'PUT', url: `${api}/${tx.id}`, headers, body: { ...tx, category } })
          .its('body.version')
          .should('eq', (tx.version as number) + 1);
      });
    });

  it('troca a categoria dos selecionados e informa o que mudou em outra aba', () => {
    const account = `E2E Lote ${stamp}`;
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);
    register(`Padaria ${stamp}`, `Outros ${stamp}`);
    register(`Feira ${stamp}`, `Outros ${stamp}`);
    register(`Acougue ${stamp}`, `Outros ${stamp}`);

    // Sem selecao nao ha barra; marcar mostra quantos.
    cy.get('[data-cy=tx-bulk]').should('not.exist');
    row(`Padaria ${stamp}`).find('[data-cy=tx-select]').check();
    row(`Feira ${stamp}`).find('[data-cy=tx-select]').check();
    cy.get('[data-cy=tx-bulk-count]').should('have.text', '2 selecionados');

    // Os dois mudam de uma vez; o terceiro fica.
    cy.get('[data-cy=tx-bulk-category]').type(`Mercado ${stamp}`);
    cy.get('[data-cy=tx-bulk-apply]').click();
    cy.get('[data-cy=tx-bulk-result]').should('contain', `2 lançamentos ficaram em Mercado ${stamp}`);
    cy.get('[data-cy=tx-bulk-conflicts]').should('not.exist');
    row(`Padaria ${stamp}`).should('contain', `Mercado ${stamp}`);
    row(`Feira ${stamp}`).should('contain', `Mercado ${stamp}`);
    row(`Acougue ${stamp}`).should('contain', `Outros ${stamp}`);
    cy.get('[data-cy=tx-bulk]').should('not.exist');

    // Outra aba recategoriza o "Acougue" depois que a lista foi lida: ele nao e sobrescrito.
    row(`Padaria ${stamp}`).find('[data-cy=tx-select]').check();
    row(`Acougue ${stamp}`).find('[data-cy=tx-select]').check();
    recategorizeElsewhere(`Acougue ${stamp}`, `Carnes ${stamp}`);
    cy.get('[data-cy=tx-bulk-category]').type(`Feira livre ${stamp}`);
    cy.get('[data-cy=tx-bulk-apply]').click();
    cy.get('[data-cy=tx-bulk-result]').should('contain', `1 lançamento ficou em Feira livre ${stamp}`);
    cy.get('[data-cy=tx-bulk-conflict]').should('have.length', 1).and('contain', `Acougue ${stamp} — Carnes ${stamp}`);
    row(`Padaria ${stamp}`).should('contain', `Feira livre ${stamp}`);
    row(`Acougue ${stamp}`).should('contain', `Carnes ${stamp}`);

    // O que deu conflito continua marcado; aplicar de novo, agora sobre a versao atual, passa.
    cy.get('[data-cy=tx-bulk-count]').should('have.text', '1 selecionado');
    row(`Acougue ${stamp}`).find('[data-cy=tx-select]').should('be.checked');
    cy.get('[data-cy=tx-bulk-category]').type(`Feira livre ${stamp}`);
    cy.get('[data-cy=tx-bulk-apply]').click();
    cy.get('[data-cy=tx-bulk-conflicts]').should('not.exist');
    row(`Acougue ${stamp}`).should('contain', `Feira livre ${stamp}`);

    // "Selecionar todos" e "Limpar selecao".
    cy.get('[data-cy=tx-select-all]').check();
    cy.get('[data-cy=tx-select]').should('be.checked');
    cy.get('[data-cy=tx-bulk-clear]').click();
    cy.get('[data-cy=tx-select]:checked').should('not.exist');
    cy.get('[data-cy=tx-bulk]').should('not.exist');
  });
});
