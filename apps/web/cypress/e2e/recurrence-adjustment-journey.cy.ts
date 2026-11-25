// RJT-10 (ADR-0010): jornada do reajuste pela SPA ate a previsao. Um aluguel de R$ 1.000 que comecou ha
// um ano (dia 1), com reajuste anual de 5% neste mes, entra na previsao do mes que vem com o reajuste
// estimado (RJT-8): R$ 1.050, marcado como estimado. Confirmado o reajuste pendente, o mes que vem fica em
// R$ 1.050 (agora pela vigencia) e o lancamento da proxima ocorrencia vem
// pre-preenchido com R$ 1.050. Um valor renegociado de R$ 1.100 a partir de daqui a dois meses deixa o mes
// que vem com R$ 1.050 e o seguinte com R$ 1.100. O reajuste so e confirmavel a partir do mes do
// aniversario (ADR-0010, decisao 5): por isso o aniversario e este mes e a previsao e lida nos seguintes.
// O usuario demo acumula dados: a previsao e comparada por diferenca.
describe('Recorrencias (jornada do reajuste com previsao)', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const monthOf = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}`;
  const today = new Date();
  const next = monthOf(new Date(today.getFullYear(), today.getMonth() + 1, 1));
  const afterNext = monthOf(new Date(today.getFullYear(), today.getMonth() + 2, 1));
  const startedOn = `${today.getFullYear() - 1}-${pad(today.getMonth() + 1)}-01`;

  /** `R$ 1.234,56` → 1234.56 (o espaco do toLocaleString e nao separavel). */
  const parse = (text: string) => Number(text.replace(/[^\d,-]/g, '').replace(',', '.'));

  type Before = Record<string, number>;

  const readExpense = (month: string) =>
    cy
      .get(`[data-cy=forecast-row][data-month="${month}"]`)
      .then(($row) => parse($row.find('[data-cy=forecast-expense]').contents().first().text()));

  const openForecast = () => {
    cy.get('[data-cy=tab-forecast]').click();
    cy.get('[data-cy=forecast-scope]').select('personal');
    cy.get('[data-cy=forecast-table]').should('be.visible');
  };

  // A previsao le a projecao do reporting: recarrega ate a diferenca bater.
  const waitForDiff = (month: string, before: Before, expected: number, attempts = 30) => {
    readExpense(month).then((expense) => {
      if (Math.abs(expense - before[month] - expected) < 0.005) return;
      if (attempts <= 1) {
        throw new Error(`Previsao de ${month}: esperado ${expected} a mais de gasto, veio ${expense} contra ${before[month]}.`);
      }
      cy.wait(1_000);
      cy.intercept('GET', '**/api/reports/cash-forecast*').as('forecast');
      cy.get('[data-cy=forecast-refresh]').click();
      cy.wait('@forecast');
      waitForDiff(month, before, expected, attempts - 1);
    });
  };

  /** RJT-9: abre o sino ate o aviso aparecer (ou sumir) — a projecao do reporting leva um instante. */
  const waitForReminder = (title: string, present = true, attempts = 30) => {
    cy.get('body').then(($body) => {
      if ($body.find('[data-cy=reminders-panel]').length === 0) cy.get('[data-cy=reminders-bell]').click();
    });
    cy.get('[data-cy=reminders-panel]').then(($panel) => {
      if (($panel.find(`[data-cy=reminder-item]:contains("${title}")`).length > 0) === present) return;
      if (attempts <= 1) throw new Error(`O aviso "${title}" nao ${present ? 'apareceu' : 'sumiu'} (projecao atrasada?).`);
      cy.get('[data-cy=reminders-bell]').click(); // fecha
      cy.wait(1_000);
      waitForReminder(title, present, attempts - 1);
    });
  };

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('confirma o reajuste e a previsao e o lancamento usam o valor de cada mes', () => {
    const [account, rent] = [`E2E Jornada Reajuste ${stamp}`, `Aluguel reajuste ${stamp}`];
    const ruleRow = () => cy.contains('[data-cy=rec-row]', rent);

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    // Antes: o gasto PF previsto do mes que vem e do seguinte.
    const before: Before = {};
    openForecast();
    readExpense(next).then((v) => (before[next] = v));
    readExpense(afterNext).then((v) => (before[afterNext] = v));

    // Aluguel com reajuste anual de 5% neste mes, pela tela.
    cy.get('[data-cy=tab-recurrences]').click();
    cy.get('[data-cy=rec-account]').select(account);
    cy.get('[data-cy=rec-type]').select('Expense');
    cy.get('[data-cy=rec-payment-method]').select('Boleto');
    cy.get('[data-cy=rec-description]').type(rent);
    cy.get('[data-cy=rec-category]').type(rent);
    cy.get('[data-cy=rec-amount]').type('1000');
    cy.get('[data-cy=rec-starts-on]').clear().type(startedOn);
    cy.get('[data-cy=rec-adjust-kind]').select('Fixed');
    cy.get('[data-cy=rec-adjust-rate]').type('5');
    cy.get('[data-cy=rec-submit]').click();
    ruleRow().find('[data-cy=rec-adjust-pending]').should('exist');

    // RJT-8: sem o reajuste confirmado, a previsao ja usa o valor estimado e mostra a parte estimada.
    openForecast();
    cy.then(() => waitForDiff(next, before, 1050));
    cy.get(`[data-cy=forecast-row][data-month="${next}"] [data-cy=forecast-estimated]`).should('contain', 'reajuste estimado');

    // RJT-9: o sino avisa o reajuste pendente; o aviso abre os valores da regra, onde ele e confirmado
    // (sugestao de 5%): o valor de hoje e o da proxima passam a R$ 1.050, e o aviso sai.
    waitForReminder(`Reajuste de ${rent}`);
    cy.contains('[data-cy=reminder-item]', `Reajuste de ${rent}`)
      .should('have.attr', 'data-kind', 'RecurrenceAdjustment')
      .and('contain', '1.000,00')
      .and('contain', '1.050,00')
      .and('contain', '5%')
      .click();
    cy.get('[data-cy=rec-adjust-summary]').should('contain', '1.050,00');
    cy.get('[data-cy=rec-adjust-confirm]').click();
    cy.get('[data-cy=rec-notice]').should('contain', `Reajuste de ${rent} confirmado`);
    ruleRow().find('[data-cy=rec-value]').should('contain', '1.050,00');
    waitForReminder(`Reajuste de ${rent}`, false);
    cy.get('[data-cy=reminders-bell]').click(); // fecha

    // Valor renegociado a partir de daqui a dois meses.
    cy.get('[data-cy=rec-amount-value]').type('1100');
    cy.get('[data-cy=rec-amount-from]').type(afterNext);
    cy.get('[data-cy=rec-amount-save]').click();
    cy.get(`[data-cy=rec-amount-row][data-period=${afterNext}]`).should('contain', '1.100,00');

    // A previsao tem o valor de cada mes.
    openForecast();
    cy.then(() => waitForDiff(next, before, 1050));
    cy.then(() => waitForDiff(afterNext, before, 1100));

    // A proxima ocorrencia vem pre-preenchida com o valor reajustado.
    cy.get('[data-cy=tab-recurrences]').click();
    ruleRow().find('[data-cy=rec-launch-next]').click();
    cy.get('[data-cy=rec-launch-amount]').should('have.value', '1050');
    cy.get('[data-cy=rec-launch-form]').contains('button', 'Cancelar').click();

    ruleRow().find('[data-cy=rec-remove]').click();
    cy.contains('[data-cy=rec-row]', rent).should('not.exist');
  });
});
