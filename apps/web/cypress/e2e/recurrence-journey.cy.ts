// REC-7 (ADR-0008): jornada das recorrencias pela SPA. Numa conta PF com cartao (fecha 10, vence 17),
// cadastra salario (dia 5 do mes que vem), aluguel (hoje) e uma assinatura no cartao (hoje). A previsao
// conta os tres nos meses certos sem nenhum lancamento; lancar o aluguel com outro valor faz ele contar
// uma vez so, pelo valor real, e aparecer no Resumo e no orcamento; pular o salario tira ele do mes.
// O usuario demo acumula dados: a previsao e comparada por diferenca e as categorias sao unicas.
describe('Recorrencias (jornada com previsao)', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const monthOf = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}`;
  const today = new Date();
  const current = monthOf(today);
  const next = monthOf(new Date(today.getFullYear(), today.getMonth() + 1, 1));
  const salaryDay = new Date(today.getFullYear(), today.getMonth() + 1, 5);
  // A assinatura de hoje cai na fatura deste mes ate o dia 10 (fechamento); depois, na do mes que vem.
  const subscriptionInCurrentInvoice = today.getDate() <= 10;

  /** `R$ 1.234,56` → 1234.56 (o espaco do toLocaleString e nao separavel). */
  const parse = (text: string) => Number(text.replace(/[^\d,-]/g, '').replace(',', '.'));

  type Cells = { income: number; expense: number; invoices: number };

  const readRow = (month: string) =>
    cy.get(`[data-cy=forecast-row][data-month="${month}"]`).then(($row) => ({
      income: parse($row.find('[data-cy=forecast-income]').contents().first().text()),
      expense: parse($row.find('[data-cy=forecast-expense]').contents().first().text()),
      invoices: parse($row.find('[data-cy=forecast-invoices]').contents().first().text()),
    }));

  const openForecast = () => {
    cy.get('[data-cy=tab-forecast]').click();
    cy.get('[data-cy=forecast-scope]').select('personal');
    cy.get('[data-cy=forecast-table]').should('be.visible');
  };

  const near = (a: number, b: number) => Math.abs(a - b) < 0.005;

  // A previsao le a projecao do reporting: recarrega ate a diferenca bater.
  const waitForDiff = (month: string, before: Cells, expected: Partial<Cells>, attempts = 30) => {
    readRow(month).then((row) => {
      const ok = (Object.keys(expected) as (keyof Cells)[]).every((k) => near(row[k] - before[k], expected[k]!));
      if (ok) return;
      if (attempts <= 1) {
        throw new Error(`Previsao de ${month}: esperado ${JSON.stringify(expected)} a mais, veio ${JSON.stringify(row)} contra ${JSON.stringify(before)}.`);
      }
      cy.wait(1_000);
      cy.intercept('GET', '**/api/reports/cash-forecast*').as('forecast');
      cy.get('[data-cy=forecast-refresh]').click();
      cy.wait('@forecast');
      waitForDiff(month, before, expected, attempts - 1);
    });
  };

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('preve salario, aluguel e assinatura; lancar e pular ajustam a previsao, o Resumo e o orcamento', () => {
    const [account, card] = [`E2E Jornada Rec ${stamp}`, `Cartao Rec ${stamp}`];
    const [salary, rent, streaming] = [`Salario ${stamp}`, `Aluguel ${stamp}`, `Streaming ${stamp}`];
    const ruleRow = (description: string) => cy.contains('[data-cy=rec-row]', description);

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('5151');
    cy.get('[data-cy=card-limit]').type('5000');
    cy.get('[data-cy=card-closing]').clear().type('10');
    cy.get('[data-cy=card-due]').clear().type('17');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', card).should('be.visible');

    cy.get('[data-cy=tab-budgets]').click();
    cy.get('[data-cy=budget-category]').type(rent);
    cy.get('[data-cy=budget-scope]').select('Personal');
    cy.get('[data-cy=budget-amount]').type('2000');
    cy.get('[data-cy=budget-submit]').click();
    cy.get(`[data-cy=budgets-personal] [data-cy=budget-row][data-category="${rent}"]`).should('exist');

    // Antes: a previsao PF deste mes e do que vem.
    openForecast();
    readRow(current).as('currentBefore');
    readRow(next).as('nextBefore');

    // Tres regras pela tela.
    const addRule = (opts: { type: 'Expense' | 'Income'; method: string; description: string; amount: string; startsOn: Date; useCard?: boolean }) => {
      cy.get('[data-cy=rec-account]').select(account);
      cy.get('[data-cy=rec-type]').select(opts.type);
      cy.get('[data-cy=rec-payment-method]').select(opts.method);
      if (opts.useCard) {
        cy.get('[data-cy=rec-card]').find('option').contains(card).then(($opt) => {
          cy.get('[data-cy=rec-card]').select($opt.val() as string);
        });
      }
      cy.get('[data-cy=rec-description]').clear().type(opts.description);
      cy.get('[data-cy=rec-category]').clear().type(opts.description);
      cy.get('[data-cy=rec-amount]').clear().type(opts.amount);
      cy.get('[data-cy=rec-starts-on]').clear().type(iso(opts.startsOn));
      cy.get('[data-cy=rec-submit]').click();
      ruleRow(opts.description).should('exist');
    };
    cy.get('[data-cy=tab-recurrences]').click();
    addRule({ type: 'Income', method: 'Pix', description: salary, amount: '3000', startsOn: salaryDay });
    addRule({ type: 'Expense', method: 'Boleto', description: rent, amount: '1500', startsOn: today });
    addRule({ type: 'Expense', method: 'CreditCard', description: streaming, amount: '50', startsOn: today, useCard: true });

    // Sem nenhum lancamento, a previsao ja conta os tres.
    openForecast();
    cy.get<Cells>('@currentBefore').then((before) =>
      waitForDiff(current, before, { income: 0, expense: 1500, invoices: subscriptionInCurrentInvoice ? 50 : 0 }));
    cy.get<Cells>('@nextBefore').then((before) => waitForDiff(next, before, { income: 3000, expense: 1500, invoices: 50 }));
    cy.get(`[data-cy=forecast-row][data-month="${current}"] [data-cy=forecast-expense-recurring]`).should('contain', 'previsto');
    cy.get(`[data-cy=forecast-row][data-month="${next}"] [data-cy=forecast-income-recurring]`).should('contain', 'previsto');

    // Lancar o aluguel deste mes por R$ 1.650: conta uma vez so, pelo valor real.
    cy.get('[data-cy=tab-recurrences]').click();
    ruleRow(rent).find('[data-cy=rec-launch-next]').click();
    cy.get('[data-cy=rec-launch-amount]').clear().type('1650');
    cy.get('[data-cy=rec-launch-save]').click();
    cy.get('[data-cy=rec-notice]').should('contain', `Lançado: ${rent}`);

    // Pular o salario do mes que vem: ele some daquele mes.
    ruleRow(salary).find('[data-cy=rec-skip-next]').click();
    cy.get('[data-cy=rec-notice]').should('contain', `Pulado: ${salary}`);

    openForecast();
    cy.get<Cells>('@currentBefore').then((before) => waitForDiff(current, before, { expense: 1650 }));
    cy.get<Cells>('@nextBefore').then((before) => waitForDiff(next, before, { income: 0, expense: 1500 }));

    // O aluguel lancado e um lancamento comum: Resumo e orcamento o veem.
    cy.get('[data-cy=tab-summary]').click();
    cy.waitForSummary('[data-cy=by-category]', rent);
    cy.get('[data-cy=by-category]').contains('li', rent).should('contain', '1.650,00');
    cy.get('[data-cy=by-category]').should('not.contain', salary).and('not.contain', streaming);
    cy.get('[data-cy=tab-budgets]').click();
    cy.waitForBudget('personal', rent, '1.650,00');

    // Limpeza: as regras saem (o lancamento do aluguel fica).
    cy.get('[data-cy=tab-recurrences]').click();
    for (const description of [salary, rent, streaming]) {
      ruleRow(description).find('[data-cy=rec-remove]').click();
      cy.contains('[data-cy=rec-row]', description).should('not.exist');
    }
  });
});
