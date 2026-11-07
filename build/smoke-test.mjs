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

  // 7) Resumo
  const sumRes = await fetch(`${GATEWAY}/api/reports/summary`, { headers: auth });
  const summary = await sumRes.json();
  ok(sumRes.status === 200, `GET /api/reports/summary (status ${sumRes.status})`);
  ok(summary.totalExpense >= 12.5, `resumo reflete o gasto (totalExpense=${summary.totalExpense})`);
  console.log('  → byCategory:', JSON.stringify(summary.byCategory));

  console.log(`\nRESULTADO: ${pass} passaram, ${fail} falharam`);
  process.exit(fail === 0 ? 0 : 1);
}

main().catch((e) => { console.error('ERRO:', e.message); process.exit(1); });
