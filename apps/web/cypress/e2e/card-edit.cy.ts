// CYC-5 (ADR-0012): editar o cartao na aba Cartoes. Corrige nome e limite; troca o ciclo a partir da primeira
// fatura possivel, vendo antes a fatura da troca; a troca fica pendente na lista e pode ser desfeita. Uma fatura
// que ja fechou nao aceita a troca.
describe('Cartoes: editar dados e ciclo', () => {
  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('corrige nome e limite, troca o ciclo com previa e desfaz a troca pendente', () => {
    const [account, card] = [`E2E Editar Cartao ${stamp}`, `Cartao Edit ${stamp}`];
    const row = () => cy.contains('[data-cy=card-row]', `${card} Black`);

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-cards]').click();
    cy.get('[data-cy=card-account]').select(account);
    cy.get('[data-cy=card-label]').type(card);
    cy.get('[data-cy=card-lastfour]').type('3030');
    cy.get('[data-cy=card-limit]').type('5000');
    cy.get('[data-cy=card-closing]').clear().type('10');
    cy.get('[data-cy=card-due]').clear().type('17');
    cy.get('[data-cy=card-submit]').click();

    // Dados: nome e limite.
    cy.contains('[data-cy=card-row]', card).find('[data-cy=card-edit-open]').click();
    cy.get('[data-cy=card-edit-label]').clear().type(`${card} Black`);
    cy.get('[data-cy=card-edit-limit]').clear().type('8000');
    cy.get('[data-cy=card-edit-save]').click();
    cy.get('[data-cy=card-notice]').should('contain', 'atualizado');
    row().should('contain', '8.000,00');

    // Ciclo: a fatura do mes passado ja fechou; a primeira possivel vem preenchida, com a previa.
    const now = new Date();
    const pad = (n: number) => String(n).padStart(2, '0');
    const past = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    cy.get('[data-cy=card-edit-closing]').clear().type('25');
    cy.get('[data-cy=card-edit-due]').clear().type('5');
    cy.get('[data-cy=card-edit-from]').invoke('val').then((suggested) => {
      cy.get('[data-cy=card-edit-from]').clear().type(`${past.getFullYear()}-${pad(past.getMonth() + 1)}`);
      cy.get('[data-cy=card-edit-preview]').should('contain', 'já fechou');
      cy.get('[data-cy=card-edit-cycle-save]').should('be.disabled');

      cy.get('[data-cy=card-edit-from]').clear().type(String(suggested));
      cy.get('[data-cy=card-edit-preview]').should('contain', 'vai de').and('contain', 'vence').and('contain', ' a 25/');
      cy.get('[data-cy=card-edit-cycle-save]').click();
      cy.get('[data-cy=card-notice]').should('contain', 'Ciclo de');

      const [y, m] = String(suggested).split('-');
      row().find('[data-cy=card-cycle-pending]').should('contain', `${m}/${y}`).and('contain', '25/5');
    });

    // Desfazer a troca pendente (a edicao continua aberta depois de salvar).
    cy.get('[data-cy=card-edit-pending]').should('contain', 'fecha dia 25');
    cy.get('[data-cy=card-edit-undo]').click();
    cy.get('[data-cy=card-notice]').should('contain', 'desfeita');
    row().find('[data-cy=card-cycle-pending]').should('not.exist');
  });
});
