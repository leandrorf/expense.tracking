// NTF-3 (ADR-0009): o sino de avisos. Um cartao com uma fatura vencida sem pagamento e um aluguel
// recorrente que vence amanha viram avisos; cada um leva aonde se resolve (a fatura certa na aba
// Cartoes; a aba Recorrencias). O usuario demo acumula dados, entao os avisos sao achados pelo titulo.
describe('Avisos de vencimento', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const addDays = (d: Date, days: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + days);
  const today = new Date();

  // Compra no dia do fechamento, vencimento 5 dias depois: escolhe uma compra de 15 a 25 dias atras em
  // que os dois dias existam em todo mes (1 a 28). A fatura venceu ha 10 a 20 dias: "vencida".
  const purchase = [15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25]
    .map((k) => addDays(today, -k))
    .find((p) => p.getDate() <= 28 && addDays(p, 5).getDate() <= 28)!;
  const dueDate = addDays(purchase, 5);
  const dueMonth = `${pad(dueDate.getMonth() + 1)}/${dueDate.getFullYear()}`;
  const tomorrow = addDays(today, 1);

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  /** Abre o painel ate o aviso aparecer (a projecao do reporting leva um instante). */
  const waitForReminder = (title: string, attempts = 30) => {
    cy.get('body').then(($body) => {
      if ($body.find('[data-cy=reminders-panel]').length === 0) cy.get('[data-cy=reminders-bell]').click();
    });
    cy.get('[data-cy=reminders-panel]').then(($panel) => {
      if ($panel.find(`[data-cy=reminder-item]:contains("${title}")`).length > 0) return;
      if (attempts <= 1) throw new Error(`O aviso "${title}" nao apareceu (projecao atrasada?).`);
      cy.get('[data-cy=reminders-bell]').click(); // fecha
      cy.wait(1_000);
      waitForReminder(title, attempts - 1);
    });
  };

  it('mostra a fatura vencida e o aluguel de amanha, e leva cada um aonde se resolve', () => {
    const [account, card, rent] = [`E2E Avisos ${stamp}`, `Cartao Aviso ${stamp}`, `Aluguel Aviso ${stamp}`];

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('4321');
    cy.get('[data-cy=card-limit]').type('5000');
    cy.get('[data-cy=card-closing]').clear().type(String(purchase.getDate()));
    cy.get('[data-cy=card-due]').clear().type(String(dueDate.getDate()));
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', card).should('be.visible');

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);
    cy.get('[data-cy=tx-payment-method]').select('CreditCard');
    cy.get('[data-cy=tx-card]').find('option').contains(card).then(($opt) => {
      cy.get('[data-cy=tx-card]').select($opt.val() as string);
    });
    cy.get('[data-cy=tx-description]').type(`Compra Aviso ${stamp}`);
    cy.get('[data-cy=tx-category]').type(`Aviso ${stamp}`);
    cy.get('[data-cy=tx-amount]').type('230');
    cy.get('[data-cy=tx-date]').clear().type(iso(purchase));
    cy.get('[data-cy=tx-submit]').click();
    cy.contains('[data-cy=tx-row]', `Compra Aviso ${stamp}`).should('exist');

    cy.get('[data-cy=tab-recurrences]').click();
    cy.get('[data-cy=rec-account]').select(account);
    cy.get('[data-cy=rec-payment-method]').select('Boleto');
    cy.get('[data-cy=rec-description]').type(rent);
    cy.get('[data-cy=rec-category]').type(`Moradia ${stamp}`);
    cy.get('[data-cy=rec-amount]').type('1800');
    cy.get('[data-cy=rec-starts-on]').clear().type(iso(tomorrow));
    cy.get('[data-cy=rec-submit]').click();
    cy.contains('[data-cy=rec-row]', rent).should('exist');

    // O sino mostra os dois, a fatura como vencida (em destaque) e o aluguel como "vence em".
    waitForReminder(rent);
    waitForReminder(`Fatura ${card}`);
    cy.get('[data-cy=reminders-count]').should('exist');
    cy.get('[data-cy=reminders-bell]').should('have.class', 'has-overdue');
    cy.contains('[data-cy=reminder-item]', `Fatura ${card}`)
      .should('have.attr', 'data-kind', 'InvoiceOverdue')
      .and('contain', 'venceu em')
      .and('contain', '230,00');
    cy.contains('[data-cy=reminder-item]', rent).should('have.attr', 'data-kind', 'RecurrenceDue').and('contain', '1.800,00');

    // A fatura abre na aba Cartoes, no mes dela (nao na atual).
    cy.contains('[data-cy=reminder-item]', `Fatura ${card}`).click();
    cy.get('[data-cy=reminders-panel]').should('not.exist');
    cy.get('[data-cy=invoice-month]').should('contain', dueMonth);
    cy.get('[data-cy=invoice-status]').should('contain', 'Vencida');

    // O aluguel leva a Recorrencias.
    cy.get('[data-cy=reminders-bell]').click();
    cy.contains('[data-cy=reminder-item]', rent).click();
    cy.get('[data-cy=recurrences]').should('be.visible');
    cy.contains('[data-cy=rec-row]', rent).should('exist');

    // Limpeza: a regra sai (o cartao e a compra ficam, como nos outros specs).
    cy.contains('[data-cy=rec-row]', rent).find('[data-cy=rec-remove]').click();
  });

  it('dispensar esconde o aviso, inclusive depois de recarregar a pagina', () => {
    // NTF-5 (ADR-0009, decisao 5).
    const [account, gym] = [`E2E Dispensa ${stamp}`, `Academia Aviso ${stamp}`];
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-recurrences]').click();
    cy.get('[data-cy=rec-account]').select(account);
    cy.get('[data-cy=rec-payment-method]').select('Pix');
    cy.get('[data-cy=rec-description]').type(gym);
    cy.get('[data-cy=rec-category]').type(`Saude ${stamp}`);
    cy.get('[data-cy=rec-amount]').type('130');
    cy.get('[data-cy=rec-starts-on]').clear().type(iso(tomorrow));
    cy.get('[data-cy=rec-submit]').click();
    cy.contains('[data-cy=rec-row]', gym).should('exist');

    waitForReminder(gym);
    cy.contains('[data-cy=reminder-row]', gym).find('[data-cy=reminder-dismiss]').click();
    cy.contains('[data-cy=reminder-item]', gym).should('not.exist');

    // A dispensa e gravada no servidor: recarregar a pagina nao traz o aviso de volta.
    cy.intercept('GET', '**/api/reports/reminders').as('reminders');
    cy.reload();
    cy.get('[data-cy=session-active]').should('be.visible');
    cy.wait('@reminders');
    cy.get('[data-cy=reminders-bell]').click();
    cy.wait('@reminders');
    cy.get('[data-cy=reminders-panel]').should('be.visible');
    cy.contains('[data-cy=reminder-item]', gym).should('not.exist');

    cy.get('[data-cy=reminders-bell]').click();
    cy.get('[data-cy=tab-recurrences]').click();
    cy.contains('[data-cy=rec-row]', gym).find('[data-cy=rec-remove]').click();
  });
});
