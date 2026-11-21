// BDG-9: jornadas de orcamento PF e PJ. O consumido vem da projecao do reporting (alimentada
// pelos eventos do transactions), entao cada jornada espera a projecao com cy.waitForBudget.
describe('Orcamentos PF e PJ', () => {
  beforeEach(() => {
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  const createAccount = (name: string, scope: 'Personal' | 'Business') => {
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(name);
    cy.get('[data-cy=account-scope]').select(scope);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', name).should('be.visible');
  };

  const createBudget = (category: string, scope: 'Personal' | 'Business', amount: string) => {
    cy.get('[data-cy=tab-budgets]').click();
    cy.get('[data-cy=budget-category]').type(category);
    cy.get('[data-cy=budget-scope]').select(scope);
    cy.get('[data-cy=budget-amount]').type(amount);
    cy.get('[data-cy=budget-submit]').click();
    cy.get(`[data-cy=budgets-${scope.toLowerCase()}] [data-cy=budget-row][data-category="${category}"]`).should('exist');
  };

  const statusRow = (scope: 'personal' | 'business', category: string) =>
    cy.get(`[data-cy=budget-status-${scope}] [data-cy=budget-status-row][data-category="${category}"]`);

  it('PF: gasto no Pix acima do limite deixa o orcamento Estourado', () => {
    // Nomes gerados dentro do teste: o retry do Cypress cria outra conta sem repetir o nome
    // (nome repetido faria o select de conta casar duas opcoes).
    const stamp = Date.now();
    const accountName = `E2E BDG PF ${stamp}`;
    const category = `Alimentacao BDG ${stamp}`;
    const description = `Mercado ${stamp}`;

    createAccount(accountName, 'Personal');
    createBudget(category, 'Personal', '100');
    statusRow('personal', category).should('have.attr', 'data-status', 'OnTrack');

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(accountName);
    cy.get('[data-cy=tx-payment-method]').select('Pix');
    cy.get('[data-cy=tx-description]').type(description);
    cy.get('[data-cy=tx-category]').type(category);
    cy.get('[data-cy=tx-amount]').type('150');
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');

    cy.get('[data-cy=tab-budgets]').click();
    cy.waitForBudget('personal', category, '150,00');
    statusRow('personal', category).should('have.attr', 'data-status', 'Exceeded');
    statusRow('personal', category).find('[data-cy=budget-status-health]').should('contain', 'Estourado');
    statusRow('personal', category).find('[data-cy=budget-status-limit]').should('contain', '100,00');
    statusRow('personal', category).should('contain', 'Passou');
    statusRow('personal', category).find('[data-cy=budget-status-remaining]').should('contain', '50,00');
    statusRow('personal', category).find('[data-cy=budget-status-percent]').should('contain', '150%');
  });

  it('PJ: gasto no cartao de credito sobe o orcamento PJ e nao o PF da mesma categoria', () => {
    const stamp = Date.now();
    const accountName = `E2E BDG PJ ${stamp}`;
    const cardLabel = `Cartao BDG ${stamp}`;
    const category = `Viagens BDG ${stamp}`;
    const description = `Passagem ${stamp}`;

    createAccount(accountName, 'Business');
    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(accountName);
    cy.get('[data-cy=card-label]').type(cardLabel);
    cy.get('[data-cy=card-lastfour]').type('4242');
    cy.get('[data-cy=card-limit]').type('5000');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', cardLabel).should('be.visible');

    createBudget(category, 'Business', '1000');
    createBudget(category, 'Personal', '500');

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(accountName);
    cy.get('[data-cy=tx-payment-method]').select('CreditCard');
    cy.get('[data-cy=tx-card]').select(`${cardLabel} •••• 4242`);
    cy.get('[data-cy=tx-description]').type(description);
    cy.get('[data-cy=tx-category]').type(category);
    cy.get('[data-cy=tx-amount]').type('300');
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');

    cy.get('[data-cy=tab-budgets]').click();
    cy.waitForBudget('business', category, '300,00');
    statusRow('business', category).should('have.attr', 'data-status', 'OnTrack');
    statusRow('business', category).find('[data-cy=budget-status-percent]').should('contain', '30%');
    // Escopos nunca se misturam: o orcamento PF da mesma categoria continua zerado.
    statusRow('personal', category).find('[data-cy=budget-status-spent]').should('contain', '0,00');
    statusRow('personal', category).find('[data-cy=budget-status-percent]').should('contain', '0%');
  });
});
