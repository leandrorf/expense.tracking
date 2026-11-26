// RCN-9 (ADR-0011): jornada da conferencia da fatura com o arquivo do banco. Num cartao novo, lancamos um
// notebook em 3x, um mercado e uma padaria. O OFX da fatura do banco traz a parcela 1/3 do notebook, o mercado,
// uma farmacia que faltou lancar e um IOF (e nao traz a padaria). A conciliacao mostra a parcela e o mercado
// conferidos, a padaria so no app e as duas linhas so no banco; o atalho "editar" abre a padaria na aba
// Lancamentos. Importar o que falta e excluir a padaria zeram a diferenca, e a fatura fica conferida.
describe('Fatura: conferencia com o arquivo do banco (jornada)', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const today = new Date();
  const iso = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;
  const ofxDay = iso.replaceAll('-', '');

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('casa a parcela e a compra, importa o que falta, exclui o que sobra e confere', () => {
    const [account, card] = [`E2E Jornada Conf ${stamp}`, `Cartao Jornada ${stamp}`];
    const [notebook, market, bakery] = [`Notebook ${stamp}`, `Mercado ${stamp}`, `Padaria ${stamp}`];
    const pharmacy = `FARMACIA ${stamp}`;
    const ofx = [
      'OFXHEADER:100', 'DATA:OFXSGML', 'VERSION:102', '',
      '<OFX><CREDITCARDMSGSRSV1><CCSTMTTRNRS><CCSTMTRS><CURDEF>BRL<BANKTRANLIST>',
      `<STMTTRN><DTPOSTED>${ofxDay}<TRNAMT>-100.00<FITID>J${stamp}-1<MEMO>NOTEBOOK LOJA PARCELA 01/03</STMTTRN>`,
      `<STMTTRN><DTPOSTED>${ofxDay}<TRNAMT>-80.00<FITID>J${stamp}-2<MEMO>SUPERMERCADO ${stamp}</STMTTRN>`,
      `<STMTTRN><DTPOSTED>${ofxDay}<TRNAMT>-39.40<FITID>J${stamp}-3<MEMO>${pharmacy}</STMTTRN>`,
      `<STMTTRN><DTPOSTED>${ofxDay}<TRNAMT>-0.98<FITID>J${stamp}-4<MEMO>IOF COMPRA INTERNACIONAL</STMTTRN>`,
      '</BANKTRANLIST><LEDGERBAL><BALAMT>-220.38<DTASOF>' + ofxDay + '</LEDGERBAL></CCSTMTRS></CCSTMTTRNRS></CREDITCARDMSGSRSV1></OFX>',
    ].join('\r\n');

    const group = (name: string) => cy.get(`[data-cy=reconciliation-group][data-group=${name}]`);
    const openInvoice = () => {
      cy.get('[data-cy=tab-cards]').click();
      cy.contains('[data-cy=cards-table] tr', card).find('[data-cy=card-invoices]').click();
    };
    const uploadBankFile = () => {
      cy.get('[data-cy=invoice-reconcile]').click();
      cy.get('[data-cy=reconciliation-file]').selectFile({ contents: Cypress.Buffer.from(ofx), fileName: 'fatura.ofx' });
      cy.get('[data-cy=reconciliation-file-total]').should('contain', '220,38');
    };

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('9191');
    cy.get('[data-cy=card-limit]').type('5000');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', card).should('be.visible');

    // Tres compras no cartao, hoje (caem na fatura atual).
    cy.get('[data-cy=tab-transactions]').click();
    const buy = (description: string, amount: string, installments?: string) => {
      cy.get('[data-cy=tx-account]').select(account);
      cy.get('[data-cy=tx-payment-method]').select('CreditCard');
      cy.get('[data-cy=tx-card]').find('option').contains(card).then(($o) => cy.get('[data-cy=tx-card]').select($o.val() as string));
      cy.get('[data-cy=tx-description]').clear().type(description);
      cy.get('[data-cy=tx-category]').clear().type(`Compras ${stamp}`);
      cy.get('[data-cy=tx-amount]').clear().type(amount);
      cy.get('[data-cy=tx-date]').clear().type(iso);
      if (installments) cy.get('[data-cy=tx-installments]').select(installments);
      cy.get('[data-cy=tx-submit]').click();
      cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');
    };
    buy(notebook, '300', '3');
    buy(market, '80');
    buy(bakery, '25');

    // A fatura atual recebe as tres (a projecao leva um instante): 100 + 80 + 25.
    const waitTotal = (attempts = 20) => {
      openInvoice();
      cy.get('[data-cy=invoice-total]').invoke('text').then((text) => {
        if (text.includes('205,00')) return;
        if (attempts <= 1) throw new Error(`A fatura nao chegou a R$ 205,00 (${text}).`);
        cy.wait(1000);
        cy.get('[data-cy=invoices-back]').click();
        waitTotal(attempts - 1);
      });
    };
    waitTotal();

    // O arquivo do banco: parcela e mercado conferidos, padaria so no app, farmacia e IOF so no banco.
    uploadBankFile();
    group('Matched').find('[data-cy=reconciliation-entry]').should('have.length', 2);
    group('Matched').should('contain', 'parcela 1/3').and('contain', `SUPERMERCADO ${stamp}`);
    group('OnlyInApp').should('contain', bakery);
    group('OnlyInBank').find('[data-cy=reconciliation-entry]').should('have.length', 2);
    group('OnlyInBank').should('contain', pharmacy).and('contain', 'IOF');
    // RCN-7: o IOF vem marcado como encargo do banco.
    group('OnlyInBank').find('[data-cy=reconciliation-charge]').should('have.length', 1);
    cy.get('[data-cy=reconciliation-file-difference]').should('contain', '15,38');

    // O atalho "editar" abre a padaria na aba Lancamentos.
    group('OnlyInApp').find('[data-cy=reconciliation-edit]').click();
    cy.get('[data-cy=tx-description]').should('have.value', bakery);
    cy.get('[data-cy=tx-cancel]').click();

    // De volta a fatura: importa o que falta e exclui a padaria; a diferenca zera.
    openInvoice();
    uploadBankFile();
    cy.get('[data-cy=reconciliation-category]').clear().type(`Cartao ${stamp}`);
    cy.get('[data-cy=reconciliation-import]').click();
    cy.get('[data-cy=reconciliation-notice]', { timeout: 30_000 }).should('contain', '2 compras importadas');
    group('OnlyInBank').should('not.exist');
    group('OnlyInApp').find('[data-cy=reconciliation-delete]').click();
    cy.get('[data-cy=reconciliation-notice]', { timeout: 30_000 }).should('contain', 'Lançamento excluído');
    group('OnlyInApp').should('not.exist');
    cy.get('[data-cy=reconciliation-file-difference]').should('contain', '0,00');
    group('Matched').find('[data-cy=reconciliation-entry]').should('have.length', 4);

    // Conferida, sem diferenca: o total do banco ja vem do arquivo.
    cy.get('[data-cy=reconciliation-bank-total]').should('have.value', '220.38');
    cy.get('[data-cy=reconciliation-mark]').click();
    cy.get('[data-cy=reconciliation-notice]').should('contain', 'Fatura conferida.');
    cy.get('[data-cy=invoice-reconciled]').should('contain', 'Conferida').and('not.contain', 'dif.');
    cy.get('[data-cy=invoice-total]').should('contain', '220,38');
  });
});
