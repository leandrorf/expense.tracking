// CYC-7 (ADR-0012): jornada da troca de ciclo pela SPA. Um cartao que fecha dia 10 e vence dia 17 tem uma compra
// numa fatura que ja fechou, paga pela tela. O ciclo passa a fechar dia 25 e vencer dia 5 a partir da fatura que a
// tela sugere. A fatura paga continua igual (total, pagamento, situacao); a fatura da troca fecha no dia 25 do mes
// anterior, vence no dia 5 e recebe uma compra feita no dia 20; o limite novo aparece no disponivel na hora.
// As datas sao calculadas a partir de hoje, para a jornada valer em qualquer dia do mes.
describe('Cartoes: jornada da troca de ciclo', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const monthOf = (d: Date) => `${pad(d.getMonth() + 1)}/${d.getFullYear()}`;
  /** `R$ 1.234,56` → 1234.56 (o espaco do toLocaleString e nao separavel). */
  const parse = (text: string) => Number(text.replace(/[^\d,-]/g, '').replace(',', '.'));

  const today = new Date();
  // Ciclo antigo (fecha 10): a fatura aberta e a deste mes ate o dia 10, depois a do mes seguinte.
  const open = new Date(today.getFullYear(), today.getMonth() + (today.getDate() <= 10 ? 0 : 1), 1);
  const paidMonth = new Date(open.getFullYear(), open.getMonth() - 1, 1); // ja fechou (dia 10 do mes dela)
  const paidPurchase = new Date(paidMonth.getFullYear(), paidMonth.getMonth(), 5);

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('troca o ciclo sem mexer na fatura paga, com a fatura da troca e o limite novo', () => {
    const [account, card] = [`E2E Jornada Ciclo ${stamp}`, `Cartao Ciclo ${stamp}`];
    const [oldPurchase, newPurchase] = [`Antes da troca ${stamp}`, `Na fatura da troca ${stamp}`];
    const row = () => cy.contains('[data-cy=card-row]', card);
    const openInvoices = () => {
      cy.get('[data-cy=tab-cards]').click();
      row().find('[data-cy=card-invoices]').click();
      cy.get('[data-cy=invoice-figures]').should('exist');
    };
    /** Navega pelas faturas ate a do mes (mm/yyyy), esperando a projecao trazer a compra. */
    const goToInvoice = (month: string, purchase: string, attempts = 15) => {
      const step = (left: number) => {
        cy.get('[data-cy=invoice-month]').invoke('text').then((label) => {
          if (label.includes(month)) {
            cy.get('body').then(($body) => {
              if ($body.find(`[data-cy=invoice-item]:contains("${purchase}")`).length > 0) return;
              if (attempts <= 1) throw new Error(`A compra "${purchase}" nao chegou a fatura de ${month}.`);
              cy.wait(1000);
              cy.get('[data-cy=invoices-back]').click();
              row().find('[data-cy=card-invoices]').click();
              goToInvoice(month, purchase, attempts - 1);
            });
            return;
          }
          if (left <= 0) throw new Error(`Fatura de ${month} nao encontrada (${label}).`);
          const [m, y] = month.split('/').map(Number);
          const [lm, ly] = (label.match(/(\d{2})\/(\d{4})/) ?? ['', '0', '0']).slice(1).map(Number);
          const button = y * 12 + m > ly * 12 + lm ? '[data-cy=invoice-next]' : '[data-cy=invoice-prev]';
          cy.get(button).then(($button) => {
            if (!$button.prop('disabled')) {
              cy.wrap($button).click();
              step(left - 1);
              return;
            }
            // A fatura ainda nao existe na lista: a projecao nao trouxe a compra. Recarrega e tenta de novo.
            if (attempts <= 1) throw new Error(`Fatura de ${month} nao apareceu (${label}).`);
            cy.wait(1000);
            cy.get('[data-cy=invoices-back]').click();
            row().find('[data-cy=card-invoices]').click();
            goToInvoice(month, purchase, attempts - 1);
          });
        });
      };
      step(12);
    };

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('7171');
    cy.get('[data-cy=card-limit]').type('5000');
    cy.get('[data-cy=card-closing]').clear().type('10');
    cy.get('[data-cy=card-due]').clear().type('17');
    cy.get('[data-cy=card-submit]').click();
    row().should('exist');

    const buy = (description: string, date: Date, amount: string) => {
      cy.get('[data-cy=tab-transactions]').click();
      cy.get('[data-cy=tx-account]').select(account);
      cy.get('[data-cy=tx-payment-method]').select('CreditCard');
      cy.get('[data-cy=tx-card]').find('option').contains(card).then(($o) => cy.get('[data-cy=tx-card]').select($o.val() as string));
      cy.get('[data-cy=tx-description]').clear().type(description);
      cy.get('[data-cy=tx-category]').clear().type(`Compras ${stamp}`);
      cy.get('[data-cy=tx-amount]').clear().type(amount);
      cy.get('[data-cy=tx-date]').clear().type(iso(date));
      cy.get('[data-cy=tx-submit]').click();
      cy.get('[data-cy=tx-table]').contains('td', description).should('be.visible');
    };

    // Uma compra numa fatura que ja fechou, paga pela tela.
    buy(oldPurchase, paidPurchase, '150');
    openInvoices();
    goToInvoice(monthOf(paidMonth), oldPurchase);
    cy.get('[data-cy=invoice-pay]').click();
    cy.get('[data-cy=payment-submit]').click();
    cy.get('[data-cy=invoice-status]', { timeout: 30_000 }).should('have.attr', 'data-status', 'Paid');

    // Troca o ciclo pela tela (fecha 25, vence 5) a partir da fatura sugerida, e o limite de 5.000 para 8.000.
    cy.get('[data-cy=invoices-back]').click();
    // Nada mais em aberto (a fatura foi paga): o disponivel passa a ser o limite novo.
    row().find('[data-cy=card-edit-open]').click();
    cy.get('[data-cy=card-edit-limit]').clear().type('8000');
    cy.get('[data-cy=card-edit-save]').click();
    cy.get('[data-cy=card-notice]').should('contain', 'atualizado');
    row().find('[data-cy=card-available]').should(($cell) => {
      expect(parse($cell.text())).to.be.closeTo(8000, 0.005);
    });
    cy.get('[data-cy=card-edit-closing]').clear().type('25');
    cy.get('[data-cy=card-edit-due]').clear().type('5');
    cy.get('[data-cy=card-edit-from]').invoke('val').then((value) => {
      const [y, m] = String(value).split('-').map(Number);
      const transition = new Date(y, m - 1, 1);
      const closingMonth = new Date(y, m - 2, 1);
      const closing = `25/${pad(closingMonth.getMonth() + 1)}/${closingMonth.getFullYear()}`;
      const due = `05/${pad(m)}/${y}`;
      cy.get('[data-cy=card-edit-preview]').should('contain', `a ${closing}`).and('contain', `vence ${due}`);
      cy.get('[data-cy=card-edit-cycle-save]').click();
      cy.get('[data-cy=card-notice]').should('contain', 'Ciclo de');

      // Uma compra no dia 20 do mes do primeiro fechamento novo cai na fatura da troca.
      buy(newPurchase, new Date(y, m - 2, 20), '60');
      openInvoices();
      goToInvoice(monthOf(transition), newPurchase);
      cy.get('[data-cy=invoice-dates]').should('contain', `${closing} / ${due}`);
      cy.get('[data-cy=invoice-total]').should('contain', '60,00');
    });

    // A fatura paga continua igual.
    goToInvoice(monthOf(paidMonth), oldPurchase);
    cy.get('[data-cy=invoice-status]').should('have.attr', 'data-status', 'Paid');
    cy.get('[data-cy=invoice-total]').should('contain', '150,00');
    cy.get('[data-cy=invoice-paid]').should('contain', '150,00');
  });
});
