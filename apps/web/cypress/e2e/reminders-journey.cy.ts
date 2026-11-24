// NTF-4 (ADR-0009): jornada "aviso → resolver → aviso some". Uma fatura vencida sem pagamento e um
// aluguel recorrente de amanha aparecem no sino; a fatura e paga pelo atalho, o aluguel e lancado pela
// aba Recorrencias, e os dois avisos somem (sem dispensar nada). Os avisos sao achados pelo titulo,
// unico por execucao, porque o usuario demo acumula dados.
describe('Avisos de vencimento (jornada: resolver)', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const addDays = (d: Date, days: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + days);
  const today = new Date();
  // Mesma escolha do reminders.cy.ts: compra no dia do fechamento, vencimento 5 dias depois, ja passado.
  const purchase = [15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25]
    .map((k) => addDays(today, -k))
    .find((p) => p.getDate() <= 28 && addDays(p, 5).getDate() <= 28)!;
  const dueDate = addDays(purchase, 5);
  const tomorrow = addDays(today, 1);

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  /** Reabre o painel ate o aviso aparecer (`present`) ou sumir; a projecao leva um instante. */
  const waitForReminder = (title: string, present: boolean, attempts = 30) => {
    cy.get('body').then(($body) => {
      if ($body.find('[data-cy=reminders-panel]').length > 0) cy.get('[data-cy=reminders-bell]').click(); // fecha
    });
    cy.get('[data-cy=reminders-bell]').click();
    cy.get('[data-cy=reminders-panel]').then(($panel) => {
      if (($panel.find(`[data-cy=reminder-item]:contains("${title}")`).length > 0) === present) return;
      if (attempts <= 1) throw new Error(`O aviso "${title}" ${present ? 'nao apareceu' : 'nao sumiu'} (projecao atrasada?).`);
      cy.wait(1_000);
      waitForReminder(title, present, attempts - 1);
    });
  };

  it('pagar a fatura e lancar o aluguel tiram os dois avisos', () => {
    const [account, card, rent] = [`E2E Avisos J ${stamp}`, `Cartao Aviso J ${stamp}`, `Aluguel Aviso J ${stamp}`];

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('8765');
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
    cy.get('[data-cy=tx-description]').type(`Compra Aviso J ${stamp}`);
    cy.get('[data-cy=tx-category]').type(`Aviso ${stamp}`);
    cy.get('[data-cy=tx-amount]').type('310');
    cy.get('[data-cy=tx-date]').clear().type(iso(purchase));
    cy.get('[data-cy=tx-submit]').click();
    cy.contains('[data-cy=tx-row]', `Compra Aviso J ${stamp}`).should('exist');

    cy.get('[data-cy=tab-recurrences]').click();
    cy.get('[data-cy=rec-account]').select(account);
    cy.get('[data-cy=rec-payment-method]').select('Boleto');
    cy.get('[data-cy=rec-description]').type(rent);
    cy.get('[data-cy=rec-category]').type(`Moradia ${stamp}`);
    cy.get('[data-cy=rec-amount]').type('1700');
    cy.get('[data-cy=rec-starts-on]').clear().type(iso(tomorrow));
    cy.get('[data-cy=rec-submit]').click();
    cy.contains('[data-cy=rec-row]', rent).should('exist');

    waitForReminder(`Fatura ${card}`, true);
    waitForReminder(rent, true);

    // Pagar a fatura pelo atalho: o valor sugerido e o que falta.
    cy.contains('[data-cy=reminder-item]', `Fatura ${card}`).click();
    cy.get('[data-cy=invoice-status]').should('contain', 'Vencida');
    cy.get('[data-cy=invoice-pay]').click();
    cy.get('[data-cy=payment-amount]').should('have.value', '310.00');
    cy.get('[data-cy=payment-submit]').click();
    cy.get('[data-cy=invoice-status]').should('contain', 'Paga');

    // Lancar o aluguel pelo atalho, na aba Recorrencias.
    waitForReminder(rent, true);
    cy.contains('[data-cy=reminder-item]', rent).click();
    cy.contains('[data-cy=rec-row]', rent).find('[data-cy=rec-launch-next]').click();
    cy.get('[data-cy=rec-launch-save]').click();
    cy.get('[data-cy=rec-notice]').should('contain', `Lançado: ${rent}`);

    // Resolvidos, os dois avisos somem sem dispensar nada.
    waitForReminder(`Fatura ${card}`, false);
    waitForReminder(rent, false);

    // Limpeza: a regra sai (o lancamento do aluguel fica).
    cy.get('[data-cy=reminders-bell]').click();
    cy.get('[data-cy=tab-recurrences]').click();
    cy.contains('[data-cy=rec-row]', rent).find('[data-cy=rec-remove]').click();
  });
});
