// MET-7 (ADR-0013): aba Metas. A meta do mes PF e definida pela tela e aparece com a forma e os numeros do mes;
// um objetivo e criado, recebe um aporte, recusa uma retirada maior que o guardado, e e arquivado e reaberto.
// O usuario demo e persistente: a meta PF e a mesma a cada execucao (a tela so a muda) e o objetivo tem nome unico.
describe('Metas', () => {
  beforeEach(() => {
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
    cy.get('[data-cy=tab-goals]').click();
  });

  it('define a meta do mes e acompanha um objetivo com aportes', () => {
    const stamp = Date.now();
    const name = `Viagem ${stamp}`;
    const pf = () => cy.get('[data-cy=goal-scope-personal]');

    // Meta do mes PF: guardar 1.000 a partir do mes atual.
    pf().find('[data-cy=goal-edit-open]').click();
    pf().find('[data-cy=goal-kind]').select('SaveAmount');
    pf().find('[data-cy=goal-value]').clear().type('1000');
    pf().find('[data-cy=goal-save]').click();
    cy.get('[data-cy=goal-notice]').should('contain', 'Meta PF');
    pf().find('[data-cy=goal-kind-label]').should('contain', 'Guardar').and('contain', '1.000,00');
    pf().find('[data-cy=goal-target]').should('contain', '1.000,00');
    pf().find('[data-cy=goal-can-spend]').should('exist');
    pf().should('not.have.attr', 'data-situation', 'none');
    // O PJ mostra os numeros do mes, com ou sem meta.
    cy.get('[data-cy=goal-scope-business] [data-cy=goal-result]').should('exist');

    // Um objetivo de 1.200, sem prazo: aporte de 300 e uma retirada maior que o guardado.
    cy.get('[data-cy=objective-scope]').select('personal');
    cy.get('[data-cy=objective-name]').type(name);
    cy.get('[data-cy=objective-target]').type('1200');
    cy.get('[data-cy=objective-submit]').click();
    const row = () => cy.get(`[data-cy=objective-row][data-name="${name}"]`);
    row().find('[data-cy=objective-saved]').should('contain', '0,00 de');

    row().find('[data-cy=contribution-open]').click();
    row().find('[data-cy=contribution-amount]').type('300');
    row().find('[data-cy=contribution-note]').type('primeiro aporte');
    row().find('[data-cy=contribution-submit]').click();
    row().find('[data-cy=objective-saved]').should('contain', '300,00');
    row().find('[data-cy=objective-missing]').should('contain', '900,00');
    // Criado hoje: o ritmo e o aporte do mes e a chegada fica 3 meses a frente.
    row().find('[data-cy=objective-pace]').should('contain', '300,00');
    row().should('have.attr', 'data-situation', 'OnTrack');

    row().find('[data-cy=contribution-open]').click();
    row().find('[data-cy=contribution-direction]').select('withdraw');
    row().find('[data-cy=contribution-amount]').type('500');
    row().find('[data-cy=contribution-submit]').click();
    row().find('[data-cy=objective-error]').should('contain', 'negativo');
    row().find('[data-cy=objective-saved]').should('contain', '300,00');

    // Arquivar tira da lista; "mostrar arquivados" traz de volta para reabrir.
    row().find('[data-cy=objective-archive]').click();
    row().should('not.exist');
    cy.get('[data-cy=objectives-show-archived]').check();
    row().should('have.attr', 'data-archived', 'true');
    row().find('[data-cy=objective-unarchive]').click();
    row().should('have.attr', 'data-archived', 'false');
    row().find('[data-cy=objective-archive]').click();
  });
});
