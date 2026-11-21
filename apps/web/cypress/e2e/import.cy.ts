// IMP-7/IMP-10: aba Importar. Extrato OFX de conta PF (prévia, categoria em lote, confirmação,
// Lançamentos e Resumo) e fatura CSV de cartão PJ (pagamento da fatura fora, orçamento PJ subindo);
// reenviar o mesmo arquivo traz o que já foi lançado como duplicado. Resumo e orçamentos leem a
// projeção do reporting, então as asserções neles esperam com cy.waitForSummary / cy.waitForBudget.
describe('Importar extrato e fatura', () => {
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

    // 6) O Resumo reflete o que foi importado: os dois gastos somados na categoria do lote (os
    //    blocos do Resumo só somam gastos; a receita não entra em "Por categoria").
    cy.get('[data-cy=tab-summary]').click();
    cy.waitForSummary('[data-cy=by-category]', category);
    cy.get('[data-cy=by-category]').contains('li', category).should('contain', '138,50');
    cy.get('[data-cy=by-category]').should('not.contain', incomeCategory);

    // 7) O mesmo arquivo de novo: tudo já lançado, nada a importar.
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

  // IMP-8: CSV com colunas que o servidor não reconhece; o usuário as escolhe vendo as primeiras linhas.
  it('importa um CSV escolhendo as colunas, com débito e crédito separados', () => {
    const stamp = Date.now();
    const accountName = `E2E CSV ${stamp}`;
    const csv = [
      'Quando;O que;Saiu;Entrou',
      `03/11/2026;FARMACIA E2E ${stamp};45,90;`,
      `04/11/2026;REEMBOLSO E2E ${stamp};;80,00`,
    ].join('\n');

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(accountName);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', accountName).should('be.visible');

    cy.get('[data-cy=tab-import]').click();
    cy.get('[data-cy=import-account]').find('option').contains(accountName).then(($opt) => {
      cy.get('[data-cy=import-account]').select($opt.val() as string);
    });
    cy.get('[data-cy=import-file]').selectFile({ contents: Cypress.Buffer.from(csv), fileName: 'extrato.csv', mimeType: 'text/csv' });
    cy.get('[data-cy=import-submit]').click();

    // Passo de colunas: a amostra mostra as linhas do arquivo e o cabeçalho vira o nome da coluna.
    cy.get('[data-cy=csv-mapping]').should('contain', 'Não reconhecemos');
    cy.get('[data-cy=csv-sample]').should('contain', `FARMACIA E2E ${stamp}`).and('contain', '45,90');
    cy.get('[data-cy=csv-map-header]').should('be.checked');

    cy.get('[data-cy=csv-map-date]').select('1. Quando');
    cy.get('[data-cy=csv-map-description]').select('2. O que');
    cy.get('[data-cy=csv-map-mode-split]').check();
    cy.get('[data-cy=csv-map-debit]').select('3. Saiu');
    cy.get('[data-cy=csv-map-credit]').select('4. Entrou');
    cy.get('[data-cy=csv-map-decimal]').select(',');
    cy.get('[data-cy=csv-role]').should('have.length', 4);

    // Duas colunas no mesmo campo bloqueiam a prévia.
    cy.get('[data-cy=csv-map-credit]').select('3. Saiu');
    cy.get('[data-cy=csv-map-problem]').should('be.visible');
    cy.get('[data-cy=csv-map-submit]').should('be.disabled');
    cy.get('[data-cy=csv-map-credit]').select('4. Entrou');

    cy.get('[data-cy=csv-map-submit]').should('be.enabled').click();

    // Prévia: débito virou gasto e crédito virou receita.
    cy.get('[data-cy=import-row]').should('have.length', 2);
    cy.get('[data-cy=import-row]').eq(0).should('contain', `FARMACIA E2E ${stamp}`).and('contain', '−').and('contain', '45,90');
    cy.get('[data-cy=import-row]').eq(1).should('contain', `REEMBOLSO E2E ${stamp}`).and('contain', '+').and('contain', '80,00');

    cy.get('[data-cy=import-bulk-category]').type(`Saude ${stamp}`);
    cy.get('[data-cy=import-bulk-apply-all]').click();
    cy.get('[data-cy=import-confirm]').click();
    cy.get('[data-cy=import-result-imported]').should('have.text', '2');
  });

  // IMP-10: fatura de cartão PJ em CSV. O pagamento da fatura (crédito) fica fora por padrão, as
  // compras entram no cartão e sobem o orçamento PJ da categoria, sem tocar o PF de mesmo nome.
  it('importa uma fatura PJ em CSV: pagamento fora, orçamento PJ sobe e reenvio vem duplicado', () => {
    const stamp = Date.now();
    const accountName = `E2E IMP PJ ${stamp}`;
    const cardLabel = `Cartao IMP ${stamp}`;
    const category = `Viagens IMP ${stamp}`;
    // Datas no mês corrente: o acompanhamento de orçamentos abre no mês do navegador.
    const now = new Date();
    const today = [now.getDate(), now.getMonth() + 1].map((n) => String(n).padStart(2, '0')).join('/') + `/${now.getFullYear()}`;
    const csv = [
      'Data;Descricao;Valor',
      `${today};HOTEL E2E ${stamp};250,00`,
      `${today};PASSAGEM E2E ${stamp};150,00`,
      `${today};PAGAMENTO FATURA E2E ${stamp};-900,00`,
    ].join('\n');
    const file = { contents: Cypress.Buffer.from(csv), fileName: 'fatura.csv', mimeType: 'text/csv' };

    const uploadInvoice = () => {
      cy.get('[data-cy=tab-import]').click();
      cy.get('[data-cy=import-account]').find('option').contains(accountName).then(($opt) => {
        cy.get('[data-cy=import-account]').select($opt.val() as string);
      });
      cy.get('[data-cy=import-card]').select(`Fatura: ${cardLabel} •••• 4242`);
      cy.get('[data-cy=import-payment-method]').should('not.exist');
      cy.get('[data-cy=import-file]').selectFile(file);
      cy.get('[data-cy=import-submit]').click();
      // Colunas reconhecidas pelo cabeçalho; na fatura o sinal vem invertido (compra positiva).
      cy.get('[data-cy=csv-mapping]').should('contain', 'reconhecemos');
      cy.get('[data-cy=csv-map-invert]').should('be.checked');
      cy.get('[data-cy=csv-map-submit]').should('be.enabled').click();
    };

    // 1) Conta PJ, cartão e orçamentos PJ e PF da mesma categoria
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(accountName);
    cy.get('[data-cy=account-scope]').select('Business');
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', accountName).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(accountName);
    cy.get('[data-cy=card-label]').type(cardLabel);
    cy.get('[data-cy=card-lastfour]').type('4242');
    cy.get('[data-cy=card-limit]').type('5000');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', cardLabel).should('be.visible');

    for (const [scope, amount] of [['Business', '1000'], ['Personal', '500']] as const) {
      cy.get('[data-cy=tab-budgets]').click();
      cy.get('[data-cy=budget-category]').type(category);
      cy.get('[data-cy=budget-scope]').select(scope);
      cy.get('[data-cy=budget-amount]').type(amount);
      cy.get('[data-cy=budget-submit]').click();
      cy.get(`[data-cy=budgets-${scope.toLowerCase()}] [data-cy=budget-row][data-category="${category}"]`).should('exist');
    }

    // 2) Envio e prévia: duas compras novas e o pagamento da fatura fora por padrão.
    uploadInvoice();
    cy.get('[data-cy=import-preview-info]').should('contain', 'Fatura de cartão');
    cy.get('[data-cy=import-row]').should('have.length', 3);
    cy.get('[data-cy=import-row][data-status=New]').should('have.length', 2);
    cy.get('[data-cy=import-row][data-status=ExcludedByDefault]')
      .should('contain', `PAGAMENTO FATURA E2E ${stamp}`)
      .find('[data-cy=import-row-include]')
      .should('not.be.checked');

    cy.get('[data-cy=import-bulk-category]').type(category);
    cy.get('[data-cy=import-bulk-apply-empty]').click();
    cy.get('[data-cy=import-confirm]').should('be.enabled').click();
    cy.get('[data-cy=import-result-imported]').should('have.text', '2');
    cy.get('[data-cy=import-result-ignored]').should('have.text', '1');

    // 3) As compras entram no cartão; o pagamento da fatura não vira lançamento.
    cy.get('[data-cy=import-go-transactions]').click();
    cy.get('[data-cy=tx-table]').contains('tr', `HOTEL E2E ${stamp}`).should('contain', 'Cartao de credito').and('contain', '250,00');
    cy.get('[data-cy=tx-table]').contains('tr', `PASSAGEM E2E ${stamp}`).should('contain', category);
    cy.get('[data-cy=tx-table]').should('not.contain', `PAGAMENTO FATURA E2E ${stamp}`);

    // 4) Orçamento PJ sobe com as compras; o PF da mesma categoria continua zerado.
    cy.get('[data-cy=tab-budgets]').click();
    cy.waitForBudget('business', category, '400,00');
    cy.get(`[data-cy=budget-status-business] [data-cy=budget-status-row][data-category="${category}"]`)
      .find('[data-cy=budget-status-percent]')
      .should('contain', '40%');
    cy.get(`[data-cy=budget-status-personal] [data-cy=budget-status-row][data-category="${category}"]`)
      .find('[data-cy=budget-status-spent]')
      .should('contain', '0,00');

    // 5) A mesma fatura de novo: as compras já lançadas vêm duplicadas (CSV sem id do banco usa a
    //    impressão digital) e o pagamento, que nunca virou lançamento, continua fora por padrão.
    uploadInvoice();
    cy.get('[data-cy=import-row][data-status=Duplicate]').should('have.length', 2);
    cy.get('[data-cy=import-row][data-status=ExcludedByDefault]').should('contain', `PAGAMENTO FATURA E2E ${stamp}`);
    cy.get('[data-cy=import-row][data-status=New]').should('not.exist');
    cy.get('[data-cy=import-confirm]').should('be.disabled');
  });
});
