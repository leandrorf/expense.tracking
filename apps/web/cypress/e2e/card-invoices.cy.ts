// INV-7: faturas do cartao na SPA. Uma compra de hoje cai na fatura atual (aberta) e uma de dois
// meses atras numa fatura ja vencida; o pagamento abate a fatura vencida e a exclusao devolve o saldo.
describe('Faturas do cartao de credito', () => {
  // Nomes novos a cada tentativa: um retry nao pode achar a conta criada pela tentativa anterior.
  let stamp = 0;
  let accountName = '';
  let cardLabel = '';
  const today = new Date();
  const iso = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  // Dia 5 de dois meses atras: com fechamento dia 10 e vencimento dia 17, vence no proprio mes (ja passou).
  const past = new Date(today.getFullYear(), today.getMonth() - 2, 5);
  const pastLabel = `${String(past.getMonth() + 1).padStart(2, '0')}/${past.getFullYear()}`;

  beforeEach(() => {
    stamp = Date.now();
    accountName = `E2E Fatura ${stamp}`;
    cardLabel = `Cartao Fatura ${stamp}`;
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  const purchase = (description: string, amount: string, date: string, installments = 1) => {
    cy.get('[data-cy=tx-account]').select(accountName);
    cy.get('[data-cy=tx-payment-method]').select('CreditCard');
    cy.get('[data-cy=tx-card]').find('option').contains(cardLabel).then(($opt) => {
      cy.get('[data-cy=tx-card]').select($opt.val() as string);
    });
    cy.get('[data-cy=tx-description]').clear().type(description);
    cy.get('[data-cy=tx-category]').clear().type(`Fatura ${stamp}`);
    cy.get('[data-cy=tx-amount]').clear().type(amount);
    cy.get('[data-cy=tx-date]').clear().type(date);
    cy.get('[data-cy=tx-installments]').select(String(installments));
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');
  };

  // A fatura le a projecao do reporting, que recebe as compras alguns segundos depois: reabre a
  // tela de faturas ate a compra aparecer.
  const openInvoicesUntil = (text: string, attempts = 30) => {
    cy.get('[data-cy=cards-table]').contains('tr', cardLabel).find('[data-cy=card-invoices]').click();
    cy.get('[data-cy=invoice-month]').should('be.visible');
    cy.get('[data-cy=invoices]').then(($view) => {
      if ($view.text().includes(text)) return;
      if (attempts <= 1) throw new Error(`A fatura nao mostrou "${text}" (projecao atrasada?).`);
      cy.wait(1_000);
      cy.get('[data-cy=invoices-back]').click();
      openInvoicesUntil(text, attempts - 1);
    });
  };

  it('mostra a fatura atual, navega para uma vencida, paga e exclui o pagamento', () => {
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(accountName);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', accountName).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(accountName);
    cy.get('[data-cy=card-label]').type(cardLabel);
    cy.get('[data-cy=card-lastfour]').type('1111');
    cy.get('[data-cy=card-limit]').type('3000');
    cy.get('[data-cy=card-closing]').clear().type('10');
    cy.get('[data-cy=card-due]').clear().type('17');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', cardLabel).should('be.visible');

    cy.get('[data-cy=tab-transactions]').click();
    // A antiga primeiro: o feed e ordenado, entao quando a de hoje aparecer a antiga ja esta projetada.
    purchase(`Farmacia ${stamp}`, '80.00', iso(past));
    purchase(`Mercado ${stamp}`, '120.00', iso(today));
    // INV-5: compra em 3x; a lista de lancamentos mostra o valor total e "em 3x".
    purchase(`Notebook ${stamp}`, '300.00', iso(today), 3);
    cy.get('[data-cy=tx-table]').contains('tr', `Notebook ${stamp}`).should('contain', '300,00').and('contain', 'em 3x');

    // Fatura atual: aberta, com a compra de hoje.
    cy.get('[data-cy=tab-cards]').click();
    openInvoicesUntil(`Notebook ${stamp}`);
    cy.get('[data-cy=invoice-month]').should('contain', '(atual)');
    cy.get('[data-cy=invoice-status]').should('have.attr', 'data-status', 'Open');
    cy.get('[data-cy=invoice-items]').should('contain', `Mercado ${stamp}`).and('contain', 'à vista');
    cy.get('[data-cy=invoice-items]').contains('tr', `Notebook ${stamp}`).should('contain', '1/3').and('contain', '100,00');

    // Anterior: a de dois meses atras, vencida.
    cy.get('[data-cy=invoice-prev]').click();
    cy.get('[data-cy=invoice-month]').should('contain', pastLabel);
    cy.get('[data-cy=invoice-status]').should('have.attr', 'data-status', 'Overdue').and('contain', 'Vencida');
    cy.get('[data-cy=invoice-total]').should('contain', '80,00');

    // Pagamento parcial: continua vencida, com saldo.
    cy.get('[data-cy=invoice-pay]').click();
    cy.get('[data-cy=payment-amount]').should('have.value', '80.00').clear().type('30');
    cy.get('[data-cy=payment-account]').should('contain', accountName);
    cy.get('[data-cy=payment-submit]').click();
    cy.get('[data-cy=invoice-paid]', { timeout: 30_000 }).should('contain', '30,00');
    cy.get('[data-cy=invoice-remaining]').should('contain', '50,00');
    cy.get('[data-cy=invoice-status]').should('have.attr', 'data-status', 'Overdue');

    // O resto (valor sugerido = o que falta): paga.
    cy.get('[data-cy=invoice-pay]').click();
    cy.get('[data-cy=payment-amount]').should('have.value', '50.00');
    cy.get('[data-cy=payment-submit]').click();
    cy.get('[data-cy=invoice-status]', { timeout: 30_000 }).should('have.attr', 'data-status', 'Paid');
    cy.get('[data-cy=invoice-pay]').should('not.exist');
    cy.get('[data-cy=invoice-payment-row]').should('have.length', 2);

    // Excluir um pagamento devolve o saldo.
    cy.get('[data-cy=invoice-payment-row]').contains('tr', '50,00').find('[data-cy=invoice-payment-delete]').click();
    cy.get('[data-cy=invoice-payment-row]', { timeout: 30_000 }).should('have.length', 1);
    cy.get('[data-cy=invoice-status]').should('have.attr', 'data-status', 'Overdue');
    cy.get('[data-cy=invoice-remaining]').should('contain', '50,00');

    // As outras parcelas, uma em cada fatura seguinte.
    cy.get('[data-cy=invoice-next]').click();
    cy.get('[data-cy=invoice-month]').should('contain', '(atual)');
    cy.get('[data-cy=invoice-next]').click();
    cy.get('[data-cy=invoice-items]').contains('tr', `Notebook ${stamp}`).should('contain', '2/3');
    cy.get('[data-cy=invoice-next]').click();
    cy.get('[data-cy=invoice-items]').contains('tr', `Notebook ${stamp}`).should('contain', '3/3');
    cy.get('[data-cy=invoice-next]').should('be.disabled');

    // INV-10: limite 3.000 − em aberto (50 da vencida + 120 + 300 em 3x) = 2.530 disponíveis.
    cy.get('[data-cy=invoice-limit]').should('contain', '470,00');
    cy.get('[data-cy=invoice-available]').should('contain', '2.530,00');
    cy.get('[data-cy=invoices-back]').click();
    cy.get('[data-cy=cards-table]').contains('tr', cardLabel).find('[data-cy=card-available]').should('contain', '2.530,00');
  });
});
