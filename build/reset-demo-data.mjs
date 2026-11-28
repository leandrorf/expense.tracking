#!/usr/bin/env node
// Apaga os dados do usuario demo no stack LOCAL (docker compose), para os testes E2E e o smoke nao
// acumularem contas, cartoes e lancamentos de uma execucao para a outra (o demo e compartilhado e
// persistente). O CI nao precisa: ele sobe o stack do zero e o derruba com `down -v`.
//
// Uso:  node build/reset-demo-data.mjs          -> so mostra o que seria apagado
//       node build/reset-demo-data.mjs --yes    -> apaga
//
// O usuario (identity) fica; somem os dados dele no accounts, no transactions (inclusive os eventos
// do outbox, para uma reconstrucao da projecao nao trazer nada de volta) e no reporting (projecoes,
// orcamentos e avisos). Os outros usuarios nao sao tocados.
import { execFileSync } from 'node:child_process';

const EMAIL = process.env.DEMO_EMAIL ?? 'demo@expense.local';
const APPLY = process.argv.includes('--yes');

if (!/^[^'"\\\s]+@[^'"\\\s]+$/.test(EMAIL)) {
  console.error(`E-mail invalido: ${EMAIL}`);
  process.exit(1);
}

/** Roda SQL no banco do container (o mysql do proprio container, com as credenciais dele). */
function sql(container, statements) {
  return execFileSync(
    'docker',
    ['exec', '-i', container, 'sh', '-c', 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD" -N -B "$MYSQL_DATABASE" 2>/dev/null'],
    { input: statements, encoding: 'utf8' },
  ).trim();
}

const owner = sql('expense-identity-db', `select id from users where email = '${EMAIL}';`);
if (!/^[0-9a-f-]{36}$/i.test(owner)) {
  console.error(`Usuario ${EMAIL} nao encontrado no identity local (o stack esta de pe?).`);
  process.exit(1);
}

const mine = `owner_user_id = '${owner}'`;
const counts = () => ({
  contas: sql('expense-accounts-db', `select count(*) from accounts where ${mine};`),
  cartoes: sql('expense-accounts-db', `select count(*) from credit_cards where ${mine};`),
  lancamentos: sql('expense-transactions-db', `select count(*) from transactions where ${mine};`),
  recorrencias: sql('expense-transactions-db', `select count(*) from recurrences where ${mine};`),
  importacoes: sql('expense-transactions-db', `select count(*) from import_batches where ${mine};`),
  eventos: sql('expense-transactions-db', `select count(*) from outbox_events where ${mine};`),
  orcamentos: sql('expense-reporting-db', `select count(*) from budgets where ${mine};`),
  metas: sql('expense-reporting-db', `select count(*) from monthly_goals where ${mine};`),
  projecao: sql('expense-reporting-db', `select count(*) from projected_transactions where ${mine};`),
});

console.log(`Usuario demo: ${EMAIL} (${owner})`);
console.log('Antes:', counts());
if (!APPLY) {
  console.log('Nada foi apagado. Rode com --yes para apagar.');
  process.exit(0);
}

// Filhos antes dos pais; cada banco numa transacao.
sql('expense-accounts-db', `start transaction;
delete from credit_cards where ${mine};
delete from accounts where ${mine};
commit;`);

const myRules = `select id from recurrences where ${mine}`;
sql('expense-transactions-db', `start transaction;
delete from import_lines where batch_id in (select id from import_batches where ${mine});
delete from import_batches where ${mine};
delete from recurrence_adjustments where recurrence_id in (${myRules});
delete from recurrence_amounts where recurrence_id in (${myRules});
delete from recurrence_occurrences where ${mine};
update transactions set recurrence_id = null where ${mine};
delete from recurrences where ${mine};
delete from card_payments where ${mine};
delete from transactions where ${mine};
delete from csv_mappings where ${mine};
delete from ledger_accounts where ${mine};
delete from accounting_exports where ${mine};
delete from outbox_events where ${mine};
commit;`);

const myProjectedRules = `select recurrence_id from projected_recurrences where ${mine}`;
sql('expense-reporting-db', `start transaction;
delete from budget_limits where budget_id in (select id from budgets where ${mine});
delete from budgets where ${mine};
delete from monthly_goal_terms where goal_id in (select id from monthly_goals where ${mine});
delete from monthly_goals where ${mine};
delete from projected_recurrence_amounts where recurrence_id in (${myProjectedRules});
delete from projected_recurrence_occurrences where ${mine};
delete from projected_recurrences where ${mine};
delete from projected_card_payments where ${mine};
delete from projected_transactions where ${mine};
delete from reminder_dismissals where ${mine};
delete from reminder_settings where ${mine};
commit;`);

console.log('Depois:', counts());
