// INV-12: fatura e previsao nas jornadas PF e PJ, ponta a ponta pela SPA. O usuario demo acumula
// lancamentos de outras execucoes, entao previsao e Resumo sao comparados por diferenca (antes/depois).
describe('Fatura e previsao de caixa (PF e PJ)', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const monthOf = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}`;
  const label = (month: string) => `${month.slice(5)}/${month.slice(0, 4)}`;
  const addMonths = (month: string, n: number) => {
    const [y, m] = month.split('-').map(Number);
    return monthOf(new Date(y, m - 1 + n, 1));
  };
  const today = new Date();

  /** `R$ 1.234,56` → 1234.56; "paga" → 0. */
  const parse = (text: string) => (/\d/.test(text) ? Number(text.replace(/[^\d,-]/g, '').replace(',', '.')) : 0);
  const near = (a: number, b: number) => Math.abs(a - b) < 0.001;

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  // ---- Cadastro e lancamento ----
  const createAccountAndCard = (account: string, card: string, scope: 'Personal' | 'Business', closing: number, due: number) => {
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-scope]').select(scope);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('5555');
    cy.get('[data-cy=card-limit]').type('10000');
    cy.get('[data-cy=card-closing]').clear().type(String(closing));
    cy.get('[data-cy=card-due]').clear().type(String(due));
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', card).should('be.visible');
  };

  const cardPurchase = (account: string, card: string, description: string, amount: string, date: string, installments = 1) => {
    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);
    cy.get('[data-cy=tx-payment-method]').select('CreditCard');
    cy.get('[data-cy=tx-card]').find('option').contains(card).then(($opt) => {
      cy.get('[data-cy=tx-card]').select($opt.val() as string);
    });
    cy.get('[data-cy=tx-description]').clear().type(description);
    cy.get('[data-cy=tx-category]').clear().type(`Jornada ${stamp}`);
    cy.get('[data-cy=tx-amount]').clear().type(amount);
    cy.get('[data-cy=tx-date]').clear().type(date);
    cy.get('[data-cy=tx-installments]').select(String(installments));
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');
  };

  // ---- Previsao ----
  type Row = { expense: number; invoices: number; remaining: number };

  const openForecast = (scope: 'personal' | 'business') => {
    cy.get('[data-cy=tab-forecast]').click();
    cy.get('[data-cy=forecast-scope]').select(scope);
    cy.get('[data-cy=forecast-row]').should('have.length', 6);
  };

  const readRow = (month: string) =>
    cy.get(`[data-cy=forecast-row][data-month="${month}"]`).then(($row): Row => ({
      expense: parse($row.find('[data-cy=forecast-expense]').text()),
      invoices: parse($row.find('[data-cy=forecast-invoices]').contents().first().text()),
      remaining: parse($row.find('[data-cy=forecast-invoices-remaining]').text()),
    }));

  // A previsao le a projecao do reporting: recarrega ate a linha bater.
  const waitForRow = (month: string, check: (row: Row) => boolean, attempts = 30) => {
    readRow(month).then((row) => {
      if (check(row)) return;
      if (attempts <= 1) throw new Error(`A previsao de ${month} nao chegou ao esperado: ${JSON.stringify(row)}.`);
      cy.wait(1_000);
      cy.intercept('GET', '**/api/reports/cash-forecast*').as('forecast');
      cy.get('[data-cy=forecast-refresh]').click();
      cy.wait('@forecast');
      waitForRow(month, check, attempts - 1);
    });
  };

  // ---- Faturas ----
  const openInvoices = (card: string) => {
    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=cards-table]').contains('tr', card).find('[data-cy=card-invoices]').click();
    cy.get('[data-cy=invoice-month]').should('be.visible');
  };

  /** Anda pelas setas ate a fatura do mes (`yyyy-MM`). */
  const goToInvoice = (month: string, attempts = 14) => {
    cy.get('[data-cy=invoice-month]').then(($h) => {
      const shown = $h.text().match(/(\d{2})\/(\d{4})/)!;
      const current = `${shown[2]}-${shown[1]}`;
      if (current === month) return;
      if (attempts <= 1) throw new Error(`Fatura ${month} nao encontrada.`);
      cy.get(current < month ? '[data-cy=invoice-next]' : '[data-cy=invoice-prev]').click();
      goToInvoice(month, attempts - 1);
    });
  };

  // ---- Resumo ----
  const readSummaryScope = (scopeLabel: string) => {
    cy.get('[data-cy=tab-summary]').click();
    cy.get('[data-cy=summary-stats]').should('be.visible');
    return cy.get('[data-cy=by-scope]').then(($block) => {
      const item = $block.find('li').filter((_, li) => li.textContent?.includes(scopeLabel) ?? false);
      return item.length ? parse(item.find('strong').text()) : 0;
    });
  };

  const waitForSummaryScope = (scopeLabel: string, expected: number, attempts = 30) => {
    readSummaryScope(scopeLabel).then((value) => {
      if (near(value, expected)) return;
      if (attempts <= 1) throw new Error(`Resumo ${scopeLabel} = ${value}, esperado ${expected}.`);
      cy.wait(1_000);
      cy.get('[data-cy=summary-apply]').click();
      waitForSummaryScope(scopeLabel, expected, attempts - 1);
    });
  };

  it('PF: compra a vista e em 3x caem nas faturas certas e na previsao pelo vencimento', () => {
    const account = `E2E Jornada PF ${stamp}`;
    const card = `Cartao PF ${stamp}`;
    // Fecha dia 10 e vence dia 17: compra de hoje vence neste mes (ate o dia 10) ou no seguinte.
    const due0 = monthOf(new Date(today.getFullYear(), today.getMonth() + (today.getDate() <= 10 ? 0 : 1), 1));
    const [due1, due2] = [addMonths(due0, 1), addMonths(due0, 2)];
    createAccountAndCard(account, card, 'Personal', 10, 17);

    openForecast('personal');
    readRow(monthOf(today)).as('nowBefore');
    readRow(due0).as('due0Before');
    readRow(due1).as('due1Before');
    readRow(due2).as('due2Before');

    cardPurchase(account, card, `A vista ${stamp}`, '50', iso(today));
    cardPurchase(account, card, `Em 3x ${stamp}`, '300', iso(today), 3);

    // Previsao PF: 50 + 100 na primeira fatura, 100 em cada uma das duas seguintes.
    openForecast('personal');
    cy.get<Row>('@due0Before').then((b) => waitForRow(due0, (r) => near(r.invoices - b.invoices, 150)));
    cy.get<Row>('@due1Before').then((b) => waitForRow(due1, (r) => near(r.invoices - b.invoices, 100)));
    cy.get<Row>('@due2Before').then((b) => waitForRow(due2, (r) => near(r.invoices - b.invoices, 100)));
    // A compra no cartao nao e saida no mes em que foi feita.
    cy.get<Row>('@nowBefore').then((b) => readRow(monthOf(today)).its('expense').should('eq', b.expense));

    // Faturas: a vista + 1/3 na primeira; 2/3 e 3/3 nas seguintes.
    openInvoices(card);
    goToInvoice(due0);
    cy.get('[data-cy=invoice-total]').should('contain', '150,00');
    cy.get('[data-cy=invoice-items]').should('contain', 'à vista').and('contain', '1/3');
    goToInvoice(due1);
    cy.get('[data-cy=invoice-items]').contains('tr', `Em 3x ${stamp}`).should('contain', '2/3').and('contain', '100,00');
    goToInvoice(due2);
    cy.get('[data-cy=invoice-items]').contains('tr', `Em 3x ${stamp}`).should('contain', '3/3');
    cy.get('[data-cy=invoice-next]').should('be.disabled');
  });

  it('PJ: pagar a fatura fechada a deixa paga, tira da previsao a pagar e nao cria gasto', () => {
    const account = `E2E Jornada PJ ${stamp}`;
    const card = `Cartao PJ ${stamp}`;
    // Uma fatura ja fechada que ainda nao venceu: fecha ontem e vence dia 28 (ou, nos dias 1 e
    // 29 a 31, fecha dia 28 e vence dia 5 do mes seguinte). A compra e no dia do fechamento.
    const day = today.getDate();
    const sameMonth = day >= 2 && day <= 28;
    const closing = sameMonth ? day - 1 : 28;
    const dueDay = sameMonth ? 28 : 5;
    const purchaseDate = sameMonth
      ? new Date(today.getFullYear(), today.getMonth(), day - 1)
      : new Date(today.getFullYear(), today.getMonth() - (day === 1 ? 1 : 0), 28);
    const dueMonth = monthOf(
      new Date(purchaseDate.getFullYear(), purchaseDate.getMonth() + (dueDay > closing ? 0 : 1), 1),
    );
    createAccountAndCard(account, card, 'Business', closing, dueDay);

    readSummaryScope('Empresa (PJ)').as('pjBefore');
    cardPurchase(account, card, `Fornecedor ${stamp}`, '456.78', iso(purchaseDate));
    cy.get<number>('@pjBefore').then((before) => waitForSummaryScope('Empresa (PJ)', before + 456.78));

    openForecast('business');
    readRow(dueMonth).as('pjRowUnpaid');

    // Fatura fechada (nao vencida): registra o pagamento do total sugerido e ela fica paga.
    openInvoices(card);
    goToInvoice(dueMonth);
    cy.get('[data-cy=invoice-month]').should('contain', label(dueMonth));
    cy.get('[data-cy=invoice-status]').should('have.attr', 'data-status', 'Closed').and('contain', 'Fechada');
    cy.get('[data-cy=invoice-pay]').click();
    cy.get('[data-cy=payment-amount]').should('have.value', '456.78');
    cy.get('[data-cy=payment-account]').find('option:selected').should('contain', account);
    cy.get('[data-cy=payment-submit]').click();
    cy.get('[data-cy=invoice-status]', { timeout: 30_000 }).should('have.attr', 'data-status', 'Paid').and('contain', 'Paga');
    cy.get('[data-cy=invoice-remaining]').should('contain', '0,00');

    // Previsao PJ: a fatura continua no total do mes, mas sai do "a pagar".
    openForecast('business');
    cy.get<Row>('@pjRowUnpaid').then((unpaid) =>
      waitForRow(dueMonth, (r) => near(r.invoices, unpaid.invoices) && near(unpaid.remaining - r.remaining, 456.78)),
    );

    // Resumo PJ: o pagamento nao e gasto; o PJ fica so com a compra.
    cy.get<number>('@pjBefore').then((before) => {
      readSummaryScope('Empresa (PJ)').then((after) => expect(after).to.be.closeTo(before + 456.78, 0.001));
    });
  });
});
