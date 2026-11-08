// Jornada PJ: login -> cria conta empresa (PJ) -> cadastra cartao de credito ->
// registra gasto no cartao. Exercita o contrato unico de meios de pagamento: o
// select e alimentado pelo catalogo do accounts e o code enviado e o nome do enum.
describe('Gasto empresarial (PJ) no cartao de credito', () => {
  const stamp = Date.now();
  const accountName = `E2E PJ ${stamp}`;
  const cardLabel = `Cartao E2E ${stamp}`;
  const description = `Nuvem E2E ${stamp}`;
  const category = `Infra ${stamp}`;

  beforeEach(() => {
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('cria conta PJ, cartao e um gasto no cartao via catalogo de pagamentos', () => {
    // 1) Conta empresarial (PJ)
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(accountName);
    cy.get('[data-cy=account-scope]').select('Business');
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', accountName).should('be.visible');

    // 2) Cartao de credito vinculado a conta PJ
    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(accountName);
    cy.get('[data-cy=card-label]').type(cardLabel);
    cy.get('[data-cy=card-lastfour]').type('4242');
    cy.get('[data-cy=card-limit]').type('5000');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', cardLabel).should('be.visible');

    // 3) Gasto no cartao. O rotulo "Cartao de credito" vem do catalogo do accounts,
    //    mas o valor enviado e o code canonico "CreditCard" (contrato unico ponta a ponta).
    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(accountName);
    cy.get('[data-cy=tx-payment-method]').select('Cartao de credito');
    cy.get('[data-cy=tx-payment-method]').should('have.value', 'CreditCard');

    // O seletor de cartao aparece quando o meio exige cartao; escolhe o cartao recem-criado.
    cy.get('[data-cy=tx-card]').should('be.visible');
    cy.get('[data-cy=tx-card] option').eq(1).then(($opt) => {
      cy.get('[data-cy=tx-card]').select($opt.val() as string);
    });

    cy.get('[data-cy=tx-description]').type(description);
    cy.get('[data-cy=tx-category]').type(category);
    cy.get('[data-cy=tx-amount]').type('199.90');
    cy.get('[data-cy=tx-submit]').click();

    cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');
    cy.get('[data-cy=tx-table]').contains('td', 'Cartao de credito').should('be.visible');

    // 4) O resumo separa por escopo: o gasto PJ aparece em "Empresa (PJ)".
    cy.get('[data-cy=tab-summary]').click();
    cy.get('[data-cy=by-scope]').contains('Empresa (PJ)').should('be.visible');
    cy.get('[data-cy=by-category]').contains('li', category).should('contain', '199,90');
  });
});
