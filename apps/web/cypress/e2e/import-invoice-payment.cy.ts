// INV-11: no extrato de conta importado, a linha "PAG FATURA" vem sugerida como pagamento de fatura
// do cartao da conta; confirmar registra o pagamento na fatura, e nao um gasto em Lancamentos.
describe('Pagamento de fatura no extrato importado', () => {
  const pad = (n: number) => String(n).padStart(2, '0');

  beforeEach(() => {
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  /** A fatura de vencimento (dia 17) mais proximo de hoje, como a sugestao do servidor. */
  const suggestedMonth = (today: Date) => {
    const options = [-1, 0, 1].map((offset) => new Date(today.getFullYear(), today.getMonth() + offset, 17));
    const due = options.reduce((best, d) =>
      Math.abs(d.getTime() - today.getTime()) < Math.abs(best.getTime() - today.getTime()) ? d : best);
    return `${due.getFullYear()}-${pad(due.getMonth() + 1)}`;
  };

  /**
   * Anda pelas setas ate a fatura do mes (`yyyy-MM`). Se a seta da direcao certa estiver
   * desabilitada, a fatura ainda nao existe na tela (projecao atrasada): para sem falhar.
   */
  const goToInvoice = (month: string, attempts = 14) => {
    cy.get('[data-cy=invoices]').then(($view) => {
      const shown = $view.find('[data-cy=invoice-month]').text().match(/(\d{2})\/(\d{4})/)!;
      const current = `${shown[2]}-${shown[1]}`;
      const arrow = current < month ? '[data-cy=invoice-next]' : '[data-cy=invoice-prev]';
      if (current === month || attempts <= 1 || $view.find(arrow).is(':disabled')) return;
      cy.get(arrow).click();
      goToInvoice(month, attempts - 1);
    });
  };

  it('sugere o pagamento da fatura, registra na fatura do cartao e nao cria gasto', () => {
    const stamp = Date.now();
    const accountName = `E2E Extrato Fatura ${stamp}`;
    const cardLabel = `Cartao Extrato ${stamp}`;
    const today = new Date();
    const ymd = `${today.getFullYear()}${pad(today.getMonth() + 1)}${pad(today.getDate())}`;
    const month = suggestedMonth(today);
    const ofx = [
      'OFXHEADER:100', 'DATA:OFXSGML', 'VERSION:102', 'CHARSET:1252', '',
      '<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>BRL<BANKTRANLIST>',
      `<STMTTRN><DTPOSTED>${ymd}<TRNAMT>-987.65<FITID>INV11-${stamp}-1<MEMO>PAG FATURA CARTAO ${stamp}</STMTTRN>`,
      `<STMTTRN><DTPOSTED>${ymd}<TRNAMT>-12.30<FITID>INV11-${stamp}-2<MEMO>PADARIA ${stamp}</STMTTRN>`,
      '</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>',
    ].join('\r\n');

    // Conta PF com um cartao (fecha 10, vence 17).
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(accountName);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', accountName).should('be.visible');
    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(accountName);
    cy.get('[data-cy=card-label]').type(cardLabel);
    cy.get('[data-cy=card-lastfour]').type('7777');
    cy.get('[data-cy=card-limit]').type('3000');
    cy.get('[data-cy=card-closing]').clear().type('10');
    cy.get('[data-cy=card-due]').clear().type('17');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', cardLabel).should('be.visible');

    // Previa: a linha "PAG FATURA" ja vem como pagamento da fatura do cartao da conta.
    cy.get('[data-cy=tab-import]').click();
    cy.get('[data-cy=import-account]').find('option').contains(accountName).then(($opt) => {
      cy.get('[data-cy=import-account]').select($opt.val() as string);
    });
    cy.get('[data-cy=import-payment-method]').select('Pix');
    cy.get('[data-cy=import-file]').selectFile({ contents: Cypress.Buffer.from(ofx, 'latin1'), fileName: 'extrato.ofx' });
    cy.get('[data-cy=import-submit]').click();

    cy.get('[data-cy=import-row]').should('have.length', 2);
    cy.get('[data-cy=import-row]').eq(0).should('have.attr', 'data-invoice-payment', 'true').within(() => {
      cy.get('[data-cy=import-row-method]').should('have.value', '__invoice');
      cy.get('[data-cy=import-row-invoice-card] option:selected').should('contain', cardLabel);
      cy.get('[data-cy=import-row-invoice-month]').should('have.value', month);
      cy.get('[data-cy=import-row-category]').should('not.exist');
    });
    // So a padaria precisa de categoria.
    cy.get('[data-cy=import-pending-count]').should('contain', '1 pagamento(s) de fatura').and('contain', '1 sem categoria');
    cy.get('[data-cy=import-row]').eq(1).find('[data-cy=import-row-category]').type(`Padaria ${stamp}`);
    cy.get('[data-cy=import-confirm]').should('be.enabled').click();
    cy.get('[data-cy=import-result-imported]').should('have.text', '2');
    cy.get('[data-cy=import-result]').should('contain', '1 pagamento(s) de fatura');

    // Lancamentos: so a padaria; o pagamento nao virou gasto.
    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-table]').should('contain', `PADARIA ${stamp}`).and('not.contain', `PAG FATURA CARTAO ${stamp}`);

    // Fatura do cartao: o pagamento aparece (a projecao chega em alguns segundos).
    const openUntilPaid = (attempts = 30) => {
      cy.get('[data-cy=tab-cards]').click();
      cy.get('[data-cy=cards-table]').contains('tr', cardLabel).find('[data-cy=card-invoices]').click();
      cy.get('[data-cy=invoice-month]').should('be.visible');
      goToInvoice(month);
      cy.get('[data-cy=invoices]').then(($view) => {
        const onMonth = $view.find('[data-cy=invoice-month]').text().includes(`${month.slice(5)}/${month.slice(0, 4)}`);
        if (onMonth && $view.find('[data-cy=invoice-payment-row]').length > 0) return;
        if (attempts <= 1) throw new Error('O pagamento importado nao chegou a fatura.');
        cy.wait(1_000);
        cy.get('[data-cy=invoices-back]').click();
        openUntilPaid(attempts - 1);
      });
    };
    openUntilPaid();
    cy.get('[data-cy=invoice-payment-row]').should('have.length', 1).and('contain', '987,65').and('contain', accountName);
    cy.get('[data-cy=invoice-paid]').should('contain', '987,65');
  });
});
