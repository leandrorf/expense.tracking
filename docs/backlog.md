# Backlog — expense.tracking (projeto Jira SCRUM)

> Fonte: `docs/PROMPT.md`. Este backlog reflete o estado real (projeto já majoritariamente
> construído). Épicos 🟢 são **retroativos** (marcar como **Done** no Jira, para rastreabilidade);
> épicos 🔵 são o **trabalho futuro**. Materializar no projeto **SCRUM** quando o conector
> Atlassian estiver conectado. Estimativas em story points (Fibonacci).

Legenda de status: 🟢 Done · 🔵 To Do · 🟡 In Progress

---

## 🟢 EPIC INFRA — Fundação, Docker & Gateway  · *Done*
Monorepo, padrões e entrada única das APIs.

- 🟢 **INFRA-1** Monorepo .NET 10 com package mgmt central (`Directory.Packages.props`), `.slnx`, `NuGet.config` fixo. · 3
- 🟢 **INFRA-2** `docker-compose` sobe MySQL + todos os serviços + web. · 3
- 🟢 **INFRA-3** API Gateway (YARP) roteando `/api/**` para os serviços de recurso. · 5
- 🟢 **INFRA-4** Smoke-test ponta a ponta (`build/smoke-test.mjs`) validando o fluxo pelo gateway. · 2

## 🟢 EPIC IDENT — Identity / OAuth2 próprio  · *Done*
Servidor OAuth2/OIDC sem bibliotecas de terceiros.

- 🟢 **IDENT-1** Authorization Code + PKCE. · 8
- 🟢 **IDENT-2** Refresh token com rotação. · 5
- 🟢 **IDENT-3** Client Credentials. · 3
- 🟢 **IDENT-4** JWT RS256 + JWKS + discovery (`/.well-known/openid-configuration`). · 5
- 🟢 **IDENT-5** Tela de login do IdP + seed (cliente SPA, cliente serviço, usuário demo). · 3

## 🟢 EPIC ACCT — Accounts (PF/PJ, cartões, meios de pagamento)  · *Done*
- 🟢 **ACCT-1** Contas PF e PJ. · 5
- 🟢 **ACCT-2** Cartões de crédito. · 3
- 🟢 **ACCT-3** Catálogo de meios de pagamento com contrato único (`code` = nome do enum). · 3
- 🟢 **ACCT-4** Proteção JWT (MetadataAddress interno + ValidIssuer público) + testes com TestAuthHandler. · 3

## 🟢 EPIC TXN — Transactions (lançamentos de gastos)  · *Done*
- 🟢 **TXN-1** Lançar gasto PF/PJ com `Money` VO e meio de pagamento. · 5
- 🟢 **TXN-2** Validar conta/cartão chamando o `accounts` via HTTP (encaminha token; porta `IAccountsClient` + fake nos testes). · 5
- 🟢 **TXN-3** Banco `expense_transactions`; `DateOnly` via converter. · 2

## 🟢 EPIC RPT — Reporting (consolidação)  · *Done*
- 🟢 **RPT-1** Agregador sem banco: lê transactions + accounts por HTTP (token encaminhado). · 5
- 🟢 **RPT-2** `SummaryCalculator` puro; endpoint `/reports/summary` por categoria/escopo PF-PJ/meio. · 3

## 🟢 EPIC WEB — SPA React (login + telas)  · *Done*
- 🟢 **WEB-1** Login OAuth2 + PKCE + callback; tokens no sessionStorage. · 5
- 🟢 **WEB-2** Abas Contas / Cartões / Lançamentos consumindo as APIs pelo gateway. · 8
- 🟢 **WEB-3** Aba Resumo (consolidado). · 3
- 🟢 **WEB-4** Refresh automático de token em 401. · 3

## 🟢 EPIC E2E — Testes EndToEnd (Cypress)  · *Done*
- 🟢 **E2E-1** `cy.login` com `cy.origin` (login cross-origin no IdP) + `cy.session`; atributos `data-cy`. · 5
- 🟢 **E2E-2** Spec jornada PF / Pix até o Resumo. · 3
- 🟢 **E2E-3** Spec jornada PJ / cartão até o Resumo. · 3

---

## 🟢 EPIC CI — Pipeline de Integração Contínua  · *Done*
**Objetivo:** cada push roda a bateria de testes e barra merge quebrado.

- 🟢 **CI-1** Workflow de build + testes unitários + integração (xUnit) em cada push/PR.
  - *Dado* um push em qualquer branch, *quando* o CI roda, *então* compila a solução e executa todos os testes unit+integração, falhando o job se algum teste falhar.
  - Entregue em `.github/workflows/ci.yml` (matriz por serviço: accounts, gateway, identity, reporting, transactions). PR #1 mergeado, CI verde no `main`.
  - Estimativa: 5
- 🟢 **CI-2** Job de E2E (Cypress) subindo o stack via `docker compose` e rodando `npm run e2e`.
  - *Dado* um PR para `main`, *quando* o CI roda, *então* sobe o stack, aguarda health dos serviços e executa os specs Cypress headless, publicando artefatos (vídeos/prints) em falha.
  - Entregue em `.github/workflows/e2e.yml` (job `Cypress E2E`, `pull_request` → `main` + `workflow_dispatch`).
  - Estimativa: 8
- 🟢 **CI-3** Branch protection / gate de PR exigindo CI verde.
  - *Dado* um PR, *quando* o CI não está verde, *então* o merge é bloqueado.
  - Aplicado como **Ruleset Active** no `main`: 6 required status checks (`build & test (accounts|gateway|identity|reporting|transactions)` + `Cypress E2E`), `strict` (up to date), sem force-push/deleção. **Bypass:** admin do repo (push direto do dono não é barrado — decisão consciente para projeto solo); o gate vale para PRs e demais atores.
  - Procedência: configurado e confirmado na UI do GitHub pelo dono do repo; o token `gh` desta sessão é read-only e não inspeciona rulesets. Prova automática: os required checks aparecem no próximo PR para `main`.
  - Estimativa: 2

## 🟡 EPIC OBS — Observabilidade  · *In Progress*
**Objetivo:** enxergar o que acontece entre os serviços.

- 🟢 **OBS-1** Logs estruturados (JSON) com correlation id propagado pelo gateway.
  - *Dado* uma requisição pelo gateway, *quando* passa pelos serviços, *então* todos os logs carregam o mesmo correlation id.
  - Entregue: logger JSON nativo (`AddJsonConsole`, `IncludeScopes`) + `ObservabilitySetup` por serviço. Middleware resolve o `X-Correlation-Id` (inbound → trace id → GUID), ecoa na resposta e injeta num log scope ambiente. Propagação: gateway reescreve o header antes do YARP; `transactions`/`reporting` usam `CorrelationIdForwardingHandler` (abstração `ICorrelationContext` + impl HttpContext, espelhando `IUserTokenAccessor`). Teste de integração no `accounts`. Branch `feature/obs-structured-logging`.
  - Estimativa: 5
- 🔵 **OBS-2** Tracing distribuído (OpenTelemetry, `traceparent` W3C) entre gateway → accounts/transactions/reporting.
  - *Dado* um lançamento de gasto, *quando* o transactions chama o accounts, *então* o trace mostra o span pai e filho correlacionados.
  - Estimativa: 8
- 🔵 **OBS-3** Endpoints de métricas/health padronizados por serviço.
  - Estimativa: 3

## 🔵 EPIC EVT — Reporting por eventos  · *To Do*
**Objetivo:** reporting deixa de agregar sob demanda e passa a consumir eventos.

- 🔵 **EVT-1** Publicação de eventos de domínio no `transactions` (outbox pattern).
  - *Dado* um gasto lançado, *quando* a transação é confirmada, *então* um evento `ExpenseRegistered` é gravado no outbox na mesma transação.
  - Estimativa: 8
- 🔵 **EVT-2** Projeção no `reporting` consumindo os eventos (com idempotência).
  - *Dado* o mesmo evento entregue mais de uma vez, *quando* o reporting processa, *então* a projeção não duplica valores.
  - Estimativa: 8
- 🔵 **EVT-3** `/reports/summary` passa a ler a projeção (sem chamadas HTTP sob demanda), mantendo compatibilidade.
  - Estimativa: 5

---

## Ideias de produto (fora do escopo atual — backlog de descoberta)
Não estão no briefing; registrar como épicos candidatos se o PO priorizar.
- Orçamentos/metas por categoria e escopo (PF/PJ).
- Importação de extrato/fatura (CSV/OFX).
- Fechamento de fatura de cartão e previsão de caixa.
- Exportação contábil (relatório PJ para o contador).
