// INV-13: saldo informado por conta e saldo projetado no fim de cada mes da previsao. O usuario demo
// acumula contas de outras execucoes, entao o teste compara o saldo PJ antes e depois (por diferenca)
// e remove o saldo no fim para nao deixar rastro nas proximas execucoes.
describe('Saldo projetado na previsao', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const today = new Date();
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const monthOf = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}`;
  const currentMonth = monthOf(today);
  const nextMonthDay = new Date(today.getFullYear(), today.getMonth() + 1, 10);
  const nextMonth = monthOf(nextMonthDay);

  /** `R$ 1.234,56` → 1234.56; "sem saldo" → 0 (o escopo ainda nao tinha conta com saldo). */
  const parse = (text: string) => (text.includes('sem saldo') ? 0 : Number(text.replace(/[^\d,-]/g, '').replace(',', '.')));

  const readBalance = (month: string) =>
    cy.get(`[data-cy=forecast-row][data-month="${month}"] [data-cy=forecast-balance]`).invoke('text').then(parse);

  const openForecast = () => {
    cy.get('[data-cy=tab-forecast]').click();
    cy.get('[data-cy=forecast-scope]').select('business');
    cy.get('[data-cy=forecast-table]').should('be.visible');
  };

  // O saldo depende da projecao do reporting (lancamentos) e do accounts (saldo): recarrega ate bater.
  const waitForBalance = (month: string, expected: number, attempts = 30) => {
    readBalance(month).then((value) => {
      if (Math.abs(value - expected) < 0.005) return;
      if (attempts <= 1) throw new Error(`Saldo de ${month}: esperado ${expected}, veio ${value}.`);
      cy.wait(1_000);
      cy.intercept('GET', '**/api/reports/cash-forecast*').as('forecast');
      cy.get('[data-cy=forecast-refresh]').click();
      cy.wait('@forecast');
      waitForBalance(month, expected, attempts - 1);
    });
  };

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('parte do saldo informado e desconta os lancamentos seguintes no mes deles', () => {
    const account = `E2E Saldo ${stamp}`;
    const row = () => cy.contains('[data-cy=account-row]', account);

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-scope]').select('Business');
    cy.get('[data-cy=account-submit]').click();
    row().find('[data-cy=account-balance]').should('contain', '—');

    // Antes: saldo PJ projetado deste mes e do seguinte.
    openForecast();
    cy.get('[data-cy=forecast-balance-notice]').should('contain', 'sem saldo informado');
    readBalance(currentMonth).as('currentBefore');
    readBalance(nextMonth).as('nextBefore');

    // Saldo de R$ 1.000 hoje; um gasto de R$ 250 no mes que vem.
    cy.get('[data-cy=tab-accounts]').click();
    row().find('[data-cy=account-balance-edit]').should('have.text', 'informar').click();
    cy.get('[data-cy=account-balance-amount]').type('1000');
    cy.get('[data-cy=account-balance-date]').should('have.value', iso(today));
    cy.get('[data-cy=account-balance-save]').click();
    row().find('[data-cy=account-balance-value]').should('contain', '1.000,00').and('contain', `em ${pad(today.getDate())}/`);
    row().find('[data-cy=account-balance-edit]').should('have.text', 'alterar');

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);
    cy.get('[data-cy=tx-payment-method]').select('Pix');
    cy.get('[data-cy=tx-description]').type(`Aluguel ${stamp}`);
    cy.get('[data-cy=tx-category]').type(`Aluguel ${stamp}`);
    cy.get('[data-cy=tx-amount]').type('250');
    cy.get('[data-cy=tx-date]').clear().type(iso(nextMonthDay));
    cy.get('[data-cy=tx-submit]').click();
    cy.contains('[data-cy=tx-row]', `Aluguel ${stamp}`).should('exist');

    // Depois: este mes ganha os 1.000; o seguinte, 1.000 - 250.
    openForecast();
    cy.get<number>('@currentBefore').then((before) => waitForBalance(currentMonth, before + 1000));
    cy.get<number>('@nextBefore').then((before) => waitForBalance(nextMonth, before + 750));

    // Remover o saldo tira a conta da projecao (e nao deixa rastro para a proxima execucao).
    cy.get('[data-cy=tab-accounts]').click();
    row().find('[data-cy=account-balance-clear]').click();
    row().find('[data-cy=account-balance-value]').should('not.exist');
    openForecast();
    cy.get<number>('@currentBefore').then((before) => waitForBalance(currentMonth, before));
  });
});
