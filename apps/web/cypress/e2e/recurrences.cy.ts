// REC-5 (ADR-0008): a aba Recorrencias. Uma regra que comecou ha 20 dias tem a primeira ocorrencia
// "a lancar"; lancada com o valor real, vira um lancamento comum marcado como recorrencia. A regra
// e editada, a proxima ocorrencia e pulada e, no fim, a regra e excluida (o lancamento fica).
describe('Recorrencias', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const started = new Date();
  started.setDate(started.getDate() - 20);

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('cadastra, lanca o que esta pendente, edita, pula e exclui', () => {
    const [account, gym] = [`E2E Recorrencia ${stamp}`, `Academia ${stamp}`];
    const ruleRow = () => cy.contains('[data-cy=rec-row]', gym);

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    // Cadastro: gasto mensal no Pix, comecando ha 20 dias.
    cy.get('[data-cy=tab-recurrences]').click();
    cy.get('[data-cy=rec-account]').select(account);
    cy.get('[data-cy=rec-payment-method]').select('Pix');
    cy.get('[data-cy=rec-description]').type(gym);
    cy.get('[data-cy=rec-category]').type(`Saude ${stamp}`);
    cy.get('[data-cy=rec-amount]').type('120');
    cy.get('[data-cy=rec-frequency]').select('Monthly');
    cy.get('[data-cy=rec-starts-on]').clear().type(iso(started));
    cy.get('[data-cy=rec-submit]').click();
    ruleRow().should('contain', `todo dia ${started.getDate()}`).find('[data-cy=rec-value]').should('contain', '120,00');

    // A primeira ocorrencia ja passou: esta "a lancar". Lancada com o valor real, sai da lista.
    cy.contains('[data-cy=rec-due-row]', gym).find('[data-cy=rec-due-launch]').click();
    cy.get('[data-cy=rec-launch-amount]').should('have.value', '120');
    cy.get('[data-cy=rec-launch-date]').should('have.value', iso(started));
    cy.get('[data-cy=rec-launch-amount]').clear().type('125.5');
    cy.get('[data-cy=rec-launch-save]').click();
    cy.get('[data-cy=rec-notice]').should('contain', `Lançado: ${gym}`);
    cy.contains('[data-cy=rec-due-row]', gym).should('not.exist');

    cy.get('[data-cy=tab-transactions]').click();
    cy.contains('[data-cy=tx-row]', gym).should('contain', '125,50').find('[data-cy=tx-recurring-label]').should('exist');

    // Editar: o valor estimado muda; o lancamento ja feito nao.
    cy.get('[data-cy=tab-recurrences]').click();
    ruleRow().find('[data-cy=rec-edit]').click();
    cy.get('[data-cy=rec-account]').should('be.disabled');
    cy.get('[data-cy=rec-amount]').clear().type('130');
    cy.get('[data-cy=rec-submit]').click();
    ruleRow().find('[data-cy=rec-value]').should('contain', '130,00');

    // Pular a proxima: a data da proxima anda um mes.
    ruleRow().find('[data-cy=rec-next]').invoke('text').then((nextBefore) => {
      ruleRow().find('[data-cy=rec-skip-next]').click();
      cy.get('[data-cy=rec-notice]').should('contain', `Pulado: ${gym}`);
      ruleRow().find('[data-cy=rec-next]').should('not.have.text', nextBefore);
    });

    // Excluir a regra: some da lista; o lancamento feito a partir dela fica.
    ruleRow().find('[data-cy=rec-remove]').click();
    cy.contains('[data-cy=rec-row]', gym).should('not.exist');
    cy.get('[data-cy=tab-transactions]').click();
    cy.contains('[data-cy=tx-row]', gym).should('contain', '125,50');
  });
});
