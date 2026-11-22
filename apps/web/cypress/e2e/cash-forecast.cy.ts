// INV-9: previsao de caixa na SPA. O usuario demo acumula lancamentos de outras execucoes, entao o
// teste compara cada celula antes e depois: um gasto com data futura entra no mes dele, e uma compra no
// cartao entra so na fatura (mes de vencimento), nunca como saida no mes da compra.
describe('Previsao de caixa', () => {
  let stamp = 0;
  let accountName = '';
  let cardLabel = '';

  const pad = (n: number) => String(n).padStart(2, '0');
  const today = new Date();
  const monthOf = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}`;
  const currentMonth = monthOf(today);
  // Cartao fecha dia 10 e vence dia 17: compra ate o dia 10 vence neste mes; depois, no seguinte.
  const dueMonth = monthOf(new Date(today.getFullYear(), today.getMonth() + (today.getDate() <= 10 ? 0 : 1), 1));
  const futureDate = new Date(today.getFullYear(), today.getMonth() + 3, 10);
  const futureMonth = monthOf(futureDate);
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

  /** `R$ 1.234,56` → 1234.56 (o espaco do toLocaleString e nao separavel). */
  const parse = (text: string) => Number(text.replace(/[^\d,-]/g, '').replace(',', '.'));

  type Cells = { income: number; expense: number; invoices: number };

  const readRow = (month: string) =>
    cy.get(`[data-cy=forecast-row][data-month="${month}"]`).then(($row) => ({
      income: parse($row.find('[data-cy=forecast-income]').text()),
      expense: parse($row.find('[data-cy=forecast-expense]').text()),
      invoices: parse($row.find('[data-cy=forecast-invoices]').contents().first().text()),
    }));

  const openForecast = (scope: 'total' | 'personal' | 'business') => {
    cy.get('[data-cy=tab-forecast]').click();
    cy.get('[data-cy=forecast-scope]').select(scope);
    cy.get('[data-cy=forecast-table]').should('be.visible');
  };

  // A previsao le a projecao do reporting: recarrega ate a celula mudar o esperado.
  const waitForRow = (month: string, check: (row: Cells) => boolean, attempts = 30) => {
    readRow(month).then((row) => {
      if (check(row)) return;
      if (attempts <= 1) throw new Error(`A previsao de ${month} nao refletiu o lancamento (projecao atrasada?).`);
      cy.wait(1_000);
      cy.intercept('GET', '**/api/reports/cash-forecast*').as('forecast');
      cy.get('[data-cy=forecast-refresh]').click();
      cy.wait('@forecast');
      waitForRow(month, check, attempts - 1);
    });
  };

  beforeEach(() => {
    stamp = Date.now();
    accountName = `E2E Previsao ${stamp}`;
    cardLabel = `Cartao Previsao ${stamp}`;
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('mostra o gasto futuro no mes dele e a compra no cartao so na fatura', () => {
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(accountName);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', accountName).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(accountName);
    cy.get('[data-cy=card-label]').type(cardLabel);
    cy.get('[data-cy=card-lastfour]').type('3333');
    cy.get('[data-cy=card-limit]').type('2000');
    cy.get('[data-cy=card-closing]').clear().type('10');
    cy.get('[data-cy=card-due]').clear().type('17');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', cardLabel).should('be.visible');

    // Antes: os valores de hoje (PF) dos meses que vao mudar.
    openForecast('personal');
    cy.get('[data-cy=forecast-notice]').should('contain', 'já está lançado');
    cy.get('[data-cy=forecast-row]').should('have.length', 6);
    readRow(currentMonth).as('currentBefore');
    readRow(dueMonth).as('dueBefore');
    readRow(futureMonth).as('futureBefore');

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(accountName);
    cy.get('[data-cy=tx-payment-method]').select('CreditCard');
    cy.get('[data-cy=tx-card]').find('option').contains(cardLabel).then(($opt) => {
      cy.get('[data-cy=tx-card]').select($opt.val() as string);
    });
    cy.get('[data-cy=tx-description]').type(`Compra ${stamp}`);
    cy.get('[data-cy=tx-category]').type(`Previsao ${stamp}`);
    cy.get('[data-cy=tx-amount]').type('321');
    cy.get('[data-cy=tx-date]').clear().type(iso(today));
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-table]').contains('td', `Compra ${stamp}`).should('be.visible');

    // Gasto fora do cartao com data futura (o formulario so registra gastos).
    cy.get('[data-cy=tx-account]').select(accountName);
    cy.get('[data-cy=tx-payment-method]').select('Pix');
    cy.get('[data-cy=tx-description]').clear().type(`Futuro ${stamp}`);
    cy.get('[data-cy=tx-category]').clear().type(`Previsao ${stamp}`);
    cy.get('[data-cy=tx-amount]').clear().type('1234.56');
    cy.get('[data-cy=tx-date]').clear().type(iso(futureDate));
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-table]').contains('td', `Futuro ${stamp}`).should('be.visible');

    openForecast('personal');
    cy.get<Cells>('@futureBefore').then((before) => {
      waitForRow(futureMonth, (row) => Math.abs(row.expense - before.expense - 1234.56) < 0.001);
    });
    cy.get<Cells>('@dueBefore').then((before) => {
      waitForRow(dueMonth, (row) => Math.abs(row.invoices - before.invoices - 321) < 0.001);
    });
    // A compra no cartao nao e saida no mes em que foi feita.
    cy.get<Cells>('@currentBefore').then((before) => {
      readRow(currentMonth).its('expense').should('eq', before.expense);
    });

    // O total inclui o PF; o filtro e o horizonte trocam a tabela.
    cy.get('[data-cy=forecast-scope]').select('total');
    readRow(futureMonth).its('expense').should('be.gte', 1234.56);
    cy.get('[data-cy=forecast-months]').select('12');
    cy.get('[data-cy=forecast-row]').should('have.length', 12);
  });
});
