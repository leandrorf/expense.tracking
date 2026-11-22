// LNC-2 (ADR-0007, decisao 7): receita e observacoes pelo formulario. A lista mostra receita com +
// em verde e gasto com - em vermelho, e as observacoes abaixo da descricao. Receita nao e parcelada,
// entao o formulario nem oferece parcelas para ela.
describe('Receita e observacoes', () => {
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

  it('lanca uma receita com observacao e a lista a distingue do gasto', () => {
    const account = `E2E Receita ${stamp}`;
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);

    // Parcelas so para gasto no cartao de credito.
    cy.get('[data-cy=tx-payment-method]').select('CreditCard');
    cy.get('[data-cy=tx-installments]').should('exist');
    cy.get('[data-cy=tx-type]').select('Income');
    cy.get('[data-cy=tx-installments]').should('not.exist');

    const lancar = (type: 'Expense' | 'Income', description: string, amount: string, notes?: string) => {
      cy.get('[data-cy=tx-type]').select(type);
      cy.get('[data-cy=tx-payment-method]').select('Pix');
      cy.get('[data-cy=tx-description]').clear().type(description);
      cy.get('[data-cy=tx-category]').clear().type(`Receita ${stamp}`);
      cy.get('[data-cy=tx-amount]').clear().type(amount);
      cy.get('[data-cy=tx-date]').clear().type(iso);
      if (notes) cy.get('[data-cy=tx-notes]').clear().type(notes);
      cy.get('[data-cy=tx-submit]').click();
      cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');
    };

    lancar('Income', `Cliente ${stamp}`, '2500', `NF ${stamp}`);
    lancar('Expense', `Fornecedor ${stamp}`, '40');

    cy.contains('[data-cy=tx-row]', `Cliente ${stamp}`).within(() => {
      cy.root().should('have.attr', 'data-type', 'Income');
      cy.get('[data-cy=tx-value]').should('have.class', 'positive').and('contain', '+').and('contain', '2.500,00');
      cy.get('[data-cy=tx-notes-label]').should('have.text', `NF ${stamp}`);
    });
    cy.contains('[data-cy=tx-row]', `Fornecedor ${stamp}`).within(() => {
      cy.root().should('have.attr', 'data-type', 'Expense');
      cy.get('[data-cy=tx-value]').should('have.class', 'negative').and('contain', '−').and('contain', '40,00');
      cy.get('[data-cy=tx-notes-label]').should('not.exist');
    });

    // O campo de observacoes limpa depois de registrar.
    cy.get('[data-cy=tx-notes]').should('have.value', '');
  });
});
