// RCN-6 (ADR-0011): conferir a fatura do cartao com a do banco pelo total. A compra de hoje cai na fatura atual;
// informar o total do banco com R$ 10,00 a mais marca a fatura como conferida com a diferenca aceita, a fatura
// ganha o selo e desfazer tira a marca.
describe('Fatura: conferir pelo total do banco', () => {
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

  it('marca como conferida com a diferenca aceita e desfaz', () => {
    const [account, card, purchase] = [`E2E Conferencia ${stamp}`, `Cartao Conf ${stamp}`, `Compra conf ${stamp}`];

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('7070');
    cy.get('[data-cy=card-limit]').type('5000');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', card).should('be.visible');

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);
    cy.get('[data-cy=tx-payment-method]').select('CreditCard');
    cy.get('[data-cy=tx-card]').find('option').contains(card).then(($o) => cy.get('[data-cy=tx-card]').select($o.val() as string));
    cy.get('[data-cy=tx-description]').type(purchase);
    cy.get('[data-cy=tx-category]').type(`Compras ${stamp}`);
    cy.get('[data-cy=tx-amount]').type('150');
    cy.get('[data-cy=tx-date]').clear().type(iso);
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-table]').contains('td', purchase).should('be.visible');

    // Abre a fatura que tem a compra (a projecao leva um instante).
    cy.get('[data-cy=tab-cards]').click();
    cy.contains('[data-cy=cards-table] tr', card).find('[data-cy=card-invoices]').click();
    // A compra de hoje esta na fatura atual, onde a tela abre; a projecao leva um instante.
    const findPurchase = (attempts = 20) => {
      cy.get('body').then(($body) => {
        if ($body.find(`[data-cy=invoice-item]:contains("${purchase}")`).length > 0) return;
        if (attempts <= 1) throw new Error('A compra nao chegou a fatura.');
        cy.wait(1000);
        cy.get('[data-cy=invoices-back]').click();
        cy.contains('[data-cy=cards-table] tr', card).find('[data-cy=card-invoices]').click();
        findPurchase(attempts - 1);
      });
    };
    findPurchase();

    cy.get('[data-cy=invoice-reconcile]').click();
    cy.get('[data-cy=reconciliation-difference]').should('contain', '150,00');
    cy.get('[data-cy=reconciliation-bank-total]').clear().type('160');
    cy.get('[data-cy=reconciliation-difference]').should('contain', 'diferença').and('contain', '10,00');
    cy.get('[data-cy=reconciliation-mark]').click();
    cy.get('[data-cy=reconciliation-notice]').should('contain', 'diferença de').and('contain', '10,00');
    cy.get('[data-cy=invoice-reconciled]').should('contain', 'Conferida').and('contain', '10,00');
    cy.get('[data-cy=reconciliation-done]').should('contain', '160,00');

    cy.get('[data-cy=reconciliation-undo]').click();
    cy.get('[data-cy=reconciliation-notice]').should('contain', 'Conferência desfeita');
    cy.get('[data-cy=invoice-reconciled]').should('not.exist');
  });
});
