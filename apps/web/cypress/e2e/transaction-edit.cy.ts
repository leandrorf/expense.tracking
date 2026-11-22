// LNC-5 (ADR-0007): editar um lancamento pela tela. O formulario abre preenchido com a conta travada,
// salva com a versao lida e, se outra aba salvou antes (409), mostra o que esta gravado sem
// sobrescrever. A "outra aba" e simulada com um PUT direto no gateway, com o token da sessao.
describe('Edicao de lancamento', () => {
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

  /** Outra aba: troca a descricao direto na API, com a versao atual. */
  const editElsewhere = (description: string, newDescription: string) =>
    cy.window().then((win) => {
      const token = JSON.parse(win.sessionStorage.getItem('expense.tokens')!).accessToken as string;
      const api = `${Cypress.env('gatewayUrl')}/api/transactions`;
      const headers = { Authorization: `Bearer ${token}` };
      cy.request({ url: api, headers }).then(({ body }) => {
        const tx = (body as Array<Record<string, unknown>>).find((t) => t.description === description)!;
        cy.request({ method: 'PUT', url: `${api}/${tx.id}`, headers, body: { ...tx, description: newDescription } })
          .its('body.version')
          .should('eq', (tx.version as number) + 1);
      });
    });

  it('corrige um gasto, cancela uma edicao e nao sobrescreve o que outra aba salvou', () => {
    const account = `E2E Edicao ${stamp}`;
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);
    cy.get('[data-cy=tx-payment-method]').select('Pix');
    cy.get('[data-cy=tx-description]').type(`Mercado ${stamp}`);
    cy.get('[data-cy=tx-category]').type(`Antes ${stamp}`);
    cy.get('[data-cy=tx-amount]').type('40');
    cy.get('[data-cy=tx-date]').clear().type(iso);
    cy.get('[data-cy=tx-submit]').click();
    row(`Mercado ${stamp}`).should('contain', '40,00');

    // Editar: formulario preenchido, conta travada; salvar muda valor, categoria e observacoes.
    row(`Mercado ${stamp}`).find('[data-cy=tx-edit]').click();
    cy.get('[data-cy=tx-editing]').should('contain', `Editando: Mercado ${stamp}`);
    cy.get('[data-cy=tx-imported-notice]').should('not.exist');
    cy.get('[data-cy=tx-account]').should('be.disabled');
    cy.get('[data-cy=tx-amount]').should('have.value', '40');
    cy.get('[data-cy=tx-submit]').should('have.text', 'Salvar alteração');
    cy.get('[data-cy=tx-amount]').clear().type('55.5');
    cy.get('[data-cy=tx-category]').clear().type(`Depois ${stamp}`);
    cy.get('[data-cy=tx-notes]').type('corrigido');
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-editing]').should('not.exist');
    row(`Mercado ${stamp}`).should('contain', '55,50').and('contain', `Depois ${stamp}`);
    row(`Mercado ${stamp}`).find('[data-cy=tx-notes-label]').should('have.text', 'corrigido');
    cy.get('[data-cy=tx-account]').should('not.be.disabled');

    // Cancelar: nada muda e o formulario volta ao lancamento novo.
    row(`Mercado ${stamp}`).find('[data-cy=tx-edit]').click();
    cy.get('[data-cy=tx-description]').clear().type('Nao salvo');
    cy.get('[data-cy=tx-cancel]').click();
    cy.get('[data-cy=tx-editing]').should('not.exist');
    cy.get('[data-cy=tx-description]').should('have.value', '');
    row(`Mercado ${stamp}`).should('exist');

    // Outra aba salva no meio da edicao: o formulario mostra o valor gravado e pede para refazer.
    row(`Mercado ${stamp}`).find('[data-cy=tx-edit]').click();
    editElsewhere(`Mercado ${stamp}`, `Feira ${stamp}`);
    cy.get('[data-cy=tx-amount]').clear().type('60');
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-conflict]').should('contain', 'mudou enquanto você editava');
    cy.get('[data-cy=tx-description]').should('have.value', `Feira ${stamp}`);
    cy.get('[data-cy=tx-amount]').should('have.value', '55.5');

    // Refeita sobre a versao atual, a edicao passa e mantem o que a outra aba mudou.
    cy.get('[data-cy=tx-amount]').clear().type('60');
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-editing]').should('not.exist');
    row(`Feira ${stamp}`).should('contain', '60,00');
  });
});
