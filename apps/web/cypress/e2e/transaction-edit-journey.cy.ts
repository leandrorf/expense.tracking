// LNC-6 (ADR-0007): jornada da edicao pela SPA. Numa conta PJ com cartao e orcamentos, lanca uma
// receita e uma compra em 3x e depois corrige a compra (valor, parcelas e categoria). Resumo,
// orcamento e fatura leem a projecao do reporting, que aplica o TransactionUpdated. O usuario demo
// acumula dados, entao a receita e comparada por diferenca e as categorias sao unicas por execucao.
describe('Edicao de lancamento (jornada com relatorios)', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const today = new Date();
  const iso = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;

  /** `R$ 1.234,56` → 1234.56. */
  const parse = (text: string) => Number(text.replace(/[^\d,-]/g, '').replace(',', '.'));

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  const readIncome = () => {
    cy.get('[data-cy=tab-summary]').click();
    cy.get('[data-cy=summary-stats]').should('be.visible');
    return cy.get('[data-cy=stat-income] .stat-value').invoke('text').then(parse);
  };

  const createBudget = (category: string) => {
    cy.get('[data-cy=tab-budgets]').click();
    cy.get('[data-cy=budget-category]').type(category);
    cy.get('[data-cy=budget-scope]').select('Business');
    cy.get('[data-cy=budget-amount]').type('1000');
    cy.get('[data-cy=budget-submit]').click();
    cy.get(`[data-cy=budgets-business] [data-cy=budget-row][data-category="${category}"]`).should('exist');
  };

  const fillTransaction = (type: 'Expense' | 'Income', description: string, category: string, amount: string) => {
    cy.get('[data-cy=tx-type]').select(type);
    cy.get('[data-cy=tx-description]').clear().type(description);
    cy.get('[data-cy=tx-category]').clear().type(category);
    cy.get('[data-cy=tx-amount]').clear().type(amount);
    cy.get('[data-cy=tx-date]').clear().type(iso);
  };

  it('a compra corrigida muda o Resumo, o orcamento e a fatura', () => {
    const [account, card] = [`E2E Jornada Edicao ${stamp}`, `Cartao Edicao ${stamp}`];
    const [before, after] = [`Antes ${stamp}`, `Depois ${stamp}`];

    readIncome().as('incomeBefore');

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-scope]').select('Business');
    cy.get('[data-cy=account-taxid]').type('11.222.333/0001-81');
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('7777');
    cy.get('[data-cy=card-limit]').type('10000');
    cy.get('[data-cy=card-closing]').clear().type('10');
    cy.get('[data-cy=card-due]').clear().type('17');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', card).should('be.visible');

    createBudget(before);
    createBudget(after);

    // Receita (Pix) e compra de R$ 300 em 3x no cartao.
    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);
    cy.get('[data-cy=tx-payment-method]').select('Pix');
    fillTransaction('Income', `Cliente ${stamp}`, `Receita ${stamp}`, '2500');
    cy.get('[data-cy=tx-submit]').click();
    cy.contains('[data-cy=tx-row]', `Cliente ${stamp}`).should('exist');

    cy.get('[data-cy=tx-payment-method]').select('CreditCard');
    cy.get('[data-cy=tx-card]').find('option').contains(card).then(($opt) => {
      cy.get('[data-cy=tx-card]').select($opt.val() as string);
    });
    fillTransaction('Expense', `Notebook ${stamp}`, before, '300');
    cy.get('[data-cy=tx-installments]').select('3');
    cy.get('[data-cy=tx-submit]').click();
    cy.contains('[data-cy=tx-row]', `Notebook ${stamp}`).should('contain', 'em 3x');

    // Antes da correcao, o orcamento "antes" consome os R$ 300.
    cy.get('[data-cy=tab-budgets]').click();
    cy.waitForBudget('business', before, '300,00');

    // Correcao: R$ 600 em 6x, em outra categoria.
    cy.get('[data-cy=tab-transactions]').click();
    cy.contains('[data-cy=tx-row]', `Notebook ${stamp}`).find('[data-cy=tx-edit]').click();
    cy.get('[data-cy=tx-amount]').clear().type('600');
    cy.get('[data-cy=tx-installments]').select('6');
    cy.get('[data-cy=tx-category]').clear().type(after);
    cy.get('[data-cy=tx-submit]').click();
    cy.contains('[data-cy=tx-row]', `Notebook ${stamp}`).should('contain', '600,00').and('contain', 'em 6x').and('contain', after);

    // Orcamento: a categoria nova recebe os R$ 600 e a antiga volta a zero.
    cy.get('[data-cy=tab-budgets]').click();
    cy.waitForBudget('business', after, '600,00');
    cy.get(`[data-cy=budget-status-business] [data-cy=budget-status-row][data-category="${before}"] [data-cy=budget-status-spent]`)
      .invoke('text')
      .then((text) => expect(parse(text)).to.eq(0));

    // Resumo: so a categoria nova, com o valor corrigido; a receita entrou.
    cy.get('[data-cy=tab-summary]').click();
    cy.waitForSummary('[data-cy=by-category]', after);
    cy.get('[data-cy=by-category]').contains('li', after).should('contain', '600,00');
    cy.get('[data-cy=by-category]').should('not.contain', before);
    cy.get<number>('@incomeBefore').then((incomeBefore) =>
      readIncome().then((income) => expect(income - incomeBefore).to.be.closeTo(2500, 0.001)),
    );

    // Fatura: seis parcelas de R$ 100, a primeira na fatura atual e a ultima "6/6".
    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=cards-table]').contains('tr', card).find('[data-cy=card-invoices]').click();
    cy.get('[data-cy=invoice-items]').contains('tr', `Notebook ${stamp}`).should('contain', '1/6').and('contain', '100,00');
    cy.get('[data-cy=invoice-total]').should('contain', '100,00');
    for (let i = 0; i < 5; i++) cy.get('[data-cy=invoice-next]').click();
    cy.get('[data-cy=invoice-items]').contains('tr', `Notebook ${stamp}`).should('contain', '6/6');
    cy.get('[data-cy=invoice-next]').should('be.disabled');
  });
});
