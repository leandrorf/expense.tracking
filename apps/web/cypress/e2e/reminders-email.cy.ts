// NTF-8 (ADR-0009, decisao 6): resumo diario por e-mail. O endereco e confirmado pelo link que chega no
// Mailpit (SMTP de desenvolvimento); confirmado, o job do reporting (a cada 20 s no compose) manda o
// resumo na hora escolhida, com o aviso de uma saida recorrente de amanha. A hora escolhida e a hora atual
// em Sao Paulo, o fuso do app. No fim o e-mail do demo e desligado (as preferencias valem para todos os specs).
describe('Resumo dos avisos por e-mail', () => {
  const pad = (n: number) => String(n).padStart(2, '0');
  const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  const today = new Date();
  const tomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
  const hourInSaoPaulo = Number(
    new Intl.DateTimeFormat('en-US', { timeZone: 'America/Sao_Paulo', hour: 'numeric', hourCycle: 'h23' }).format(today),
  );
  const mailpit = () => Cypress.env('mailpitUrl') as string;

  let stamp = 0;
  beforeEach(() => {
    stamp = Date.now();
    cy.login(Cypress.env('demoEmail'), Cypress.env('demoPassword'));
    cy.visit('/');
    cy.get('[data-cy=session-active]').should('be.visible');
  });

  afterEach(() => {
    cy.window().then((win) => {
      const token = JSON.parse(win.sessionStorage.getItem('expense.tokens')!).accessToken as string;
      const headers = { Authorization: `Bearer ${token}` };
      const api = `${Cypress.env('gatewayUrl')}/api/reports/reminder-settings`;
      cy.request({ method: 'PUT', url: `${api}/email`, headers, body: { email: null, enabled: false, sendHour: 8 } });
      cy.request({ method: 'PUT', url: api, headers, body: { leadDays: 3, invoices: true, recurrences: true, budgets: true } });
      // As regras deste spec saem mesmo se o teste falhar no meio (senao entram no resumo da proxima vez).
      const rules = `${Cypress.env('gatewayUrl')}/api/transactions/recurrences`;
      cy.request({ url: rules, headers }).then(({ body }) => {
        (body as Array<{ id: string; description: string }>)
          .filter((r) => r.description.startsWith('Academia Resumo '))
          .forEach((r) => cy.request({ method: 'DELETE', url: `${rules}/${r.id}`, headers }));
      });
    });
  });

  /** Espera um e-mail para `to` cujo assunto comeca com `subject`, e devolve o texto dele. */
  const waitForEmail = (to: string, subject: string, attempts = 60): Cypress.Chainable<string> =>
    cy.request(`${mailpit()}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}`).then(({ body }) => {
      const found = (body.messages as Array<{ ID: string; Subject: string }>).find((m) => m.Subject.startsWith(subject));
      if (found) {
        return cy.request(`${mailpit()}/api/v1/message/${found.ID}`).then(({ body: message }) => message.Text as string);
      }
      if (attempts <= 1) throw new Error(`Nenhum e-mail "${subject}" para ${to}.`);
      cy.wait(2_000);
      return waitForEmail(to, subject, attempts - 1);
    });

  it('confirma o endereco pelo link e recebe o resumo com o aviso', () => {
    const [account, gym, address] = [`E2E Resumo ${stamp}`, `Academia Resumo ${stamp}`, `e2e-${stamp}@expense.local`];

    cy.get('[data-cy=tab-accounts]').click();
    cy.get('[data-cy=account-name]').type(account);
    cy.get('[data-cy=account-submit]').click();
    cy.get('[data-cy=accounts-table]').contains('td', account).should('be.visible');

    cy.get('[data-cy=tab-recurrences]').click();
    cy.get('[data-cy=rec-account]').select(account);
    cy.get('[data-cy=rec-payment-method]').select('Pix');
    cy.get('[data-cy=rec-description]').type(gym);
    cy.get('[data-cy=rec-category]').type(`Saude ${stamp}`);
    cy.get('[data-cy=rec-amount]').type('150');
    cy.get('[data-cy=rec-starts-on]').clear().type(iso(tomorrow));
    cy.get('[data-cy=rec-submit]').click();
    cy.contains('[data-cy=rec-row]', gym).should('exist');

    // Espera a regra chegar a projecao do reporting (o aviso aparece no sino): so entao confirma o
    // e-mail, porque o resumo pode sair logo depois da confirmacao.
    const waitForReminder = (attempts = 30) => {
      cy.intercept('GET', '**/api/reports/reminders').as('reminders');
      cy.get('[data-cy=reminders-bell]').click();
      cy.wait('@reminders');
      cy.get('[data-cy=reminders-panel]').then(($panel) => {
        if ($panel.find(`[data-cy=reminder-item]:contains("${gym}")`).length > 0) return;
        if (attempts <= 1) throw new Error(`O aviso "${gym}" nao apareceu.`);
        cy.get('[data-cy=reminders-bell]').click();
        cy.wait(1_000);
        waitForReminder(attempts - 1);
      });
    };
    waitForReminder();

    // Preferencias: endereco, ligado, na hora atual. O link de confirmacao vai para o Mailpit.
    cy.get('[data-cy=reminders-settings-toggle]').click();
    cy.get('[data-cy=reminders-email]').clear().type(address);
    cy.get('[data-cy=reminders-email-enabled]').check();
    cy.get('[data-cy=reminders-email-hour]').select(String(hourInSaoPaulo));
    cy.get('[data-cy=reminders-settings-save]').click();
    cy.get('[data-cy=reminders-email-notice]').should('contain', `link de confirmação para ${address}`);
    cy.get('[data-cy=reminders-email-status]').should('have.attr', 'data-confirmed', 'false');

    // O link abre a SPA, que confirma o endereco com a sessao do usuario.
    waitForEmail(address, 'Confirme o e-mail').then((text) => {
      const link = /http:\/\/[^\s]+\/confirm-email\?token=[0-9a-f]+/.exec(text)![0];
      cy.visit(new URL(link).pathname + new URL(link).search);
    });
    cy.get('[data-cy=email-confirmation]').should('have.attr', 'data-ok', 'true').and('contain', address);
    cy.get('[data-cy=reminders-bell]').click();
    cy.get('[data-cy=reminders-settings-toggle]').click();
    cy.get('[data-cy=reminders-email-status]').should('have.attr', 'data-confirmed', 'true');

    // O job manda o resumo com o aviso da academia de amanha.
    waitForEmail(address, 'Expense Tracking:').then((text) => {
      expect(text).to.contain(`${gym}: vence em ${pad(tomorrow.getDate())}/${pad(tomorrow.getMonth() + 1)}, R$ 150,00`);
      expect(text).to.contain('http://localhost:5173/');
    });

  });
});
