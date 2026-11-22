// CTB-7: jornada da exportacao contabil pela SPA. Lanca numa conta PJ (CNPJ proprio do teste) e numa
// PF, paga parte da fatura do cartao PJ, exporta a conta PJ na aba Contador e abre o ZIP baixado: so
// os lancamentos PJ (a compra em 3x uma vez, inteira), o pagamento e o resumo com os totais da previa.
// A categoria ganha uma conta contabil (CTB-5), que sai nas planilhas. Tudo no mes de hoje; o
// formulario de lancamento so registra gastos, entao a jornada nao tem receita.
describe('Exportacao contabil (jornada PJ e PF)', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const today = new Date();
  const iso = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;
  const br = `${pad(today.getDate())}/${pad(today.getMonth() + 1)}/${today.getFullYear()}`;
  const month = iso.slice(0, 7);

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  const createAccount = (name: string, scope: 'Personal' | 'Business', taxId?: string) => {
    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(name);
    cy.get('[data-cy=account-scope]').select(scope);
    if (taxId) cy.get('[data-cy=account-taxid]').type(taxId);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', name).should('be.visible');
  };

  const createCard = (account: string, card: string) => {
    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('5555');
    cy.get('[data-cy=card-limit]').type('10000');
    cy.get('[data-cy=card-closing]').clear().type('10');
    cy.get('[data-cy=card-due]').clear().type('17');
    cy.get('[data-cy=card-submit]').click();
    cy.get('[data-cy=cards-table]').contains('td', card).should('be.visible');
  };

  const expense = (account: string, description: string, amount: string, card?: string, installments = 1) => {
    cy.get('[data-cy=tab-transactions]').click();
    cy.get('[data-cy=tx-account]').select(account);
    cy.get('[data-cy=tx-payment-method]').select(card ? 'CreditCard' : 'Pix');
    if (card) {
      cy.get('[data-cy=tx-card]').find('option').contains(card).then(($opt) => {
        cy.get('[data-cy=tx-card]').select($opt.val() as string);
      });
      cy.get('[data-cy=tx-installments]').select(String(installments));
    }
    cy.get('[data-cy=tx-description]').clear().type(description);
    cy.get('[data-cy=tx-category]').clear().type(`Contabil ${stamp}`);
    cy.get('[data-cy=tx-amount]').clear().type(amount);
    cy.get('[data-cy=tx-date]').clear().type(iso);
    cy.get('[data-cy=tx-submit]').click();
    cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');
  };

  // A fatura le a projecao do reporting, que recebe a compra alguns segundos depois: volta e reabre
  // ate a compra aparecer.
  const openInvoiceWith = (card: string, description: string, attempts = 30) => {
    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=cards-table]').contains('tr', card).find('[data-cy=card-invoices]').click();
    cy.get('[data-cy=invoice-month]').should('be.visible');
    cy.get('[data-cy=invoices]').then(($section) => {
      if ($section.find('[data-cy=invoice-items]').text().includes(description)) return;
      if (attempts <= 1) throw new Error(`A compra ${description} nao chegou a fatura (projecao atrasada?).`);
      cy.wait(1_000);
      cy.get('[data-cy=invoices-back]').click();
      openInvoiceWith(card, description, attempts - 1);
    });
  };

  it('o ZIP baixado tem so o movimento PJ, o pagamento da fatura e o resumo da previa', () => {
    // CNPJ so deste teste (14 digitos a partir do carimbo de tempo), para o nome do arquivo.
    const cnpj = String(stamp).padStart(14, '9').slice(-14);
    const cnpjText = `${cnpj.slice(0, 2)}.${cnpj.slice(2, 5)}.${cnpj.slice(5, 8)}/${cnpj.slice(8, 12)}-${cnpj.slice(12)}`;
    const [pj, pf, card] = [`E2E Contabil PJ ${stamp}`, `E2E Contabil PF ${stamp}`, `Cartao Contabil ${stamp}`];

    createAccount(pj, 'Business', cnpjText);
    createAccount(pf, 'Personal');
    createCard(pj, card);
    expense(pj, `Software ${stamp}`, '120.50');
    expense(pj, `Notebook ${stamp}`, '900', card, 3);
    expense(pf, `Mercado ${stamp}`, '75');

    // Paga parte da fatura atual do cartao PJ, pela conta PJ.
    openInvoiceWith(card, `Notebook ${stamp}`);
    cy.get('[data-cy=invoice-month]').invoke('text').then((text) => {
      const invoice = text.match(/\d{2}\/\d{4}/)![0];
      cy.get('[data-cy=invoice-pay]').click();
      cy.get('[data-cy=payment-account]').find('option').contains(pj).then(($opt) => {
        cy.get('[data-cy=payment-account]').select($opt.val() as string);
      });
      cy.get('[data-cy=payment-amount]').clear().type('100');
      cy.get('[data-cy=payment-date]').clear().type(iso);
      cy.get('[data-cy=payment-submit]').click();
      cy.get('[data-cy=invoice-paid]', { timeout: 30_000 }).should('contain', '100,00');
      cy.wrap(invoice).as('invoice');
    });

    // Aba Contador: so a conta PJ do teste, no mes de hoje.
    cy.intercept('GET', '**/api/transactions/accounting-export/preview*').as('preview');
    cy.get('[data-cy=tab-accounting]').click();
    cy.get('[data-cy=accounting-month]').clear().type(month);
    cy.get('[data-cy=accounting-account]').find('option').contains(pj).then(($opt) => {
      cy.get('[data-cy=accounting-account]').select($opt.val() as string);
    });
    cy.wait('@preview');
    cy.get('[data-cy=accounting-lines]').should('have.text', '2');
    cy.get('[data-cy=accounting-payments]').should('have.text', '1');
    cy.get('[data-cy=accounting-total][data-currency=BRL] [data-cy=accounting-expense]').should('contain', '1.020,50');

    // Plano de contas: sem codigo, a previa avisa; com codigo, o aviso some.
    cy.get('[data-cy=accounting-unmapped]').should('contain', '2 lançamento(s) sem conta contábil');
    cy.get(`[data-cy=ledger-row][data-category="Contabil ${stamp}"]`).within(() => {
      cy.get('[data-cy=ledger-code]').type('3.1.02');
      cy.get('[data-cy=ledger-name]').type('Despesas operacionais');
      cy.get('[data-cy=ledger-save]').click();
    });
    cy.wait('@preview');
    cy.get('[data-cy=accounting-unmapped]').should('not.exist');
    cy.get(`[data-cy=ledger-row][data-category="Contabil ${stamp}"] [data-cy=ledger-remove]`).should('be.visible');

    const fileName = `contabil-${cnpj}-${month}.zip`;
    cy.get('[data-cy=accounting-download]').should('contain.text', fileName).click();
    cy.readFile(`${Cypress.config('downloadsFolder')}/${fileName}`, null).should('exist');

    cy.task<Record<string, string>>('unzip', `${Cypress.config('downloadsFolder')}/${fileName}`).then((files) => {
      const lines = (name: string) => files[name].replace(/^﻿/, '').split('\r\n').filter(Boolean);

      // Lancamentos: os dois PJ, a compra em 3x uma vez e inteira; o gasto PF nao entra.
      expect(lines('lancamentos.csv')).to.deep.equal([
        'Data;Descrição;Categoria;Conta contábil;Tipo;Valor;Moeda;Meio;Cartão;Parcelas;Conta;CNPJ;Observações;Origem',
        `${br};Software ${stamp};Contabil ${stamp};3.1.02;Despesa;-120,50;BRL;Pix;;1;${pj};${cnpjText};;digitado`,
        `${br};Notebook ${stamp};Contabil ${stamp};3.1.02;Despesa;-900,00;BRL;Cartão de crédito;${card} final 5555;3;${pj};${cnpjText};;digitado`,
      ]);
      expect(files['lancamentos.csv']).not.to.contain(`Mercado ${stamp}`);

      cy.get<string>('@invoice').then((invoice) => {
        expect(lines('pagamentos-fatura.csv')).to.deep.equal([
          'Data;Cartão;Fatura;Conta de origem;CNPJ;Valor;Moeda',
          `${br};${card} final 5555;${invoice};${pj};${cnpjText};100,00;BRL`,
        ]);
      });

      // Resumo: os mesmos totais que a previa mostrou na tela.
      expect(lines('resumo.csv')).to.deep.equal([
        'Moeda;Categoria;Conta contábil;Entradas;Saídas;Saldo',
        `BRL;Contabil ${stamp};3.1.02;0,00;1020,50;-1020,50`,
        'BRL;Total;;0,00;1020,50;-1020,50',
      ]);
    });
  });
});
