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

  // 12) Confirmação (IMP-5): vira lançamento, chega ao Resumo pela projeção, e reimportar o mesmo
  // arquivo só traz duplicadas.
  const confirmRes = await fetch(`${GATEWAY}/api/imports/${batch.id}/confirm`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({ lines: (batch.lines ?? []).map((l) => ({ id: l.id, category: 'Smoke importado' })) }),
  });
  const confirmed = confirmRes.status === 200 ? await confirmRes.json() : {};
  ok(confirmed.status === 'Confirmed' && confirmed.summary?.imported === 2,
    `POST /api/imports/{id}/confirm criou os lancamentos (status ${confirmRes.status}, importadas=${confirmed.summary?.imported})`);
  let imported;
  const importStarted = Date.now();
  do {
    if (imported) await new Promise((r) => setTimeout(r, 1000));
    const res = await fetch(`${GATEWAY}/api/reports/summary?accountId=${account.id}`, { headers: auth });
    imported = res.status === 200 ? await res.json() : {};
  } while ((imported.totalExpense !== 354.5 || imported.totalIncome !== 100) && Date.now() - importStarted < 30_000);
  ok(imported.totalExpense === 354.5 && imported.totalIncome === 100,
    `resumo inclui os importados (gastos=${imported.totalExpense}, receitas=${imported.totalIncome}, em ${((Date.now() - importStarted) / 1000).toFixed(1)} s)`);
  const againRes = await upload(ofx, 'smoke.ofx');
  const again = againRes.status === 201 ? await againRes.json() : {};
  ok(again.summary?.duplicate === 2 && again.summary?.included === 0,
    `reimportar o mesmo arquivo so traz duplicadas (status ${againRes.status}, duplicadas=${again.summary?.duplicate})`);

  // 13) Desfazer (IMP-9): exclui os lançamentos do lote e o Resumo volta ao que era antes.
  const undoRes = await fetch(`${GATEWAY}/api/imports/${batch.id}/undo`, { method: 'POST', headers: auth });
  const undone = undoRes.status === 200 ? await undoRes.json() : {};
  ok(undone.status === 'Undone',
    `POST /api/imports/{id}/undo desfez o lote (status ${undoRes.status}, lote=${undone.status})`);
  let reverted;
  const undoStarted = Date.now();
  do {
    if (reverted) await new Promise((r) => setTimeout(r, 1000));
    const res = await fetch(`${GATEWAY}/api/reports/summary?accountId=${account.id}`, { headers: auth });
    reverted = res.status === 200 ? await res.json() : {};
  } while ((reverted.totalExpense !== 312.5 || reverted.totalIncome !== 0) && Date.now() - undoStarted < 30_000);
  ok(reverted.totalExpense === 312.5 && reverted.totalIncome === 0,
    `resumo sem os importados depois de desfazer (gastos=${reverted.totalExpense}, receitas=${reverted.totalIncome}, em ${((Date.now() - undoStarted) / 1000).toFixed(1)} s)`);
  // 14) Fatura do cartão (INV-4): compra no cartão aparece na fatura atual, com descrição e total.
  const cardRes = await fetch(`${GATEWAY}/api/credit-cards`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({ accountId: account.id, label: `Smoke ${Date.now()}`, brand: 'Visa', lastFourDigits: '4242', creditLimit: 5000, closingDay: 10, dueDay: 17 }),
  });
  const card = cardRes.status === 201 ? await cardRes.json() : {};
  const purchaseRes = await fetch(`${GATEWAY}/api/transactions`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({
      accountId: account.id, type: 'Expense', paymentMethod: 'CreditCard', creditCardId: card.id,
      description: 'Smoke cartão', category: 'Smoke cartão', amount: 77.7, currency: 'BRL',
      occurredOn: new Date().toISOString().slice(0, 10),
    }),
  });
  ok(cardRes.status === 201 && purchaseRes.status === 201,
    `cartao e compra no cartao criados (status ${cardRes.status}/${purchaseRes.status})`);
  let invoices;
  const invoiceStarted = Date.now();
  do {
    if (invoices) await new Promise((r) => setTimeout(r, 1000));
    const res = await fetch(`${GATEWAY}/api/reports/cards/${card.id}/invoices`, { headers: auth });
    invoices = res.status === 200 ? await res.json() : {};
  } while (!(invoices.invoices ?? []).some((i) => i.total === 77.7) && Date.now() - invoiceStarted < 30_000);
  const current = (invoices.invoices ?? []).find((i) => i.total === 77.7);
  ok(current?.status === 'Open' && current.items?.[0]?.description === 'Smoke cartão',
    `GET /api/reports/cards/{id}/invoices traz a compra na fatura ${current?.dueMonth} (${current?.status}, em ${((Date.now() - invoiceStarted) / 1000).toFixed(1)} s)`);
  const unknownCard = await fetch(`${GATEWAY}/api/reports/cards/${crypto.randomUUID()}/invoices`, { headers: auth });
  ok(unknownCard.status === 404, `fatura de cartao desconhecido responde 404 (status ${unknownCard.status})`);

  // 15) Pagamento de fatura (INV-6): abate a fatura, nao vira gasto, e excluir devolve o saldo.
  const invoiceOf = async (dueMonth, until) => {
    let found;
    const started = Date.now();
    do {
      if (found) await new Promise((r) => setTimeout(r, 1000));
      const res = await fetch(`${GATEWAY}/api/reports/cards/${card.id}/invoices`, { headers: auth });
      found = ((res.status === 200 ? await res.json() : {}).invoices ?? []).find((i) => i.dueMonth === dueMonth) ?? {};
    } while (!until(found) && Date.now() - started < 30_000);
    return found;
  };
  const payRes = await fetch(`${GATEWAY}/api/transactions/card-payments`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({
      creditCardId: card.id, invoiceDueMonth: current?.dueMonth, accountId: account.id, amount: 77.7,
      paidOn: new Date().toISOString().slice(0, 10),
    }),
  });
  const payment = payRes.status === 201 ? await payRes.json() : {};
  const paidInvoice = await invoiceOf(current?.dueMonth, (i) => i.paid === 77.7);
  ok(payRes.status === 201 && paidInvoice.paid === 77.7 && paidInvoice.remaining === 0
      && paidInvoice.payments?.[0]?.paymentId === payment.id,
    `POST /api/transactions/card-payments abate a fatura ${current?.dueMonth} (status ${payRes.status}, pago=${paidInvoice.paid}, falta=${paidInvoice.remaining})`);
  const afterPay = await (await fetch(`${GATEWAY}/api/reports/summary?accountId=${account.id}`, { headers: auth })).json();
  ok(Math.abs(afterPay.totalExpense - 390.2) < 0.001,
    `pagamento de fatura nao vira gasto no resumo (gastos=${afterPay.totalExpense})`);
  const unpayRes = await fetch(`${GATEWAY}/api/transactions/card-payments/${payment.id}`, { method: 'DELETE', headers: auth });
  const unpaid = await invoiceOf(current?.dueMonth, (i) => i.paid === 0);
  ok(unpayRes.status === 204 && unpaid.paid === 0 && unpaid.remaining === 77.7,
    `DELETE do pagamento devolve o saldo da fatura (status ${unpayRes.status}, pago=${unpaid.paid}, falta=${unpaid.remaining})`);

  // 16) Previsao de caixa (INV-8): a fatura do cartao entra no mes de vencimento, a pagar.
  const forecastRes = await fetch(`${GATEWAY}/api/reports/cash-forecast?months=3`, { headers: auth });
  const forecast = forecastRes.status === 200 ? await forecastRes.json() : {};
  const dueMonth = (forecast.months ?? []).find((m) => m.month === current?.dueMonth);
  ok(forecastRes.status === 200 && forecast.months?.length === 3 && dueMonth?.total.invoicesRemaining >= 77.7,
    `GET /api/reports/cash-forecast traz a fatura em ${current?.dueMonth} (status ${forecastRes.status}, faturas a pagar=${dueMonth?.total.invoicesRemaining})`);
  const badForecast = await fetch(`${GATEWAY}/api/reports/cash-forecast?months=13`, { headers: auth });
  ok(badForecast.status === 400, `previsao com 13 meses responde 400 (status ${badForecast.status})`);

  // 16b) Limite disponivel (INV-10): limite do cartao menos o que falta pagar (a compra de 77,70).
  const limitsRes = await fetch(`${GATEWAY}/api/reports/cards/limits`, { headers: auth });
  const cardLimit = ((limitsRes.status === 200 ? await limitsRes.json() : []).find((l) => l.cardId === card.id)) ?? {};
  ok(cardLimit.outstanding === 77.7 && Math.abs(cardLimit.availableLimit - 4922.3) < 0.001,
    `GET /api/reports/cards/limits: em aberto ${cardLimit.outstanding}, disponivel ${cardLimit.availableLimit} (status ${limitsRes.status})`);

  // 17) Compra parcelada (INV-5): so no cartao de credito, de 1 a 24; o valor e o total.
  const installmentPurchase = (paymentMethod, creditCardId, installments) => fetch(`${GATEWAY}/api/transactions`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({
      accountId: account.id, type: 'Expense', paymentMethod, creditCardId, installments,
      description: 'Smoke parcelado', category: 'Smoke cartão', amount: 300, currency: 'BRL',
      occurredOn: new Date().toISOString().slice(0, 10),
    }),
  });
  const parcelRes = await installmentPurchase('CreditCard', card.id, 3);
  const parcel = parcelRes.status === 201 ? await parcelRes.json() : {};
  const pixParcel = await installmentPurchase('Pix', undefined, 3);
  ok(parcel.installments === 3 && parcel.amount === 300 && pixParcel.status === 400,
    `POST /api/transactions em 3x no cartao (status ${parcelRes.status}, parcelas=${parcel.installments}); Pix parcelado = ${pixParcel.status}`);
  if (parcel.id) await fetch(`${GATEWAY}/api/transactions/${parcel.id}`, { method: 'DELETE', headers: auth });

  // 18) Exportacao contabil PJ (CTB-2/CTB-3): ZIP com lancamentos, pagamentos de fatura e resumo do mes.
  const pjRes = await fetch(`${GATEWAY}/api/accounts`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({ name: `PJ E2E ${Date.now()}`, scope: 'Business', taxId: '11.222.333/0001-81' }),
  });
  const pj = await pjRes.json();
  const month = new Date().toISOString().slice(0, 7);
  await fetch(`${GATEWAY}/api/transactions`, {
    method: 'POST', headers: auth,
    body: JSON.stringify({
      accountId: pj.id, type: 'Expense', paymentMethod: 'Boleto', description: 'Smoke contábil; NF 1',
      category: 'Software', amount: 1234.56, currency: 'BRL', occurredOn: new Date().toISOString().slice(0, 10),
    }),
  });
  const exportRes = await fetch(`${GATEWAY}/api/transactions/accounting-export?month=${month}&accountId=${pj.id}`, { headers: auth });
  const zip = Buffer.from(await exportRes.arrayBuffer());
  // Cada arquivo do ZIP: cabecalho local de 30 bytes + nome + extra, depois o conteudo (deflate).
  const { inflateRawSync } = await import('node:zlib');
  const files = {};
  for (let at = 0; exportRes.status === 200 && zip.readUInt32LE(at) === 0x04034b50;) {
    const [nameLength, extraLength, size] = [zip.readUInt16LE(at + 26), zip.readUInt16LE(at + 28), zip.readUInt32LE(at + 18)];
    const start = at + 30 + nameLength + extraLength;
    const body = zip.subarray(start, start + size);
    files[zip.subarray(at + 30, at + 30 + nameLength).toString()] =
      (zip.readUInt16LE(at + 8) === 8 ? inflateRawSync(body) : body).toString('utf8');
    at = start + size;
  }
  const csv = files['lancamentos.csv'] ?? '';
  ok(exportRes.status === 200 && csv.startsWith('﻿Data;Descrição')
    && csv.includes('"Smoke contábil; NF 1";Software;;Despesa;-1234,56;BRL;Boleto;;1;') && csv.includes('11.222.333/0001-81')
    && files['pagamentos-fatura.csv']?.startsWith('﻿Data;Cartão;Fatura')
    && files['resumo.csv']?.includes('BRL;Total;;0,00;1234,56;-1234,56')
    && exportRes.headers.get('content-disposition')?.includes(`contabil-11222333000181-${month}.zip`),
    `GET /api/transactions/accounting-export devolve o ZIP do mes PJ (status ${exportRes.status}, ${Object.keys(files).join(', ')})`);
  // O resumo do ZIP bate com o Resumo da conta PJ no reporting (a projecao chega alguns segundos depois).
  let pjSummary = {};
  for (let i = 0; i < 30 && pjSummary.totalExpense !== 1234.56; i++) {
    if (i > 0) await new Promise((r) => setTimeout(r, 1000));
    pjSummary = await (await fetch(`${GATEWAY}/api/reports/summary?accountId=${pj.id}`, { headers: auth })).json();
  }
  ok(pjSummary.totalExpense === 1234.56 && pjSummary.totalIncome === 0,
    `resumo do ZIP bate com GET /api/reports/summary da conta PJ (saidas ${pjSummary.totalExpense})`);
  // Previa (CTB-4): as mesmas quantidades e totais, sem o arquivo.
  const previewRes = await fetch(`${GATEWAY}/api/transactions/accounting-export/preview?month=${month}&accountId=${pj.id}`, { headers: auth });
  const preview = previewRes.status === 200 ? await previewRes.json() : {};
  ok(preview.lines === 1 && preview.totals?.[0]?.expense === 1234.56 && preview.fileName === `contabil-11222333000181-${month}.zip`,
    `GET /api/transactions/accounting-export/preview (status ${previewRes.status}, lancamentos=${preview.lines})`);
  // 19) Plano de contas (CTB-5): a categoria ganha um codigo, que sai na coluna "Conta contabil".
  // No MySQL real, "Software" e "software" sao categorias diferentes (collation binaria).
  const ledgerPut = (category, code) => fetch(`${GATEWAY}/api/transactions/ledger-accounts`, {
    method: 'PUT', headers: auth, body: JSON.stringify({ category, code, name: 'Smoke' }),
  });
  const [ledgerUpper, ledgerLower] = [await ledgerPut('Software', '3.1.02'), await ledgerPut('software', '9.9.99')];
  const ledgerPreview = await (await fetch(`${GATEWAY}/api/transactions/accounting-export/preview?month=${month}&accountId=${pj.id}`, { headers: auth })).json();
  const ledgerList = await (await fetch(`${GATEWAY}/api/transactions/ledger-accounts`, { headers: auth })).json();
  ok(ledgerUpper.status === 200 && ledgerLower.status === 200 && ledgerPreview.unmappedLines === 0
    && ledgerList.find((l) => l.category === 'Software')?.code === '3.1.02'
    && ledgerList.find((l) => l.category === 'software')?.code === '9.9.99',
    `PUT /api/transactions/ledger-accounts associa "Software" e "software" separadas (sem conta na previa: ${ledgerPreview.unmappedLines})`);
  await fetch(`${GATEWAY}/api/transactions/ledger-accounts?category=software`, { method: 'DELETE', headers: auth });
  await fetch(`${GATEWAY}/api/transactions/ledger-accounts?category=Software`, { method: 'DELETE', headers: auth });

  const pfExport = await fetch(`${GATEWAY}/api/transactions/accounting-export?month=${month}&accountId=${account.id}`, { headers: auth });
  ok(pfExport.status === 400, `exportacao contabil de conta PF responde 400 (status ${pfExport.status})`);

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
