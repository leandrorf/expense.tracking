// REC-9 e REC-10 (ADR-0008): a importacao reconhece a ocorrencia de uma recorrencia, e "repetir todo
// mes" cria a regra a partir de um lancamento. As datas do extrato ficam em 2030 para nao depender
// do dia em que o teste roda.
describe('Recorrencias na importacao e a partir de um lancamento', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const label = (d: Date) => `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()}`;

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  const createAccount = (name: string) => {
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(name);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', name).should('be.visible');
  };

  it('a linha do extrato parecida com o aluguel vira a ocorrencia dele', () => {
    const [account, rent] = [`E2E Rec Import ${stamp}`, `Aluguel ${stamp}`];
    createAccount(account);

    cy.get('[data-cy=tab-recurrences]').click();
    cy.get('[data-cy=rec-account]').select(account);
    cy.get('[data-cy=rec-payment-method]').select('Boleto');
    cy.get('[data-cy=rec-description]').type(rent);
    cy.get('[data-cy=rec-category]').type(`Moradia ${stamp}`);
    cy.get('[data-cy=rec-amount]').type('2000');
    cy.get('[data-cy=rec-starts-on]').clear().type('2030-01-10');
    cy.get('[data-cy=rec-submit]').click();
    cy.contains('[data-cy=rec-row]', rent).find('[data-cy=rec-next]').should('have.text', '10/01/2030');

    const ofx = [
      'OFXHEADER:100', 'DATA:OFXSGML', 'VERSION:102', 'CHARSET:1252', '',
      '<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>BRL<BANKTRANLIST>',
      `<STMTTRN><DTPOSTED>20300111<TRNAMT>-2050.00<FITID>REC-${stamp}-1<MEMO>BOLETO ALUGUEL ${stamp}</STMTTRN>`,
      `<STMTTRN><DTPOSTED>20300115<TRNAMT>-40.00<FITID>REC-${stamp}-2<MEMO>PADARIA ${stamp}</STMTTRN>`,
      '</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>',
    ].join('\r\n');

    cy.get('[data-cy=tab-import]').click();
    cy.get('[data-cy=import-account]').find('option').contains(account).then(($opt) => {
      cy.get('[data-cy=import-account]').select($opt.val() as string);
    });
    cy.get('[data-cy=import-payment-method]').select('Pix');
    cy.get('[data-cy=import-file]').selectFile({ contents: Cypress.Buffer.from(ofx, 'latin1'), fileName: 'extrato.ofx', mimeType: 'application/x-ofx' });
    cy.get('[data-cy=import-submit]').click();

    // So a linha parecida com o aluguel (valor ate 10% e data ate 5 dias) vem sugerida; a categoria e a da regra.
    cy.get('[data-cy=import-row]').eq(0).find('[data-cy=import-row-recurrence-label]').should('contain', rent);
    cy.get('[data-cy=import-row]').eq(0).find('[data-cy=import-row-recurrence]').should('be.checked');
    cy.get('[data-cy=import-row]').eq(0).find('[data-cy=import-row-category]').should('have.value', `Moradia ${stamp}`);
    cy.get('[data-cy=import-row]').eq(1).find('[data-cy=import-row-recurrence-label]').should('not.exist');
    cy.get('[data-cy=import-row]').eq(1).find('[data-cy=import-row-category]').type(`Padaria ${stamp}`);
    cy.get('[data-cy=import-confirm]').should('be.enabled').click();
    cy.get('[data-cy=import-result-imported]').should('have.text', '2');

    // O lancamento importado fechou janeiro: a proxima e fevereiro.
    cy.get('[data-cy=tab-transactions]').click();
    cy.contains('[data-cy=tx-row]', `BOLETO ALUGUEL ${stamp}`).find('[data-cy=tx-recurring-label]').should('exist');
    cy.contains('[data-cy=tx-row]', `PADARIA ${stamp}`).find('[data-cy=tx-recurring-label]').should('not.exist');
    cy.get('[data-cy=tab-recurrences]').click();
    cy.contains('[data-cy=rec-row]', rent).find('[data-cy=rec-next]').should('have.text', '10/02/2030');
    cy.contains('[data-cy=rec-row]', rent).find('[data-cy=rec-remove]').click();
  });

  it('"repetir" cria a regra a partir do lancamento, que conta como a ocorrencia do mes dele', () => {
    const [account, internet] = [`E2E Rec Repetir ${stamp}`, `Internet ${stamp}`];
    const today = new Date();
    const nextDay = new Date(today.getFullYear(), today.getMonth() + 1, 1);
    nextDay.setDate(Math.min(today.getDate(), new Date(nextDay.getFullYear(), nextDay.getMonth() + 1, 0).getDate()));
    createAccount(account);

    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);
    cy.get('[data-cy=tx-payment-method]').select('Pix');
    cy.get('[data-cy=tx-description]').type(internet);
    cy.get('[data-cy=tx-category]').type(`Casa ${stamp}`);
    cy.get('[data-cy=tx-amount]').type('99.9');
    cy.get('[data-cy=tx-date]').clear().type(iso(today));
    cy.get('[data-cy=tx-submit]').click();
    cy.contains('[data-cy=tx-row]', internet).find('[data-cy=tx-repeat]').click();

    // A aba Recorrencias abre preenchida com o lancamento.
    cy.get('[data-cy=rec-from-transaction]').should('contain', internet);
    cy.get('[data-cy=rec-description]').should('have.value', internet);
    cy.get('[data-cy=rec-amount]').should('have.value', '99.9');
    cy.get('[data-cy=rec-starts-on]').should('have.value', iso(today));
    cy.get('[data-cy=rec-submit]').click();
    cy.get('[data-cy=rec-from-transaction]').should('not.exist');
    cy.contains('[data-cy=rec-row]', internet).find('[data-cy=rec-next]').should('have.text', label(nextDay));

    // O lancamento ficou vinculado (e nao oferece "repetir" de novo).
    cy.get('[data-cy=tab-transactions]').click();
    cy.contains('[data-cy=tx-row]', internet).find('[data-cy=tx-recurring-label]').should('exist');
    cy.contains('[data-cy=tx-row]', internet).find('[data-cy=tx-repeat]').should('not.exist');

    cy.get('[data-cy=tab-recurrences]').click();
    cy.contains('[data-cy=rec-row]', internet).find('[data-cy=rec-remove]').click();
  });
});
