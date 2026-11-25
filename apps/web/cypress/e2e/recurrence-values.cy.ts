// RJT-5 (ADR-0010): valores com vigencia e reajuste anual na aba Recorrencias. Uma regra que comecou ha
// um ano, com reajuste de 5% neste mes, mostra o reajuste pendente; confirmado, o valor de hoje passa a
// ser o reajustado. Um valor novo a partir de um mes futuro entra e sai da lista de vigencias, e editar a
// regra mantem o reajuste configurado.
describe('Recorrencias: valores e reajuste', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const now = new Date();
  const monthNames = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];
  const startedOn = `${now.getFullYear() - 1}-${pad(now.getMonth() + 1)}-01`;
  const thisMonthLabel = `${monthNames[now.getMonth()]}/${now.getFullYear()}`;
  const inTwoMonths = new Date(now.getFullYear(), now.getMonth() + 2, 1);
  const inTwoMonthsPeriod = `${inTwoMonths.getFullYear()}-${pad(inTwoMonths.getMonth() + 1)}`;

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  it('configura o reajuste, confirma o pendente e mantem valores por vigencia', () => {
    const [account, rent] = [`E2E Reajuste ${stamp}`, `Aluguel ${stamp}`];
    const ruleRow = () => cy.contains('[data-cy=rec-row]', rent);

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    // Cadastro com reajuste anual de 5% no mes de hoje (o padrao e o mes do inicio).
    cy.get('[data-cy=tab-recurrences]').click();
    cy.get('[data-cy=rec-account]').select(account);
    cy.get('[data-cy=rec-payment-method]').select('Boleto');
    cy.get('[data-cy=rec-description]').type(rent);
    cy.get('[data-cy=rec-category]').type(`Moradia ${stamp}`);
    cy.get('[data-cy=rec-amount]').type('1000');
    cy.get('[data-cy=rec-starts-on]').clear().type(startedOn);
    cy.get('[data-cy=rec-adjust-kind]').select('Fixed');
    cy.get('[data-cy=rec-adjust-month]').should('have.value', String(now.getMonth() + 1));
    cy.get('[data-cy=rec-adjust-rate]').type('5');
    cy.get('[data-cy=rec-submit]').click();
    ruleRow().find('[data-cy=rec-adjustment]').should('contain', `reajuste em ${monthNames[now.getMonth()]}: 5%`);

    // O aniversario e este mes: o reajuste aparece pendente com a sugestao; confirmado, vale o valor novo.
    ruleRow().find('[data-cy=rec-adjust-pending]').should('contain', thisMonthLabel).click();
    cy.get('[data-cy=rec-adjust-summary]').should('contain', '1.000,00').and('contain', '1.050,00').and('contain', '5%');
    cy.get('[data-cy=rec-adjust-value]').should('have.value', '5');
    cy.get('[data-cy=rec-adjust-confirm]').click();
    cy.get('[data-cy=rec-notice]').should('contain', `Reajuste de ${rent} confirmado`);
    ruleRow().find('[data-cy=rec-value]').should('contain', '1.050,00');
    ruleRow().find('[data-cy=rec-adjust-pending]').should('not.exist');
    cy.get('[data-cy=rec-adjust-form]').should('not.exist');
    cy.get('[data-cy=rec-amount-row]').should('have.length', 2);

    // Valor novo a partir de daqui a dois meses: entra na lista e, como ainda nao comecou, pode sair.
    cy.get('[data-cy=rec-amount-value]').type('1100');
    cy.get('[data-cy=rec-amount-from]').type(inTwoMonthsPeriod);
    cy.get('[data-cy=rec-amount-save]').click();
    cy.get(`[data-cy=rec-amount-row][data-period=${inTwoMonthsPeriod}]`).should('contain', '1.100,00');
    cy.get(`[data-cy=rec-amount-row][data-period=${inTwoMonthsPeriod}]`).find('[data-cy=rec-amount-remove]').click();
    cy.get(`[data-cy=rec-amount-row][data-period=${inTwoMonthsPeriod}]`).should('not.exist');
    cy.get('[data-cy=rec-amount-row]').should('have.length', 2);

    // Editar a regra traz o reajuste no formulario e o mantem.
    ruleRow().find('[data-cy=rec-edit]').click();
    cy.get('[data-cy=rec-adjust-kind]').should('have.value', 'Fixed');
    cy.get('[data-cy=rec-adjust-rate]').should('have.value', '5');
    cy.get('[data-cy=rec-notes]').clear().type('Contrato renovado');
    cy.get('[data-cy=rec-submit]').click();
    ruleRow().find('[data-cy=rec-adjustment]').should('contain', '5%');
    ruleRow().find('[data-cy=rec-value]').should('contain', '1.050,00');

    ruleRow().find('[data-cy=rec-remove]').click();
    cy.contains('[data-cy=rec-row]', rent).should('not.exist');
  });
});
