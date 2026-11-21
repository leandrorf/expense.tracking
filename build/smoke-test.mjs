// Validação E2E do stack: login -> authorize+PKCE -> token -> gateway (accounts/transactions/reporting).
import crypto from 'node:crypto';

const IDENTITY = process.env.IDENTITY ?? 'http://localhost:5080';
const GATEWAY = process.env.GATEWAY ?? 'http://localhost:8080';
const EMAIL = 'demo@expense.local';
const PASSWORD = 'Password123!';
const CLIENT_ID = 'expense-tracking-spa';
const REDIRECT = 'http://localhost:5173/callback';

const b64url = (buf) => buf.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
let pass = 0, fail = 0;
const ok = (cond, msg) => { if (cond) { console.log(`  ✓ ${msg}`); pass++; } else { console.log(`  ✗ ${msg}`); fail++; } };

async function main() {
  // 1) Login (cookie de sessão)
  const loginRes = await fetch(`${IDENTITY}/account/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: EMAIL, password: PASSWORD }),
  });
  ok(loginRes.status === 200, `login do usuario demo (status ${loginRes.status})`);
  const setCookies = loginRes.headers.getSetCookie?.() ?? [];
  const cookie = setCookies.map((c) => c.split(';')[0]).join('; ');
  ok(cookie.length > 0, 'cookie de sessao recebido');

  // 2) Authorize + PKCE
  const verifier = b64url(crypto.randomBytes(32));
  const challenge = b64url(crypto.createHash('sha256').update(verifier).digest());
  const state = b64url(crypto.randomBytes(16));
  const authorizeUrl = `${IDENTITY}/oauth/authorize?response_type=code&client_id=${CLIENT_ID}`
    + `&redirect_uri=${encodeURIComponent(REDIRECT)}`
    + `&scope=${encodeURIComponent('openid profile expenses.read expenses.write offline_access')}`
    + `&state=${state}&code_challenge=${challenge}&code_challenge_method=S256`;
  const authRes = await fetch(authorizeUrl, { headers: { Cookie: cookie }, redirect: 'manual' });
  const location = authRes.headers.get('location') ?? '';
  ok(authRes.status >= 300 && authRes.status < 400, `authorize retornou redirect (status ${authRes.status})`);
  const code = new URL(location, REDIRECT).searchParams.get('code');
  ok(!!code, `authorization code emitido`);

  // 3) Token exchange
  const tokenRes = await fetch(`${IDENTITY}/oauth/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code', code, redirect_uri: REDIRECT, client_id: CLIENT_ID, code_verifier: verifier,
    }),
  });
  const token = await tokenRes.json();
  ok(tokenRes.status === 200 && !!token.access_token, `token obtido (expires_in=${token.expires_in})`);
  const auth = { Authorization: `Bearer ${token.access_token}`, 'Content-Type': 'application/json' };

  // 4) Gateway -> accounts (lista vazia ou existente)
  const listRes = await fetch(`${GATEWAY}/api/accounts`, { headers: auth });
  ok(listRes.status === 200, `GET /api/accounts via gateway (status ${listRes.status})`);

  // 5) Criar conta
  const accRes = await fetch(`${GATEWAY}/api/accounts`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({ name: `Conta E2E ${Date.now()}`, scope: 'Personal' }),
  });
  const account = await accRes.json();
  ok(accRes.status === 201 && !!account.id, `POST /api/accounts criou conta (${account.id})`);

  // 6) Criar lançamento (transactions valida a conta no accounts)
  const txRes = await fetch(`${GATEWAY}/api/transactions`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({
      accountId: account.id, type: 'Expense', paymentMethod: 'Pix',
      description: 'Café E2E', category: 'Alimentacao', amount: 12.5,
      currency: 'BRL', occurredOn: new Date().toISOString().slice(0, 10),
    }),
  });
  const tx = await txRes.json();
  ok(txRes.status === 201 && !!tx.id, `POST /api/transactions criou lancamento (status ${txRes.status})`);

  // 7) Resumo da conta nova. O reporting le a projecao (EVT-3), que recebe o lancamento alguns
  // segundos depois (margem do feed + polling do worker): consulta ate refletir, por ate 30 s.
  let sumRes;
  let summary;
  const started = Date.now();
  do {
    if (summary) await new Promise((r) => setTimeout(r, 1000));
    sumRes = await fetch(`${GATEWAY}/api/reports/summary?accountId=${account.id}`, { headers: auth });
    summary = await sumRes.json();
  } while (sumRes.status === 200 && summary.totalExpense !== 12.5 && Date.now() - started < 30_000);
  ok(sumRes.status === 200, `GET /api/reports/summary (status ${sumRes.status})`);
  ok(summary.totalExpense === 12.5,
    `resumo reflete o gasto da conta nova (totalExpense=${summary.totalExpense}, em ${((Date.now() - started) / 1000).toFixed(1)} s)`);
  console.log('  → byCategory:', JSON.stringify(summary.byCategory));

  // 8) Orçamento (BDG-2). Categoria única por execução: o demo é persistente e a categoria é
  // única por dono + escopo.
  const budgetCategory = `Smoke ${Date.now()}`;
  const budgetRes = await fetch(`${GATEWAY}/api/budgets`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({ category: budgetCategory, scope: 'Personal', amount: 1500, startMonth: new Date().toISOString().slice(0, 7) }),
  });
  const budget = await budgetRes.json();
  ok(budgetRes.status === 201 && !!budget.id, `POST /api/budgets criou orcamento (status ${budgetRes.status})`);
  const budgetsRes = await fetch(`${GATEWAY}/api/budgets`, { headers: auth });
  const budgets = budgetsRes.status === 200 ? await budgetsRes.json() : [];
  ok(budgets.some((b) => b.id === budget.id), `GET /api/budgets lista o orcamento novo (status ${budgetsRes.status})`);

  // 9) Vigência e encerramento (BDG-3): o valor muda a partir do mês seguinte e o orçamento é
  // encerrado dois meses depois; cada mês mostra o valor que vale nele.
  const monthAfter = (n) => { const d = new Date(); d.setUTCDate(1); d.setUTCMonth(d.getUTCMonth() + n); return d.toISOString().slice(0, 7); };
  const amountIn = async (month) => {
    const res = await fetch(`${GATEWAY}/api/budgets?month=${month}`, { headers: auth });
    return (await res.json()).find((b) => b.id === budget.id)?.amount;
  };
  const changeRes = await fetch(`${GATEWAY}/api/budgets/${budget.id}/amount`, {
    method: 'PUT', headers: auth, body: JSON.stringify({ amount: 1800, fromMonth: monthAfter(1) }),
  });
  ok(changeRes.status === 200, `PUT /api/budgets/{id}/amount alterou o valor (status ${changeRes.status})`);
  const closeRes = await fetch(`${GATEWAY}/api/budgets/${budget.id}/close`, {
    method: 'POST', headers: auth, body: JSON.stringify({ fromMonth: monthAfter(2) }),
  });
  ok(closeRes.status === 200, `POST /api/budgets/{id}/close encerrou o orcamento (status ${closeRes.status})`);
  const timeline = [await amountIn(monthAfter(0)), await amountIn(monthAfter(1)), await amountIn(monthAfter(2))];
  ok(timeline[0] === 1500 && timeline[1] === 1800 && timeline[2] === undefined,
    `valor por mes respeita vigencia e encerramento (${JSON.stringify(timeline)})`);

  // 10) Acompanhamento do mês (BDG-4): um gasto na categoria do orçamento (variando maiúsculas)
  // aparece no consumido depois que a projeção o recebe.
  const budgetTxRes = await fetch(`${GATEWAY}/api/transactions`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({
      accountId: account.id, type: 'Expense', paymentMethod: 'Cash',
      description: 'Gasto do orcamento', category: budgetCategory.toUpperCase(), amount: 300,
      currency: 'BRL', occurredOn: new Date().toISOString().slice(0, 10),
    }),
  });
  ok(budgetTxRes.status === 201, `POST /api/transactions na categoria do orcamento (status ${budgetTxRes.status})`);
  let status;
  const statusStarted = Date.now();
  do {
    if (status) await new Promise((r) => setTimeout(r, 1000));
    const res = await fetch(`${GATEWAY}/api/budgets/status?month=${monthAfter(0)}`, { headers: auth });
    status = res.status === 200 ? (await res.json()).budgets.find((b) => b.budgetId === budget.id) ?? {} : {};
  } while (status.spent !== 300 && Date.now() - statusStarted < 30_000);
  ok(status.spent === 300 && status.remaining === 1200 && status.status === 'OnTrack',
    `GET /api/budgets/status reflete o gasto (spent=${status.spent}, remaining=${status.remaining}, status=${status.status}, em ${((Date.now() - statusStarted) / 1000).toFixed(1)} s)`);

  // 11) Importação (IMP-4): envia um OFX sintético e lê a prévia; arquivo grande demais é barrado.
  const day = new Date().toISOString().slice(0, 10).replaceAll('-', '');
  const ofx = ['OFXHEADER:100', 'DATA:OFXSGML', 'VERSION:102', 'CHARSET:1252', '',
    '<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>BRL<BANKTRANLIST>',
    `<STMTTRN><DTPOSTED>${day}<TRNAMT>-42.00<FITID>SMOKE-${Date.now()}-1<MEMO>Smoke import</STMTTRN>`,
    `<STMTTRN><DTPOSTED>${day}<TRNAMT>100.00<FITID>SMOKE-${Date.now()}-2<MEMO>Smoke receita</STMTTRN>`,
    '</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>'].join('\r\n');
  const upload = (content, name) => {
    const form = new FormData();
    form.append('file', new Blob([content]), name);
    form.append('accountId', account.id);
    form.append('paymentMethod', 'Pix');
    return fetch(`${GATEWAY}/api/imports`, {
      method: 'POST', headers: { Authorization: auth.Authorization }, body: form,
      signal: AbortSignal.timeout(30_000),
    });
  };
  const importRes = await upload(ofx, 'smoke.ofx');
  const batch = importRes.status === 201 ? await importRes.json() : {};
  ok(importRes.status === 201 && batch.status === 'Pending' && batch.summary?.new === 2,
    `POST /api/imports criou o lote pendente (status ${importRes.status}, novas=${batch.summary?.new})`);
  const batchRes = await fetch(`${GATEWAY}/api/imports/${batch.id}`, { headers: auth });
  ok(batchRes.status === 200 && (await batchRes.json()).lines?.length === 2,
    `GET /api/imports/{id} devolve a previa (status ${batchRes.status})`);
  // O gateway responde antes de receber o arquivo inteiro e fecha a conexao; o fetch do Node as
  // vezes reporta isso como erro de rede em vez de entregar a resposta. Os dois sao recusa.
  const big = await upload(new Uint8Array(6 * 1024 * 1024).fill(97), 'grande.ofx')
    .then((res) => `status ${res.status}`, (err) => (err.name === 'TimeoutError' ? 'timeout' : 'conexao encerrada'));
  ok(big === 'status 400' || big === 'status 413' || big === 'conexao encerrada',
    `POST /api/imports com 6 MB e recusado (${big})`);

  console.log(`\nRESULTADO: ${pass} passaram, ${fail} falharam`);
  process.exit(fail === 0 ? 0 : 1);
}

main().catch((e) => { console.error('ERRO:', e.message); process.exit(1); });
