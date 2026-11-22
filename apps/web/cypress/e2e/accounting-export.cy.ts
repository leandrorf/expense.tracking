// CTB-4: tela "Exportar para o contador". O usuario demo ja tem contas PJ (de outras execucoes),
// entao o teste cobre a tela: mes anterior por padrao, previa do mes, mes sem movimento e o download
// do ZIP com o nome que a previa mostra. A jornada completa (lancar, exportar, conferir) e a CTB-7.
describe('Exportacao contabil', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const today = new Date();
  const previous = new Date(today.getFullYear(), today.getMonth() - 1, 1);
  const previousMonth = `${previous.getFullYear()}-${pad(previous.getMonth() + 1)}`;

  beforeEach(() => {
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('mostra a previa do mes anterior e baixa o ZIP', () => {
    // Garante uma conta PJ, mesmo num banco recem-criado.
    cy.get('[data-cy=tab-accounts]').click();
    const accountName = `E2E Contador ${Date.now()}`;
    cy.get('[data-cy=account-name]').type(accountName);
    cy.get('[data-cy=account-scope]').select('Business');
    cy.get('[data-cy=account-taxid]').type('11.222.333/0001-81');
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', accountName).should('be.visible');

    cy.intercept('GET', '**/api/transactions/accounting-export/preview*').as('preview');
    cy.get('[data-cy=tab-accounting]').click();
    cy.get('[data-cy=accounting-month]').should('have.value', previousMonth);
    cy.wait('@preview').its('request.url').should('include', `month=${previousMonth}`);
    cy.get('[data-cy=accounting-preview]').should('be.visible');

    // Mes sem movimento: nada a exportar, mas o ZIP sai (so com os cabecalhos).
    cy.get('[data-cy=accounting-month]').clear().type('2001-01');
    cy.wait('@preview');
    cy.get('[data-cy=accounting-lines]').should('have.text', '0');
    cy.get('[data-cy=accounting-payments]').should('have.text', '0');
    cy.get('[data-cy=accounting-totals]').should('not.exist');

    cy.intercept('GET', '**/api/transactions/accounting-export?*').as('download');
    cy.get('[data-cy=accounting-download]')
      .should('contain.text', '2001-01.zip')
      .invoke('text')
      .then((text) => {
        const fileName = text.replace('Baixar ', '').trim();
        cy.get('[data-cy=accounting-download]').click();
        cy.wait('@download').its('response.statusCode').should('eq', 200);
        // ZIP comeca com "PK".
        cy.readFile(`${Cypress.config('downloadsFolder')}/${fileName}`, null).should((content: Uint8Array) => {
          expect(content[0]).to.eq(0x50);
          expect(content[1]).to.eq(0x4b);
        });
      });
  });
});
