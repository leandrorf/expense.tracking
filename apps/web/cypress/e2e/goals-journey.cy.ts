// MET-11 (ADR-0013): jornada da meta e do objetivo. Numa conta PF nova, uma receita deixa o resultado do mes
// folgado; a meta de guardar 1.000 fica "dentro" com o que sobra; um gasto que deixa so 100 para gastar muda a
// situacao para "atencao". Depois um objetivo recebe um aporte, mostra o progresso e o ritmo, e o Resumo nao muda.
// O usuario demo tem outros lancamentos PF no mes: os valores sao calculados a partir do resultado lido na tela.
describe('Metas: jornada da meta do mes e do objetivo', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const today = new Date();
  const iso = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;
  /** `R$ 1.234,56` / `-R$ 1.234,56` → numero (o espaco do toLocaleString e nao separavel). */
  const parse = (text: string) => Number(text.replace(/[^\d,-]/g, '').replace(',', '.'));
  const pf = () => cy.get('[data-cy=goal-scope-personal]');

  /** A meta le a previsao, que le a projecao: recarrega ate o campo bater. */
  const waitForGoal = (field: string, expected: number, attempts = 30) => {
    pf().find(`[data-cy=${field}]`).invoke('text').then((text) => {
      if (Math.abs(parse(text) - expected) < 0.005) return;
      if (attempts <= 1) throw new Error(`Meta PF: ${field} esperado ${expected}, veio ${text}.`);
      cy.wait(1_000);
      cy.intercept('GET', '**/api/goals/monthly/status*').as('goalStatus');
      cy.get('[data-cy=goal-month-refresh]').click();
      cy.wait('@goalStatus');
      waitForGoal(field, expected, attempts - 1);
    });
  };

  /** Abre o Resumo e le os totais depois da resposta do /reports/summary. */
  const readSummary = () => {
    cy.get('[data-cy=tab-summary]').click();
    cy.get('[data-cy=summary-stats]').should('be.visible');
    cy.intercept('GET', '**/api/reports/summary*').as('summary');
    cy.get('[data-cy=summary-apply]').click();
    cy.wait('@summary');
    return cy.get('[data-cy=summary-stats]').invoke('text');
  };

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('a meta muda com cada lancamento, o objetivo com cada aporte, e o Resumo nao ve o aporte', () => {
    const account = `E2E Metas ${stamp}`;
    const objective = `Reserva ${stamp}`;

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    const launch = (type: 'Expense' | 'Income', description: string, amount: number) => {
      cy.get('[data-cy=tab-transactions]').click();
      cy.get('[data-cy=tx-account]').select(account);
      cy.get('[data-cy=tx-type]').select(type);
      cy.get('[data-cy=tx-payment-method]').select('Pix');
      cy.get('[data-cy=tx-description]').clear().type(description);
      cy.get('[data-cy=tx-category]').clear().type(`Metas ${stamp}`);
      cy.get('[data-cy=tx-amount]').clear().type(amount.toFixed(2));
      cy.get('[data-cy=tx-date]').clear().type(iso);
      cy.get('[data-cy=tx-submit]').click();
      cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');
    };

    // A meta PF: guardar 1.000 a partir deste mes.
    cy.get('[data-cy=tab-goals]').click();
    pf().find('[data-cy=goal-edit-open]').click();
    pf().find('[data-cy=goal-kind]').select('SaveAmount');
    pf().find('[data-cy=goal-value]').clear().type('1000');
    pf().find('[data-cy=goal-save]').click();
    cy.get('[data-cy=goal-notice]').should('contain', 'Meta PF');

    pf().find('[data-cy=goal-result]').invoke('text').then((text) => {
      const before = parse(text);
      // Receita para o resultado ficar em pelo menos 6.000.
      const income = Math.ceil(Math.max(0, -before)) + 6000;
      const afterIncome = Math.round((before + income) * 100) / 100;
      launch('Income', `Salario ${stamp}`, income);

      cy.get('[data-cy=tab-goals]').click();
      waitForGoal('goal-result', afterIncome);
      waitForGoal('goal-can-spend', afterIncome - 1000);
      pf().should('have.attr', 'data-situation', 'Inside');

      // Um gasto que deixa so 100 para gastar: menos de 20% da meta, "atencao".
      const spending = Math.round((afterIncome - 1000 - 100) * 100) / 100;
      launch('Expense', `Compra grande ${stamp}`, spending);
      cy.get('[data-cy=tab-goals]').click();
      waitForGoal('goal-can-spend', 100);
      pf().should('have.attr', 'data-situation', 'Attention');
      pf().find('[data-cy=goal-situation]').should('contain', 'Atenção');
    });

    // O Resumo antes do aporte, ja com o gasto (o Resumo le a mesma projecao, mas busca de novo ao abrir).
    let summaryBefore = '';
    readSummary().then((text) => {
      summaryBefore = text;
    });

    // Objetivo de 3.000 com prazo daqui a 5 meses: um aporte de 600.
    cy.get('[data-cy=tab-goals]').click();
    const deadline = new Date(today.getFullYear(), today.getMonth() + 5, 1);
    cy.get('[data-cy=objective-scope]').select('personal');
    cy.get('[data-cy=objective-name]').type(objective);
    cy.get('[data-cy=objective-target]').type('3000');
    cy.get('[data-cy=objective-deadline]').type(`${deadline.getFullYear()}-${pad(deadline.getMonth() + 1)}`);
    cy.get('[data-cy=objective-submit]').click();
    const row = () => cy.get(`[data-cy=objective-row][data-name="${objective}"]`);
    row().find('[data-cy=contribution-open]').click();
    row().find('[data-cy=contribution-amount]').type('600');
    row().find('[data-cy=contribution-submit]').click();

    // Falta 2.400 em 6 meses (o atual e os 5 seguintes) = 400 por mes; ritmo 600 (criado hoje): no ritmo.
    row().find('[data-cy=objective-saved]').should('contain', '600,00');
    row().find('[data-cy=objective-missing]').should('contain', '2.400,00');
    row().find('[data-cy=objective-needed]').should('contain', '400,00');
    row().find('[data-cy=objective-pace]').should('contain', '600,00');
    row().should('have.attr', 'data-situation', 'OnTrack');
    row().find('[data-cy=objective-archive]').click();

    // Aporte nao e lancamento: o Resumo continua igual.
    readSummary().should((text) => expect(text).to.eq(summaryBefore));
  });
});
