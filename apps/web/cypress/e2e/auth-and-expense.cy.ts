// Jornada principal: login OAuth2 -> cria conta PF -> registra gasto via Pix ->
// confere o lancamento na lista e o reflexo no Resumo (reporting agregando tudo).
describe('Gasto pessoal (PF) com Pix', () => {
  const stamp = Date.now();
  const accountName = `E2E PF ${stamp}`;
  const description = `Cafe E2E ${stamp}`;
  const category = `Alimentacao ${stamp}`;
  const amount = '42.50';

  beforeEach(() => {
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('cria conta, registra o gasto e ve o total refletido no resumo', () => {
    // 1) Cria uma conta pessoal (PF)
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(accountName);
    cy.get('[data-cy=account-scope]').select('Personal');
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', accountName).should('be.visible');

    // 2) Registra um gasto pago com Pix nessa conta
    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(accountName);
    cy.get('[data-cy=tx-payment-method]').select('Pix');
    cy.get('[data-cy=tx-description]').type(description);
    cy.get('[data-cy=tx-category]').type(category);
    cy.get('[data-cy=tx-amount]').type(amount);
    cy.get('[data-cy=tx-submit]').click();

    cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');
    cy.get('[data-cy=tx-table]').contains('td', 'Pix').should('be.visible');

    // 3) O Resumo (reporting) consolida o gasto: a categoria unica deste run bate o valor.
    cy.get('[data-cy=tab-summary]').click();
    cy.get('[data-cy=by-payment-method]').contains('Pix').should('be.visible');
    cy.get('[data-cy=by-category]').contains('li', category).should('contain', '42,50');
  });
});
