// IMP-7: aba Importar. Extrato OFX de conta PF: prévia, categoria em lote, confirmação, resumo e
// os lançamentos na aba Lançamentos; reenviar o mesmo arquivo traz tudo como já lançado.
// (A IMP-10 amplia com a fatura PJ em CSV e o reflexo no Resumo e nos orçamentos.)
describe('Importar extrato (OFX)', () => {
  beforeEach(() => {
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('importa um extrato PF com prévia, categoria em lote e sem duplicar na segunda vez', () => {
    // Nomes gerados dentro do teste: uma nova tentativa do Cypress não reaproveita a conta anterior.
    const stamp = Date.now();
    const accountName = `E2E Import ${stamp}`;
    const category = `Mercado ${stamp}`;
    const incomeCategory = `Salario ${stamp}`;
    const ofx = [
      'OFXHEADER:100', 'DATA:OFXSGML', 'VERSION:102', 'CHARSET:1252', '',
      '<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>BRL<BANKTRANLIST>',
      `<STMTTRN><DTPOSTED>20261102<TRNAMT>-18.50<FITID>E2E-${stamp}-1<MEMO>PADARIA E2E ${stamp}</STMTTRN>`,
      `<STMTTRN><DTPOSTED>20261103<TRNAMT>-120.00<FITID>E2E-${stamp}-2<MEMO>MERCADO E2E ${stamp}</STMTTRN>`,
      `<STMTTRN><DTPOSTED>20261105<TRNAMT>3000.00<FITID>E2E-${stamp}-3<MEMO>SALARIO E2E ${stamp}</STMTTRN>`,
      '</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>',
    ].join('\r\n');
    const file = { contents: Cypress.Buffer.from(ofx, 'latin1'), fileName: 'extrato.ofx', mimeType: 'application/x-ofx' };

    // 1) Conta PF
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(accountName);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', accountName).should('be.visible');

    // 2) Envio
    cy.get('[data-cy=tab-import]').click();
    cy.get('[data-cy=import-account]').find('option').contains(accountName).then(($opt) => {
      cy.get('[data-cy=import-account]').select($opt.val() as string);
    });
    cy.get('[data-cy=import-card]').should('have.value', '');
    cy.get('[data-cy=import-payment-method]').select('Pix');
    cy.get('[data-cy=import-file]').selectFile(file);
    cy.get('[data-cy=import-submit]').click();

    // 3) Prévia: três linhas novas, incluídas, sem categoria; confirmar bloqueado até categorizar.
    cy.get('[data-cy=import-row]').should('have.length', 3);
    cy.get('[data-cy=import-row][data-status=New]').should('have.length', 3);
    cy.get('[data-cy=import-pending-count]').should('contain', '3 incluída(s)').and('contain', '3 sem categoria');
    cy.get('[data-cy=import-confirm]').should('be.disabled');

    cy.get('[data-cy=import-bulk-category]').type(category);
    cy.get('[data-cy=import-bulk-apply-empty]').click();
    cy.get('[data-cy=import-row]').eq(2).find('[data-cy=import-row-category]').clear().type(incomeCategory);
    cy.get('[data-cy=import-row]').eq(2).find('[data-cy=import-row-method]').select('Transferencia bancaria');
    cy.get('[data-cy=import-pending-count]').should('not.contain', 'sem categoria');

    // 4) Confirmação e resumo
    cy.get('[data-cy=import-confirm]').should('be.enabled').click();
    cy.get('[data-cy=import-result]').should('be.visible');
    cy.get('[data-cy=import-result-imported]').should('have.text', '3');
    cy.get('[data-cy=import-result-duplicate]').should('have.text', '0');
    cy.get('[data-cy=import-result-ignored]').should('have.text', '0');

    // 5) Os lançamentos aparecem na aba Lançamentos
    cy.get('[data-cy=import-go-transactions]').click();
    cy.get('[data-cy=tx-table]').contains('tr', `PADARIA E2E ${stamp}`).should('contain', category).and('contain', '18,50');
    cy.get('[data-cy=tx-table]').contains('tr', `SALARIO E2E ${stamp}`).should('contain', incomeCategory);

    // 6) O mesmo arquivo de novo: tudo já lançado, nada a importar.
    cy.get('[data-cy=tab-import]').click();
    cy.get('[data-cy=import-account]').find('option').contains(accountName).then(($opt) => {
      cy.get('[data-cy=import-account]').select($opt.val() as string);
    });
    cy.get('[data-cy=import-file]').selectFile(file);
    cy.get('[data-cy=import-submit]').click();
    cy.get('[data-cy=import-row][data-status=Duplicate]').should('have.length', 3);
    cy.get('[data-cy=import-row-include]').each(($box) => cy.wrap($box).should('be.disabled').and('not.be.checked'));
    cy.get('[data-cy=import-confirm]').should('be.disabled');
  });
});
