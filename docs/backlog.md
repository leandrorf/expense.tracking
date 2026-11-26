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
- 🟢 **RPT-1** Agregador sem banco: lê transactions + accounts por HTTP (token encaminhado). · 5 *(substituído pela projeção por eventos no épico EVT)*
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

## 🟢 EPIC OBS — Observabilidade  · *Done*
**Objetivo:** enxergar o que acontece entre os serviços.

- 🟢 **OBS-1** Logs estruturados (JSON) com correlation id propagado pelo gateway.
  - *Dado* uma requisição pelo gateway, *quando* passa pelos serviços, *então* todos os logs carregam o mesmo correlation id.
  - Entregue: logger JSON nativo (`AddJsonConsole`, `IncludeScopes`) + `ObservabilitySetup` por serviço. Middleware resolve o `X-Correlation-Id` (inbound → trace id → GUID), ecoa na resposta e injeta num log scope ambiente. Propagação: gateway reescreve o header antes do YARP; `transactions`/`reporting` usam `CorrelationIdForwardingHandler` (abstração `ICorrelationContext` + impl HttpContext, espelhando `IUserTokenAccessor`). Teste de integração no `accounts`. Branch `feature/obs-structured-logging`.
  - Estimativa: 5
- 🟢 **OBS-2** Tracing distribuído (OpenTelemetry, `traceparent` W3C) entre gateway → accounts/transactions/reporting.
  - *Dado* um lançamento de gasto, *quando* o transactions chama o accounts, *então* o trace mostra o span pai e filho correlacionados.
  - Entregue: OpenTelemetry 1.21 (ASP.NET Core + HttpClient) em `ObservabilitySetup.AddDistributedTracing` de cada serviço; nome `expense-tracking-<serviço>`; `/health` fora dos traces. Exportação OTLP só com `OTEL_EXPORTER_OTLP_ENDPOINT` (testes/CI não exportam). Jaeger 2.23 no compose (UI `:16686`). Teste de integração no `transactions` com accounts falso em Kestrel real prova pai/filho nos spans e no `traceparent` recebido. Validado ao vivo: smoke test + trace `gateway → transactions → accounts` no Jaeger. Branch `feature/obs-distributed-tracing`.
  - Fora do escopo: spans de banco (instrumentação EF Core do OpenTelemetry ainda é beta) e métricas (OBS-3).
  - Estimativa: 8
- 🟢 **OBS-3** Endpoints de métricas/health padronizados por serviço.
  - *Dado* qualquer serviço, *quando* consulto `/health/live` e `/health/ready`, *então* recebo o mesmo contrato JSON, com a prontidão refletindo as dependências do próprio serviço; *e* as métricas HTTP e de runtime chegam ao Prometheus identificadas pelo serviço.
  - Entregue: health checks do ASP.NET Core (`/health/live` sem checks; `/health/ready` com o banco via `AddDbContextCheck` em accounts/identity/transactions; `/health` como alias). Métricas OpenTelemetry (`http.server.request.duration`, `http.client.request.duration`, meter `System.Runtime`) exportadas por OTLP. OpenTelemetry Collector 0.165 (contrib) como entrada única: traces → Jaeger, métricas → Prometheus 3.16 (config em `infra/observability/`). E2E passa a esperar `/health/ready`. Testes de integração do contrato nos 5 serviços + 503 com dependência falha e métrica no pipeline (accounts). Branch `feature/obs-metrics-health`.
  - Estimativa: 3

## 🟢 EPIC EVT — Reporting por eventos  · *Concluído*
**Objetivo:** reporting deixa de agregar sob demanda e passa a consumir eventos.
**Transporte:** outbox do `transactions` exposto como feed HTTP, lido por polling pelo `reporting` — ver [ADR-0001](adr/0001-transporte-de-eventos-transactions-reporting.md) (Aceito).

- 🟢 **EVT-1** Eventos de domínio no `transactions`: outbox + feed HTTP.
  - *Dado* um lançamento criado ou excluído, *quando* a mudança é confirmada, *então* um evento `TransactionRegistered` ou `TransactionDeleted` é gravado no outbox na mesma transação de banco.
  - *Dado* um consumidor com token de serviço (client credentials, escopo `events.read`), *quando* chama `GET /events?after={seq}`, *então* recebe os eventos em ordem de sequência, sem pular eventos de transações que ainda não commitaram (margem contra lacunas).
  - O `TransactionRegistered` carrega o escopo PF/PJ da conta, capturado na validação que o `transactions` já faz no `accounts` (o escopo é imutável).
  - O evento guarda `eventId`, `version`, `occurredAt`, `ownerUserId` e o `traceparent` de origem.
  - Carga inicial: a migração grava um `TransactionRegistered` para cada lançamento já existente.
  - Entregue: tabela `outbox_events` (migração `AddOutboxEvents`) gravada no mesmo `SaveChanges` do lançamento (porta `IEventOutbox`); `TransactionRegistered` (com `accountScope` = `Personal`/`Business`, lido na validação da conta no accounts) e `TransactionDeleted`; envelope com `traceParent`. Feed `GET /events?after&limit` com o escopo `events.read`, fora do gateway, servindo só o prefixo contíguo mais antigo que a margem (`Events:FeedSafetyMarginSeconds`, 5 s). Carga inicial na subida do serviço (idempotente; `accountScope = null` para lançamentos anteriores ao outbox). Identity: cliente de serviço ganha `events.read`, inclusive em bancos existentes. Testes: casos de uso (unit), feed/autorização/paginação/margem/carga inicial (integração), token com `events.read` (identity). Validado ao vivo no MySQL. Branch `feature/evt-outbox-feed`.
  - Estimativa: 8
- 🟢 **EVT-2** Projeção no `reporting` consumindo o feed (idempotente).
  - *Dado* o mesmo evento entregue mais de uma vez, *quando* o reporting processa, *então* a projeção não duplica valores.
  - *Dado* o checkpoint zerado, *quando* o worker roda, *então* a projeção é reconstruída a partir do log.
  - Worker em segundo plano com polling; projeção e checkpoint gravados no mesmo commit. Banco próprio `expense_reporting` (MySQL) no compose.
  - Entregue: `ProjectionWorker` (hosted service) lê `GET /events` em lotes (`Projection:BatchSize` 100, polling de 2 s, backoff de 10 s em erro) com token de serviço (`ClientCredentialsTokenProvider`, escopo `events.read`, token reaproveitado até perto de expirar). `ProjectionProcessor` aplica cadastro como upsert e exclusão como remoção se existir, e grava projeção (`projected_transactions`) e checkpoint (`projection_checkpoints`) no mesmo `SaveChanges`. Tipo de evento desconhecido é pulado; versão não suportada aborta o lote sem gravar. Span por lote ligado (links) aos traces de origem. `accountScope = null` dos eventos legados fica gravado como nulo (vira `Unknown` na leitura, EVT-3). Banco `expense_reporting` (migração `InitialCreate`) com check de readiness. Testes: processador (unit), projeção/rebuild/worker e token de serviço (integração). Validado ao vivo: projeção igual à fonte (15 lançamentos, 404,90), replay com checkpoint zerado sem duplicar e rebuild do zero. Branch `feature/evt-reporting-projection`.
  - Estimativa: 8
- 🟢 **EVT-3** `/reports/summary` passa a ler a projeção (sem chamadas HTTP sob demanda), mantendo compatibilidade.
  - *Dado* o `transactions` ou o `accounts` fora do ar, *quando* consulto o resumo, *então* ele responde com a projeção (possivelmente defasada).
  - Métrica de atraso da projeção (última sequência do feed menos o checkpoint) no pipeline da OBS-3. O E2E que confere o Resumo passa a esperar a projeção.
  - Entregue: `GetSummaryHandler` lê a projeção (`IProjectionQueries`) filtrando por dono (`sub`), conta e período; contrato da resposta inalterado. Escopo vem do evento; `accountScope` nulo (legado) agrupa como `Unknown`. Removidos os clients HTTP do resumo (`ITransactionsClient`, `IAccountsClient`, repasse de token/correlation id) e a dependência do `accounts` no compose. Feed do transactions ganha `headSequence` (campo novo, compatível); métricas `reporting.projection.lag`/`reporting.projection.checkpoint` (`ProjectionStatus`, observable gauges) no Collector → Prometheus. E2E: comando `cy.waitForSummary` recarrega o Resumo (botão Aplicar) até a projeção refletir; smoke test confere o resumo da conta nova com espera. Testes: atraso (unit), resumo por dono/filtros/legado `Unknown` e métricas do worker (integração), `headSequence` (transactions). Validado ao vivo: resumo idêntico com transactions e accounts parados, métricas no Prometheus, E2E 2/2 e smoke 10/10. Branch `feature/evt-reporting-summary-projection`.
  - Estimativa: 5
- 🟢 **EVT-4** Escopo PF/PJ dos lançamentos anteriores ao outbox (backfill).
  - *Dado* um lançamento legado cujo `TransactionRegistered` saiu sem escopo, *quando* o transactions sobe, *então* ele grava um `TransactionAccountScopeResolved` com o escopo da conta lido no accounts, e o Resumo deixa de mostrá-lo como "Desconhecido".
  - *Dado* a projeção reconstruída do zero, *quando* o reporting relê o feed, *então* o escopo dos legados é o mesmo (o log é a fonte, nada é corrigido só na projeção).
  - Entregue: accounts expõe `GET /internal/account-scopes` (escopo `accounts.scopes.read`, só `{accountId, scope}`, até 100 ids, sem filtro de dono via porta `IAccountScopeLookup`); identity concede o escopo ao cliente de serviço (seed reconcilia); gateway bloqueia `/api/internal/**` (404). Transactions: `AccountScopeBackfillWorker` (subida, retentativas até 5 min) + `AccountScopeBackfill` idempotente (ignora excluídos e já resolvidos), client credentials próprio. Reporting aplica `TransactionAccountScopeResolved` só em linha existente. Testes: endpoint interno (401/403/200 entre donos/limites), bloqueio no gateway (com contraprova), backfill e client em lotes de 100 com token de serviço (transactions), aplicação e não-recriação (reporting). Validado ao vivo: 7 legados resolvidos, projeção sem escopo nulo, restart sem duplicar, 404 no gateway, 403 com outro escopo, smoke 10/10 e E2E 2/2. Branch `feature/evt-legacy-scope-backfill`.
  - Estimativa: 3

---

## 🟢 EPIC BDG — Orçamentos por categoria e escopo (PF/PJ)  · *Done*
**Problema:** hoje o usuário só vê quanto **já gastou** (Resumo). Ele não tem como saber, durante o
mês, se um gasto PF ou PJ está saindo do planejado, e só percebe o estouro depois de fechado o mês.
**Objetivo (outcome):** para cada categoria que importa, o usuário define um limite mensal separado
para PF e para PJ e acompanha, a qualquer momento do mês, quanto consumiu, quanto resta e quais
orçamentos estouraram, sem misturar o dinheiro da empresa com o pessoal.
**Métricas de sucesso:** (1) o usuário cadastra orçamentos para as categorias que mais gasta, nos
dois escopos; (2) o status do mês bate com o Resumo do mesmo período para a mesma categoria e escopo
(diferença zero, salvo a defasagem da projeção); (3) a jornada PF e a PJ passam no E2E do CI.
**Rastreabilidade:** `docs/PROMPT.md` → Domínio ("controle de gastos PF e PJ"; "escopo PF/PJ é
dimensão de primeira classe em lançamentos e relatórios"; meios cartão, Pix, dinheiro,
transferência e boleto). Ideia promovida de "Ideias de produto" a pedido do dono do produto
(2026-11-20).

**Achados no código que moldam o épico:**
- **Categoria é texto livre.** `Transaction.Category` é `string` obrigatória, só com `Trim()` e no
  máximo 60 caracteres (`Transaction.CategoryMaxLength`). Não há enum, catálogo nem cadastro de
  categorias; a SPA usa um `<input>` livre (`tx-category`). A categoria viaja assim no
  `TransactionRegistered` e fica na projeção `projected_transactions.category`.
- **O agrupamento atual é sensível a maiúsculas e acentos.** O `SummaryCalculator` agrupa com o
  comparador padrão (ordinal): "Alimentação", "alimentação" e "Alimentacao" viram três linhas no
  Resumo. Já um filtro feito no MySQL (collation padrão `utf8mb4_0900_ai_ci`) trataria as três como
  iguais. O orçamento precisa de uma regra de correspondência explícita, no domínio, sem depender da
  collation.
- **Escopo PF/PJ** já está em cada linha da projeção (`AccountScope`); `Unknown` só sobra para conta
  inexistente no `accounts` (EVT-4).
- **Não há edição de lançamento** (só criar e excluir), então o consumido só muda por cadastro ou
  exclusão, os dois já projetados.
- **Moeda:** `Money` aceita qualquer ISO 4217, mas o Resumo assume uma moeda só (a primeira que
  encontra).
- **O Resumo não cruza categoria × escopo:** devolve `byCategory` e `byScope` separados. O consumo
  de um orçamento precisa de uma consulta nova na projeção.

**Decisões (aceitas pelo dono em 2026-11-20, registradas na ADR-0002; a recomendação de cada item foi a escolhida):**
1. **Onde mora o orçamento.** Opções: (a) no `reporting`, em tabelas próprias no
   `expense_reporting`, ao lado da projeção; (b) serviço novo `budgets` com banco próprio;
   (c) no `accounts`. *Recomendação: (a)*. O consumido sai da projeção sem chamada entre serviços e
   sem um segundo consumidor do feed. A opção (b) é a mais limpa como contexto delimitado, mas exige
   chamar o `reporting` na consulta ou virar o **segundo consumidor** do feed, gatilho que a
   ADR-0001 lista para reabrir o transporte. A opção (c) não tem os lançamentos. A escolha muda o
   papel do `reporting` (deixa de ser só modelo de leitura e passa a ter dado escrito pelo usuário),
   então é decisão arquitetural: **registrar na ADR-0002 (BDG-1) antes da primeira história de
   código.**
2. **Período.** *Recomendação:* mês civil fixo, sem períodos personalizados. O orçamento é um limite
   mensal **recorrente** que vale a partir de um mês de vigência; alterar o valor cria nova vigência
   a partir do mês escolhido, preservando o limite dos meses anteriores. Alternativa mais simples:
   o valor atual vale para todos os meses (o histórico muda junto).
3. **Cálculo do consumido.** *Recomendação:* consulta à projeção do `reporting`: soma dos
   lançamentos do tipo **gasto** (receitas não contam), do mesmo dono, escopo e moeda, com
   `occurredOn` dentro do mês (**regime de competência**: compra no cartão conta na data da compra,
   não na fatura), em **todas as contas** do escopo e em todos os meios de pagamento. Defasagem
   aceita: a mesma do Resumo (margem do feed + polling).
4. **Correspondência de categoria.** *Recomendação:* comparar por chave normalizada no domínio
   (trim, espaços internos colapsados, sem diferença de maiúsculas nem de acentos), aplicada ao
   orçamento e aos lançamentos. Um catálogo gerenciado de categorias (com renomear/mesclar) fica fora
   do MVP; a BDG-7 reduz a digitação divergente sugerindo as categorias já usadas.
5. **Alertas de estouro.** *Recomendação:* no MVP, **status calculado na leitura** (Dentro, Atenção
   a partir de 80%, Estourado acima de 100%) e destaque visual na SPA. Notificação ativa (e-mail,
   push) fica fora: não há infraestrutura de notificação no projeto e o briefing não pede.
6. **Moeda.** *Recomendação:* o orçamento tem moeda (padrão BRL) e só soma lançamentos da mesma
   moeda.
7. **"Metas".** *Recomendação:* neste épico, meta = teto de gasto. Meta de economia ou de receita
   fica como pergunta aberta ao dono, fora do MVP.

- ✅ **BDG-1** ADR-0002: onde mora o orçamento e regras do modelo.
  ([ADR-0002](adr/0002-orcamentos-no-reporting.md), Aceita em 2026-11-20)
  - *Dado* as decisões em aberto 1 a 7 deste épico, *quando* o dono do produto revisa a análise de
    opções, *então* a ADR-0002 é registrada como **Aceita** em `docs/adr/` (com o índice atualizado),
    e o PO ajusta as histórias seguintes ao que foi decidido.
  - Conteúdo mínimo: serviço dono do orçamento e impacto na ADR-0001; período e vigência; regra de
    correspondência de categoria; cálculo do consumido; rota no gateway e escopos OAuth
    (reaproveitar `expenses.read`/`expenses.write`).
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **BDG-2** Definir e listar orçamentos mensais por categoria e escopo (API).
  (`GET`/`POST /budgets` no `reporting`, rota `/api/budgets/**` no gateway, smoke test estendido)
  - *Dado* um usuário com o escopo `expenses.write`, *quando* define um orçamento "Alimentação",
    escopo **PF**, R$ 1.500,00 a partir de 2026-12, *então* o orçamento é criado e aparece na
    listagem dele, e de nenhum outro usuário.
  - *Dado* um orçamento "Alimentação" PF vigente, *quando* o usuário define "Alimentação" **PJ**,
    *então* os dois coexistem como orçamentos independentes.
  - *Dado* um orçamento "Alimentação" PF vigente, *quando* o usuário tenta criar "alimentacao" PF,
    *então* recebe um erro de conflito, porque a categoria normalizada é a mesma.
  - *Dado* valor zero ou negativo, mais de 2 casas decimais, categoria vazia ou acima de 60
    caracteres, escopo diferente de `Personal`/`Business`, ou mês inválido, *quando* tenta criar,
    *então* recebe erro de validação no mesmo formato dos demais serviços.
  - *Dado* um token sem `expenses.write`, *quando* tenta criar, *então* recebe 403; sem token, 401.
  - Rota pelo gateway (ex.: `/api/budgets/**`) para o serviço decidido na BDG-1.
  - Estimativa: 5 · **Must** · Dependências: BDG-1
- ✅ **BDG-3** Alterar o valor e encerrar um orçamento (API).
  (`PUT /budgets/{id}/amount`, `POST /budgets/{id}/close`, `GET /budgets?month=`; vigências em `budget_limits`)
  - *Dado* "Alimentação" PF de R$ 1.500,00 desde 2026-12, *quando* o usuário altera para
    R$ 1.800,00 a partir de 2027-02, *então* dezembro e janeiro continuam com R$ 1.500,00 e fevereiro
    em diante usa R$ 1.800,00.
  - *Dado* um orçamento vigente, *quando* o usuário o encerra a partir de um mês, *então* ele deixa de
    aparecer nos meses seguintes e continua nos anteriores.
  - *Dado* um orçamento de outro usuário, *quando* tenta alterar ou encerrar, *então* recebe 404.
  - Estimativa: 3 · **Must** · Dependências: BDG-2
- ✅ **BDG-4** Acompanhamento do mês: limite, consumido, restante e status (API).
  (`GET /budgets/status?month=`, cálculo puro em `BudgetStatusCalculator`)
  - *Dado* "Alimentação" PF de R$ 1.000,00 em 2026-12 e gastos PF em "Alimentação" de R$ 300,00 no
    **Pix**, R$ 200,00 em **dinheiro** e R$ 250,00 no **cartão de crédito**, em contas PF diferentes,
    *quando* consulto o status de 2026-12, *então* o consumido é R$ 750,00, o restante R$ 250,00, 75%
    e status **Dentro**.
  - *Dado* "Fornecedores" PJ de R$ 5.000,00 e gastos PJ de R$ 3.000,00 em **boleto** e R$ 1.200,00 em
    **transferência**, *quando* consulto o mês, *então* 84% e status **Atenção**.
  - *Dado* um gasto que faz o consumido passar do limite, *quando* consulto o mês, *então* o status é
    **Estourado** e o restante é negativo (o quanto passou).
  - *Dado* um gasto PJ em "Alimentação", *quando* consulto o orçamento "Alimentação" PF, *então* ele
    não entra no consumido (escopos nunca se misturam).
  - *Dado* receitas, lançamentos de outro mês, de outra moeda ou com escopo `Unknown`, *quando*
    consulto o mês, *então* nenhum deles entra no consumido.
  - *Dado* um gasto lançado como "alimentação " (minúscula, espaço sobrando), *quando* consulto
    "Alimentação" PF, *então* ele entra no consumido.
  - *Dado* uma compra no cartão em 2026-12-28 com fatura em janeiro, *quando* consulto dezembro,
    *então* ela conta em dezembro (data da compra).
  - *Dado* um gasto excluído, *quando* a projeção processa a exclusão, *então* o consumido diminui.
  - *Dado* o `transactions` fora do ar, *quando* consulto o status, *então* ele responde a partir da
    projeção (possivelmente defasada).
  - Cálculo do status como função pura no domínio (como o `SummaryCalculator`), com teste unitário.
  - Estimativa: 5 · **Must** · Dependências: BDG-2 (BDG-3 para a vigência)
- ✅ **BDG-5** Aba Orçamentos na SPA: cadastro, alteração e encerramento.
  (`BudgetsView` + `api/budgets.ts`; acompanhamento visual do mês fica na BDG-6)
  - *Dado* o usuário logado, *quando* abre a aba **Orçamentos**, *então* vê os orçamentos separados em
    PF e PJ e um formulário com categoria, escopo, valor e mês de vigência.
  - *Dado* o formulário preenchido, *quando* salva, *então* o orçamento aparece na lista sem recarregar
    a página; *dado* um conflito ou erro de validação, *então* a mensagem da API aparece junto ao
    formulário.
  - *Dado* um orçamento na lista, *quando* altera o valor ou encerra, *então* a lista reflete a
    mudança.
  - Atributos `data-cy` em todos os elementos usados pelo E2E; acessível por teclado.
  - Estimativa: 5 · **Must** · Dependências: BDG-2, BDG-3
- ✅ **BDG-6** Acompanhamento visual do mês na aba Orçamentos.
  (`MonthTracking` em `BudgetsView`, consumindo `GET /budgets/status?month=`)
  - *Dado* orçamentos PF e PJ no mês corrente, *quando* abro a aba, *então* cada um mostra limite,
    consumido, restante, percentual e uma barra de progresso, agrupados por escopo.
  - *Dado* um orçamento **Atenção** ou **Estourado**, *quando* a lista renderiza, *então* o status
    aparece em texto e em destaque visual (não só por cor).
  - *Dado* a aba aberta, *quando* navego para o mês anterior ou o seguinte, *então* os valores e os
    limites são os daquele mês.
  - Estimativa: 3 · **Must** · Dependências: BDG-4, BDG-5
- ✅ **BDG-7** Sugerir as categorias já usadas (orçamento e lançamento).
  (`GET /budgets/categories` com `CategorySuggestions`; `<datalist>` via `useCategorySuggestions`; inclui as categorias dos orçamentos)
  - *Dado* lançamentos PF e PJ com categorias "Alimentação" e "Combustível", *quando* digito a
    categoria no formulário de orçamento ou de lançamento, *então* as categorias já usadas pelo
    usuário aparecem como sugestão, sem repetir variações de maiúscula/acento.
  - *Dado* outro usuário, *quando* consulta as sugestões, *então* não vê as categorias deste.
  - Continua permitindo categoria nova (texto livre); a lista sai da projeção.
  - Estimativa: 3 · **Should** · Dependências: BDG-1 (independe das demais)
- ✅ **BDG-8** Gastos sem orçamento no mês.
  (`unbudgeted` em `GET /budgets/status`, `BudgetStatusCalculator.CalculateUnbudgeted`; bloco na aba)
  - *Dado* gastos PJ em "Viagens" sem orçamento PJ para essa categoria, *quando* consulto o
    acompanhamento do mês, *então* vejo o total "Sem orçamento" por escopo, com as categorias que o
    compõem.
  - Estimativa: 2 · **Should** · Dependências: BDG-4 (UI junto da BDG-6)
- ✅ **BDG-9** E2E Cypress: jornadas de orçamento PF e PJ.
  (`cypress/e2e/budgets.cy.ts` + `cy.waitForBudget`; botão "atualizar" no acompanhamento do mês)
  - *Dado* um orçamento "Alimentação" PF, *quando* lanço um gasto PF no **Pix** que ultrapassa o
    limite, *então* a aba Orçamentos mostra o consumido e o status **Estourado**.
  - *Dado* um orçamento PJ, *quando* lanço um gasto PJ no **cartão de crédito**, *então* o consumido do
    orçamento PJ sobe e o do PF da mesma categoria não muda.
  - Espera a projeção como o `cy.waitForSummary` (sem `cy.wait` fixo); roda no job `Cypress E2E`.
  - Estimativa: 5 · **Must** · Dependências: BDG-6
- ✅ **BDG-10** Limiar de "Atenção" configurável por orçamento.
  (`warningThreshold` 1–100, padrão 80, no cadastro e em `PUT /budgets/{id}/warning-threshold`; sem vigência)
  - *Dado* um orçamento com limiar de 90%, *quando* o consumido chega a 85%, *então* o status é
    **Dentro**; a 90%, **Atenção**.
  - Estimativa: 2 · **Could** · Dependências: BDG-4

**Fora do escopo (Won't, nesta versão):** notificação ativa de estouro (e-mail/push); orçamento
por conta ou por cartão; períodos que não sejam o mês civil (semanal, anual, personalizado);
catálogo gerenciado de categorias (renomear/mesclar); metas de economia ou de receita; conversão
entre moedas.

**Priorização (MoSCoW):** Must = BDG-1, 2, 3, 4, 5, 6, 9 (28 pts), o mínimo para definir, acompanhar
e provar ponta a ponta nos dois escopos. Should = BDG-7, 8 (5 pts): a BDG-7 ataca o maior risco
do épico (categoria livre divergente que some do consumido) e é barata. Could = BDG-10 (2 pts).
Total: 35 pts.

**Definition of Ready (por história):** decisões da BDG-1 aceitas; critérios de aceite revisados com
QA; contrato da API (rota, payload, erros) descrito na história de backend antes da de frontend.

**Definition of Done do épico:**
- CI verde no PR: `build & test` dos serviços afetados e `Cypress E2E` (os 6 checks exigidos no
  `main`; serviço novo, se a BDG-1 decidir por ele, entra na matriz e nos required checks).
- Testes unitários do domínio (orçamento, vigência, normalização de categoria, cálculo de status).
- Testes de integração (WebApplicationFactory): isolamento por dono (404 em recurso alheio), 401/403
  por escopo, validações, conflito, consumido a partir da projeção (PF/PJ, todos os meios, exclusão).
- E2E das jornadas PF e PJ (BDG-9) no job do CI; smoke test (`build/smoke-test.mjs`) cobrindo
  criar orçamento e ler o status pelo gateway.
- Migração EF no banco do serviço dono; logs, traces e métricas HTTP chegando pelo pipeline da OBS.
- `docs/architecture.md`, a ADR-0002 e o histórico do `docs/PROMPT.md` atualizados; validado ao
  vivo no `docker compose`.

---

## 🟢 EPIC IDX — Identity em repositório próprio  · *Done*
**Problema:** o projeto é de microserviços em tempo de execução (cada serviço com processo, banco e
deploy próprios), mas o `identity` ainda nasce, compila, testa e é versionado junto com os
consumidores dele. Uma mudança no servidor de autenticação passa pelo CI de todo o monorepo, e uma
mudança nos consumidores pode exigir mexer no código do `identity` (clientes e escopos fixos nele).
**Objetivo (outcome):** o `identity` vive em um repositório próprio, com CI, versionamento e imagem
Docker próprios. O monorepo passa a consumi-lo como qualquer serviço externo (imagem com versão
fixada) e é dono da própria configuração de clientes OAuth, escopos e usuário demo, sem precisar de
release do `identity` para isso.
**Métricas de sucesso:** (1) o monorepo não tem mais código do `identity` e o stack sobe só com a
imagem publicada; (2) um cliente ou escopo novo no monorepo não exige mudança no repositório do
`identity`; (3) o E2E do monorepo continua verde contra a versão fixada da imagem; (4) o repositório
do `identity` tem CI próprio verde e o `main` dele protegido.
**Rastreabilidade:** `docs/PROMPT.md` → Arquitetura ("microserviços backend", "OAuth2 próprio").
Iniciativa pedida pelo dono do produto (2026-11-21).

**Achados no código que moldam o épico:**
- **Sem referência de código entre serviços.** O `identity` não usa projeto de outro serviço (nenhum
  `ProjectReference` fora de `services/identity`). A separação é de build, CI e configuração.
- **Build compartilhado.** O `identity` herda `Directory.Build.props` (net10.0, nullable,
  `InvariantGlobalization`), `Directory.Packages.props` (versões centrais) e `NuGet.config` da raiz.
  O `Dockerfile` usa a raiz do monorepo como contexto. No repositório novo, esses arquivos viram
  cópias próprias (as versões de pacote podem divergir entre os repositórios com o tempo).
- **Configuração dos consumidores escrita no código.** O `IdentitySeeder` fixa o cliente da SPA
  (redirect `http://localhost:5173/callback`, escopos `expenses.*`), o cliente
  `expense-tracking-service` com `events.read` e `accounts.scopes.read` (EVT-1, EVT-4) e o usuário
  `demo@expense.local`; o CORS e a tela de login também citam a SPA e o usuário demo. Hoje, cada
  escopo de serviço novo (como os do EVT) exigiu mudar o `identity`.
- **Todos dependem dele em tempo de execução.** No `docker-compose.yml`, os outros 4 serviços e a
  SPA apontam para `identity:8080` (JWKS interno, token endpoint de serviço) e para o issuer
  público `http://localhost:5080`. O contrato que precisa ficar estável é o OIDC: discovery, JWKS,
  `/oauth/authorize`, `/oauth/token` e as claims `sub`/`scope`.
- **CI e gate.** A matriz do `ci.yml` tem `identity`; o ruleset `main gate (CI-3)` exige o check
  `build & test (identity)`; o `e2e.yml` sobe o stack com `--build` (inclusive o `identity`) e o
  login do Cypress passa pela tela dele.
- **Histórico pequeno.** Só 8 commits tocam `services/identity`, o que torna barato levar o
  histórico junto.

**Decisões (aceitas pelo dono em 2026-11-21, registradas na ADR-0003; a recomendação de cada item foi a escolhida):**
1. **Como o monorepo consome o `identity`.** Opções: (a) imagem Docker publicada no GitHub
   Container Registry (`ghcr.io/leandrorf/expense-identity`) com versão fixada no compose;
   (b) submódulo git; (c) o CI clona o outro repositório e faz o build. *Recomendação: (a)*. É o
   jeito de consumir um serviço independente: o monorepo nem compila o `identity`. (b) e (c)
   mantêm o acoplamento de build que o épico quer remover.
2. **Versão.** *Recomendação:* SemVer com tag git (`v1.0.0`) gerando a imagem com a mesma tag; o
   monorepo fixa a versão exata (nunca `latest`) e sobe de versão por PR, que roda o E2E. Mudança
   incompatível no contrato OIDC (claims, rotas, escopos) = versão major.
3. **Nome e visibilidade.** *Recomendação:* repositório `leandrorf/expense-identity`, **público**
   como o atual, e imagem pública no GHCR, para o compose e o CI do monorepo baixarem sem
   credencial.
4. **Histórico.** *Recomendação:* levar o histórico de `services/identity` com `git filter-repo`
   (8 commits) e remover a pasta do monorepo só depois que a imagem publicada estiver em uso.
5. **Configuração dos clientes.** *Recomendação:* clientes OAuth, escopos, origens de CORS e usuário
   demo saem do código e passam a vir de configuração (`Seed:Clients`, `Seed:Users`, `Cors:AllowedOrigins`),
   com o seed continuando idempotente e reconciliando escopos. O `docker-compose.yml` do monorepo
   passa a declarar os clientes dele. Sem configuração, o `identity` sobe sem cliente nenhum.
   **Feito antes da extração, ainda no monorepo** (IDX-2), para separar a mudança de comportamento
   da mudança de lugar.
6. **Desenvolvimento local.** *Recomendação:* o compose usa a imagem por padrão; um
   `docker-compose.identity-local.yml` opcional faz o build a partir de um clone local do
   repositório do `identity` (caminho por variável), para testar as duas mudanças juntas.
7. **Proteção do contrato.** *Recomendação:* o repositório do `identity` roda, no CI, um teste de
   contrato OIDC contra o container (discovery, JWKS, Authorization Code + PKCE, refresh,
   client credentials, claims), para não quebrar consumidores sem perceber. O E2E do monorepo
   continua sendo a prova ponta a ponta.

- ✅ **IDX-1** ADR: identity em repositório próprio.
  ([ADR-0003](adr/0003-identity-em-repositorio-proprio.md), Aceita em 2026-11-21)
  - *Dado* as decisões em aberto 1 a 7 deste épico, *quando* o dono do produto revisa as opções,
    *então* a ADR é registrada como **Aceita** em `docs/adr/` (índice atualizado) e o PO ajusta as
    histórias seguintes ao que foi decidido.
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **IDX-2** Clientes, escopos, CORS e usuário demo por configuração (ainda no monorepo).
  (`SeedOptions` + `LoginPageOptions`; config do monorepo em `infra/identity/appsettings.Production.json`)
  - *Dado* o `identity` sem nenhum cliente no código, *quando* sobe com os clientes declarados na
    configuração (o compose do monorepo), *então* o login da SPA, o client credentials do serviço e
    o E2E funcionam como hoje.
  - *Dado* um escopo novo adicionado só na configuração de um cliente existente, *quando* o
    `identity` reinicia, *então* o cliente passa a ter o escopo sem recriar o banco (reconciliação).
  - *Dado* nenhuma configuração de cliente, *quando* o `identity` sobe, *então* ele funciona sem
    clientes e sem usuário demo (nada de padrão escondido no código).
  - Origem do CORS e texto de demo da tela de login vêm da mesma configuração.
  - Estimativa: 5 · **Must** · Dependências: IDX-1
- ✅ **IDX-3** Repositório `expense-identity` com histórico, build e CI próprios.
  ([leandrorf/expense-identity](https://github.com/leandrorf/expense-identity): histórico via `git subtree split`, CI `build & test` + `docker image`, ruleset `main gate` id 27785008)
  - *Dado* o repositório novo criado com o histórico de `services/identity`, *quando* um PR é aberto
    nele, *então* o CI roda build, testes unitários e de integração e fica verde.
  - Build autônomo: cópias de `Directory.Build.props`, `Directory.Packages.props` (só os pacotes
    usados) e `NuGet.config`; `Dockerfile` com a raiz do próprio repositório como contexto.
  - `main` protegido por ruleset (check de build e teste obrigatório, sem force-push), no mesmo
    padrão do CI-3.
  - Estimativa: 3 · **Must** · Dependências: IDX-2
- ✅ **IDX-4** Publicar a imagem versionada no GHCR.
  (workflow `Release` no `expense-identity`; `ghcr.io/leandrorf/expense-identity:1.0.0` pública, tag `v1.0.0`)
  - *Dado* uma tag `vX.Y.Z` no repositório do `identity`, *quando* o workflow de release roda,
    *então* a imagem `ghcr.io/leandrorf/expense-identity:X.Y.Z` é publicada, pública, com os
    rótulos OCI de origem e versão.
  - *Dado* um push sem tag, *quando* o CI roda, *então* a imagem é construída (para pegar erro de
    Dockerfile) mas não é publicada.
  - Estimativa: 3 · **Must** · Dependências: IDX-3
- ✅ **IDX-5** Monorepo consome a imagem e remove o código do `identity`.
  (compose com `ghcr.io/leandrorf/expense-identity:1.0.0`; `docker-compose.identity-local.yml`; matriz e ruleset sem `identity`)
  - *Dado* o `docker-compose.yml` com `image: ghcr.io/leandrorf/expense-identity:<versão fixada>`,
    *quando* subo o stack, *então* tudo funciona sem `services/identity` no repositório (smoke 17/17).
  - *Dado* um PR no monorepo, *quando* o CI roda, *então* a matriz não tem mais `identity`, o
    `e2e.yml` baixa a imagem fixada e o Cypress passa.
  - Ruleset do `main` sem o check `build & test (identity)` (mudança feita no mesmo PR, para o gate
    não travar esperando um check que não existe mais).
  - `docker-compose.identity-local.yml` opcional para build a partir de um clone local; README com
    os dois jeitos.
  - Estimativa: 5 · **Must** · Dependências: IDX-4
- ✅ **IDX-6** Teste de contrato OIDC no repositório do `identity`.
  ([expense-identity#2](https://github.com/leandrorf/expense-identity/pull/2): `ContractTests` de caixa-preta, job `oidc contract` obrigatório no `main` de lá)
  - *Dado* o container do `identity` com um cliente de teste configurado, *quando* o CI roda o teste
    de contrato, *então* ele valida discovery, JWKS, Authorization Code + PKCE, refresh com rotação,
    client credentials e as claims `sub`/`scope` que os serviços usam.
  - *Dado* uma mudança que remove ou renomeia uma claim, *quando* o PR roda, *então* o teste falha.
  - Estimativa: 3 · **Should** · Dependências: IDX-3
- ✅ **IDX-7** Atualização de versão da imagem no monorepo.
  (`.github/dependabot.yml`, ecossistema `docker-compose`, semanal, restrito a `ghcr.io/leandrorf/expense-identity`)
  - *Dado* uma versão nova da imagem publicada, *quando* o Dependabot (ecossistema `docker-compose`)
    roda, *então* abre um PR no monorepo trocando a versão fixada, e o E2E do PR prova a
    compatibilidade.
  - Estimativa: 2 · **Should** · Dependências: IDX-5

**Fora do escopo (Won't, nesta versão):** extrair os outros serviços (`accounts`, `transactions`,
`reporting`, `gateway`) para repositórios próprios (avaliar depois desta experiência); biblioteca
NuGet compartilhada entre repositórios; deploy em nuvem ou orquestrador (Kubernetes); mudar o
protocolo ou as claims do OAuth2.

**Priorização (MoSCoW):** Must = IDX-1, 2, 3, 4, 5 (18 pts), o mínimo para o monorepo rodar e testar
sem o código do `identity`. Should = IDX-6, 7 (5 pts): protegem o contrato e mantêm a versão em dia
sem trabalho manual. Total: 23 pts. **Independente do épico IMP** (que mexe no `transactions`); a
recomendação é fazer o IDX primeiro, porque ele muda o CI e o gate que todos os PRs seguintes usam.

**Definition of Ready (por história):** decisões da IDX-1 aceitas; repositório e GHCR com permissão
de escrita para a conta `leandrorf` (`gh` ativo, ver memória do projeto).

**Definition of Done do épico:**
- CI verde nos dois repositórios; `main` protegido nos dois.
- O monorepo sobe e passa no smoke e no E2E só com a imagem publicada, sem `services/identity`.
- Configuração dos clientes OAuth do monorepo declarada no próprio monorepo.
- Testes de integração do `identity` cobrindo o seed por configuração (reconciliação de escopos,
  sem configuração = sem clientes).
- `docs/architecture.md`, a ADR, o `README.md` dos dois repositórios, o histórico do
  `docs/PROMPT.md` e a memória do projeto atualizados.

---

## 🟢 EPIC IMP — Importação de extrato e fatura (CSV/OFX)  · *Done*
**Problema:** todo lançamento hoje é digitado à mão, um por um. Quem tem dezenas de gastos por mês
em cartão e conta (PF e PJ) desiste de registrar, e aí o Resumo e os orçamentos ficam incompletos.
**Objetivo (outcome):** o usuário importa o extrato da conta ou a fatura do cartão que o banco já
entrega (OFX ou CSV), confere uma prévia com categoria e meio de pagamento sugeridos e confirma. Os
lançamentos entram com o escopo PF/PJ da conta escolhida, sem duplicar o que já existe, e passam a
contar no Resumo e nos orçamentos como qualquer outro.
**Métricas de sucesso:** (1) importar um extrato de um mês leva poucos minutos, contra digitar
linha a linha; (2) reimportar o mesmo arquivo não cria nenhum lançamento repetido; (3) depois da
confirmação, o Resumo e o acompanhamento dos orçamentos refletem os lançamentos importados (salvo a
defasagem da projeção); (4) as jornadas PF (extrato OFX) e PJ (fatura do cartão) passam no E2E do CI.
**Rastreabilidade:** `docs/PROMPT.md` → Domínio ("controle de gastos PF e PJ"; meios cartão, Pix,
dinheiro, transferência e boleto). Ideia promovida de "Ideias de produto" a pedido do dono do
produto (2026-11-21).

**Achados no código que moldam o épico:**
- **Não há edição de lançamento** (só criar e excluir). Uma categoria errada na importação só se
  corrige excluindo e lançando de novo, e a categoria pesa no consumido dos orçamentos. Por isso a
  categoria precisa ser conferida **antes** de confirmar, na prévia.
- **Categoria e descrição são obrigatórias** (`Transaction.Create`: descrição até 200, categoria
  até 60 caracteres). Extrato e fatura não trazem categoria; o memo do banco vira a descrição.
- **Valor sempre positivo** (`Money` recusa ≤ 0); o sentido vem do `TransactionType`
  (`Expense`/`Income`). Débitos do extrato viram gasto e créditos viram receita.
- **Cartão de crédito exige `creditCardId`** e os outros meios não aceitam cartão. Os meios são
  `Cash`, `Pix`, `Boleto`, `CreditCard`, `DebitCard`, `BankTransfer`. O OFX não diz se um débito foi
  Pix, boleto ou TED; quando muito, o memo dá pistas ("PIX ENVIADO", "PAG BOLETO").
- **Não existe identificador externo nem deduplicação.** O lançamento não guarda a origem, então
  hoje nada impede o mesmo gasto de entrar duas vezes.
- **Cada criação valida conta e cartão no `accounts` por HTTP** (`IAccountsClient`). Importar
  centenas de linhas reaproveitando o caso de uso atual faria centenas de chamadas; a importação
  valida a conta e o cartão **uma vez por lote**.
- **Cada lançamento gera um `TransactionRegistered` no outbox**, então os importados chegam à
  projeção do `reporting` sem mudança no feed (ADR-0001). Uma importação grande só aumenta o atraso
  momentâneo da projeção (métrica `reporting.projection.lag`).
- **Não há upload de arquivo em nenhum serviço** (nenhum endpoint `multipart`). Limites de tamanho,
  validação de formato e cuidado com dados sensíveis (extrato bancário) entram pela primeira vez.
- **Sem bibliotecas pagas** (decisão do projeto). As bibliotecas OFX para .NET estão abandonadas;
  para CSV, o `CsvHelper` tem licença livre (MS-PL/Apache 2.0).

**Decisões (aceitas na [ADR-0004](adr/0004-importacao-csv-ofx-no-transactions.md), IMP-1; ficou a recomendação de cada uma):**
1. **Onde mora a importação.** Opções: (a) módulo no `transactions`; (b) serviço novo `imports`.
   *Recomendação: (a)*. Quem cria lançamentos e escreve no outbox é o `transactions`; um serviço
   novo teria de chamar a API dele linha a linha ou dividir o banco. A leitura do arquivo fica em
   classes puras, fáceis de extrair se um dia virar serviço.
2. **Prévia guardada no servidor ou no navegador.** Opções: (a) lote de importação persistido
   (`import_batches`/`import_lines`, status *Pendente → Confirmado/Descartado*); (b) sem estado: o
   servidor só lê o arquivo e a SPA devolve as linhas na confirmação. *Recomendação: (a)*. Dá
   confirmação idempotente (clique duplo ou reenvio não duplica), deduplicação feita no servidor
   sobre o que ele mesmo leu, e o id do lote serve para desfazer a importação (IMP-9). Lotes
   pendentes expiram em 7 dias.
3. **Deduplicação.** *Recomendação:* cada linha ganha uma **chave externa** por conta. No OFX é o
   `FITID` do banco. No CSV é uma impressão digital de data, valor e descrição normalizada, mais a
   ordem da linha entre as idênticas do mesmo dia (dois cafés iguais no mesmo dia são dois gastos).
   Coluna `external_id` no lançamento, índice único (dono, conta, `external_id`). Na prévia, a linha
   já importada aparece como **duplicada** e fica de fora por padrão. Um lançamento excluído pode ser
   importado de novo.
4. **Categoria.** *Recomendação:* toda linha incluída precisa de categoria antes de confirmar
   (não existe "A classificar" automático, porque não dá para editar depois). A prévia já vem
   preenchida pelo histórico (mesma descrição normalizada → última categoria usada, IMP-6), com
   sugestões da BDG-7 e "aplicar às selecionadas".
5. **Meio de pagamento.** *Recomendação:* fatura de cartão (OFX `CREDITCARDMSGSRSV1` ou CSV marcado
   como fatura) → `CreditCard` com o cartão escolhido no lote. Extrato de conta → o usuário escolhe
   um meio padrão para o lote (ex.: Pix) e pode trocar por linha. Pistas no memo ("PIX", "BOLETO",
   "TED", "SAQUE") viram sugestão, sem decidir sozinhas.
6. **Créditos e pagamento de fatura.** *Recomendação:* crédito no extrato de conta vira **receita**
   (incluída por padrão). Crédito na fatura do cartão (pagamento da fatura, estorno) fica **fora por
   padrão**: o pagamento da fatura não é gasto nem receita, e contá-lo duplicaria os gastos do
   cartão. O usuário pode incluir um estorno como receita na prévia.
7. **Leitura dos arquivos.** *Recomendação:* leitor de OFX próprio (OFX 1.x em SGML e 2.x em XML,
   com DTD e entidades externas desligadas), `CsvHelper` para CSV. CSV com mapeamento de colunas
   (data, descrição, valor ou débito/crédito separados), separador `;` ou `,`, decimal com vírgula,
   datas `dd/MM/yyyy` e `yyyy-MM-dd`, codificação UTF-8 ou Windows-1252 (bancos brasileiros).
8. **Limites e dados sensíveis.** *Recomendação:* arquivo até 5 MB e 5.000 linhas, só `.ofx`,
   `.qfx` e `.csv`. O arquivo original **não é guardado** (só as linhas lidas, no lote) e nunca vai
   para log, trace ou mensagem de erro; os dados do lote seguem a mesma regra de dono (404 para lote
   de outro usuário). Escopos OAuth: reaproveitar `expenses.write` (importar é lançar).
9. **Rota.** *Recomendação:* `/imports` no `transactions`, exposto como `/api/imports/**` no
   gateway, antes do catch-all `/api/**`.

- ✅ **IMP-1** ADR: onde mora a importação e regras do modelo. → [ADR-0004](adr/0004-importacao-csv-ofx-no-transactions.md), Aceita.
  - *Dado* as decisões em aberto 1 a 9 deste épico, *quando* o dono do produto revisa as opções,
    *então* a ADR é registrada como **Aceita** em `docs/adr/` (com o índice atualizado) e o PO
    ajusta as histórias seguintes ao que foi decidido.
  - Numeração: a próxima livre (0004, já que a IDX-1 ficou com a 0003).
  - Refinamentos registrados na ADR além das recomendações: conta e cartão validados no envio e de
    novo na confirmação; formato conferido pelo conteúdo, não só pela extensão; limite de corpo no
    Kestrel e no gateway; id do lote guardado em cada lançamento criado (para a IMP-9);
    `CreditCard` não aceito como meio em extrato de conta.
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **IMP-2** Ler extrato e fatura em OFX (domínio, sem I/O). → `Domain/Imports/Ofx/OfxReader`; exemplos sintéticos em `tests/.../Imports/Samples/`.
  - *Dado* um OFX 1.x (SGML, Windows-1252) de extrato de conta corrente de banco brasileiro,
    *quando* é lido, *então* cada `STMTTRN` vira uma linha com data, valor com sinal, memo e `FITID`.
  - *Dado* um OFX 2.x (XML) de fatura de cartão (`CREDITCARDMSGSRSV1`), *quando* é lido, *então* as
    compras vêm como débito e o pagamento da fatura como crédito, e o arquivo é reconhecido como
    fatura de cartão.
  - *Dado* um OFX com `<!DOCTYPE>` ou entidade externa, *quando* é lido, *então* nada é resolvido
    (sem XXE) e o arquivo é recusado com erro de formato.
  - *Dado* um arquivo truncado ou que não é OFX, *quando* é lido, *então* o erro diz o motivo sem
    ecoar o conteúdo do arquivo.
  - Testes unitários com arquivos de exemplo **sintéticos** (nunca extrato real) no repositório.
  - Feito também: `FITID` repetido no mesmo arquivo (há bancos que repetem) vira `FITID#2`, `#3`…;
    valor com vírgula decimal aceito; descrição pelo `MEMO`, ou `NAME` sem `MEMO`; arquivo com mais
    de uma conta recusado com `import.multiple_statements`. Linha sem `FITID` fica sem chave: a
    IMP-4 usa a impressão digital da IMP-3 como alternativa.
  - Estimativa: 5 · **Must** · Dependências: IMP-1
- ✅ **IMP-3** Ler CSV com mapeamento de colunas (domínio, sem I/O). → `Domain/Imports/Csv` + adaptador `Infrastructure/Imports/CsvHelperTableReader`.
  - *Dado* um CSV com `;`, decimal com vírgula e data `dd/MM/yyyy`, *quando* é lido com o
    mapeamento (data → coluna 1, descrição → coluna 2, valor → coluna 3), *então* as linhas saem com
    data, valor com sinal e descrição.
  - *Dado* um CSV com colunas separadas de débito e crédito, *quando* é lido, *então* débito vira
    valor negativo e crédito positivo.
  - *Dado* um CSV sem mapeamento informado, *quando* é lido, *então* o leitor detecta separador e
    cabeçalho e propõe um mapeamento, que a SPA mostra para o usuário confirmar (IMP-8).
  - *Dado* uma linha com data ou valor inválido, *quando* é lida, *então* ela sai marcada como
    **inválida** (com o motivo), sem derrubar o arquivo inteiro.
  - Impressão digital de cada linha para a deduplicação (decisão 3), com teste de dois gastos
    idênticos no mesmo dia.
  - Feito assim: o domínio continua sem pacote externo. Mapeamento (`CsvColumnMapping`), proposta
    (`CsvMappingDetector`), leitura das linhas (`CsvStatementParser`) e impressão digital
    (`StatementFingerprint`, também para OFX sem `FITID`) ficam no domínio. Só a separação em
    células usa o `CsvHelper` **34.0.0**, atrás da porta `ICsvTableReader`, na Infrastructure.
    Extras: linha `sep=;` do Excel, valores com `R$`, milhar e parênteses, data com hora e opção
    **inverter sinal** no mapeamento, para faturas que trazem a compra positiva (a SPA oferece na
    IMP-8). Aspas sem fechamento recusam o arquivo (`import.format_invalid`) e mapeamento
    incoerente volta `import.mapping_invalid`.
  - Estimativa: 5 · **Must** · Dependências: IMP-1
- ✅ **IMP-4** Enviar o arquivo e ver a prévia (API). → `POST /imports`, `POST /imports/csv/proposal`, `GET /imports/{id}`.
  - *Dado* um usuário com `expenses.write` e uma conta dele, *quando* envia um OFX em
    `POST /imports` (multipart, com a conta e, se for fatura, o cartão), *então* recebe o lote
    *Pendente* com as linhas: tipo (gasto/receita), data, valor, descrição, meio e categoria
    sugeridos, e a situação de cada uma (nova, **duplicada**, inválida, fora por padrão).
  - *Dado* linhas que já existem como lançamento na mesma conta, *quando* a prévia é montada,
    *então* elas aparecem como duplicadas e não incluídas.
  - *Dado* uma conta ou cartão de outro usuário, *quando* envia, *então* recebe 404 (o `accounts`
    é consultado uma vez por lote).
  - *Dado* um arquivo acima de 5 MB, com mais de 5.000 linhas ou de formato não aceito, *quando*
    envia, *então* recebe 400 com código estável (`import.file_too_large`, `import.too_many_lines`,
    `import.format_invalid`), e o conteúdo do arquivo não aparece em log, trace nem resposta.
  - `GET /imports/{id}` devolve o lote; lote de outro usuário = 404. Rota `/api/imports/**` no gateway.
  - Feito assim: multipart com `file`, `accountId`, `creditCardId` (o CSV vira fatura quando tem
    cartão), `paymentMethod` (meio padrão do extrato) e `mapping` (JSON, só CSV; sem ele,
    `import.mapping_required`). `POST /imports/csv/proposal` devolve a proposta de mapeamento sem
    guardar nada (base da IMP-8). O lançamento ganhou `external_id` (índice único com dono e conta)
    e `import_batch_id`; lote pendente vence em 7 dias e um worker de hora em hora apaga os vencidos.
    Limite de corpo no serviço e no gateway, que recusa pelo `Content-Length` sem repassar.
  - Revisão de segurança do envio (antes do merge): achados e corrigidos três vetores de negação de
    serviço que cabem em 5 MB — OFX com aninhamento profundo (estouro de pilha), CSV com milhões de
    linhas curtas (memória) e com milhares de colunas (CPU). Limites: 32 níveis e 100 mil
    elementos no OFX; 5.001 linhas e 50 colunas no CSV, conferidos durante a leitura.
  - Estimativa: 5 · **Must** · Dependências: IMP-2, IMP-3
- ✅ **IMP-5** Confirmar a importação (API). → `POST /imports/{id}/confirm`.
  - *Dado* um lote pendente, *quando* confirmo com as linhas incluídas, cada uma com categoria e meio
    de pagamento, *então* os lançamentos são criados numa transação só, cada um com seu `external_id`,
    e um `TransactionRegistered` por lançamento entra no outbox.
  - *Dado* uma linha incluída sem categoria, *quando* confirmo, *então* nada é criado e a resposta
    aponta as linhas pendentes.
  - *Dado* o mesmo lote confirmado duas vezes (clique duplo, reenvio), *quando* a segunda
    confirmação chega, *então* nenhum lançamento é duplicado e a resposta é a mesma da primeira.
  - *Dado* uma corrida em que outra importação criou o mesmo `external_id`, *quando* confirmo,
    *então* o índice único impede a duplicata e a linha volta como duplicada.
  - *Dado* a confirmação, *quando* a projeção processa os eventos, *então* o Resumo e o status dos
    orçamentos do mês incluem os importados no escopo PF/PJ da conta.
  - Feito assim: o corpo traz as decisões por linha (`id`, `included`, `category`,
    `paymentMethod`); linha omitida mantém a prévia. Sem categoria em linha incluída → 400
    `import.categories_required` com os números em `lines`. Duas guardas contra corrida: o lote
    grava com `WHERE status = 'Pending'` (concorrência otimista) e o índice único de `external_id`;
    quem perde recarrega e decide de novo (até 3 tentativas). Validado no MySQL: 8 confirmações
    paralelas do mesmo lote → 20 lançamentos, respostas idênticas; dois lotes do mesmo arquivo em
    paralelo → 20 importadas + 20 duplicadas. Contador `transactions.imports.lines`
    (`result`=imported|duplicate, `format`) no Prometheus.
  - Estimativa: 5 · **Must** · Dependências: IMP-4
- ✅ **IMP-6** Sugerir categoria e meio de pagamento pelo histórico. → `Transaction.DescriptionKey`, `FindLatestCategoriesAsync`, `Domain/Imports/PaymentMethodHints`.
  - *Dado* lançamentos anteriores com a descrição "UBER *TRIP" na categoria "Transporte", *quando*
    importo uma linha com "Uber *Trip", *então* a categoria sugerida é "Transporte" (descrição
    normalizada, a categoria mais recente vence).
  - *Dado* um memo com "PIX" ou "BOLETO" num extrato de conta, *quando* a prévia é montada, *então*
    o meio sugerido é Pix ou Boleto; sem pista, vale o meio padrão do lote.
  - Feito assim: o lançamento ganhou `description_key` (maiúsculas e espaços simples, a mesma
    normalização da impressão digital), com índice (dono, chave) e backfill na migração
    `TransactionDescriptionKey`. A prévia busca, para as descrições do arquivo, a categoria do
    lançamento **criado por último** com a mesma chave, em qualquer conta do usuário (nunca de outro
    usuário). "Mais recente" é a última categoria usada (`created_at_utc`), não a data do gasto: quem
    recategoriza um estabelecimento vê a escolha nova na próxima importação. A sugestão só vai para
    linhas que podem entrar (novas e fora por padrão), e confirmar sem mexer mantém a sugestão.
    Pistas no memo, por palavra inteira e só em extrato de conta: PIX → Pix, BOLETO → Boleto,
    TED → Transferência, SAQUE → Dinheiro; com mais de uma, vale a primeira no texto. Na SPA, os
    campos da prévia já vêm preenchidos; "aplicar às sem categoria" não sobrescreve a sugestão.
  - Estimativa: 3 · **Should** · Dependências: IMP-4
- ✅ **IMP-7** Aba Importar na SPA: envio, prévia e confirmação. → `views/ImportView.tsx`, `api/imports.ts`.
  - *Dado* o usuário logado, *quando* abre **Importar**, escolhe a conta (e o cartão, se for fatura)
    e envia o arquivo, *então* vê a prévia em tabela, com as duplicadas e as inválidas destacadas e
    fora da seleção.
  - *Dado* a prévia, *quando* edita categoria ou meio de uma linha, ou aplica uma categoria às linhas
    selecionadas, *então* o botão Confirmar só habilita quando toda linha incluída tem categoria.
  - *Dado* a confirmação, *quando* termina, *então* vê o resumo (quantas importadas, duplicadas e
    ignoradas) e um atalho para os Lançamentos.
  - Campo de categoria com as sugestões da BDG-7; `data-cy` nos elementos usados pelo E2E;
    acessível por teclado.
  - Feito assim: "Tipo" escolhe entre extrato da conta (com meio padrão, sem cartão de crédito)
    e fatura de um cartão da conta. "Selecionadas" = as linhas incluídas: a categoria em lote vale
    para as incluídas sem categoria ou para todas as incluídas. Arquivo acima de 5 MB ou de formato
    não aceito é barrado antes do envio. CSV usa o mapeamento proposto pelo servidor (fatura com
    sinal invertido); a escolha manual das colunas veio na IMP-8. Linhas apontadas pelo servidor
    em `import.categories_required` ficam destacadas. E2E básico em `cypress/e2e/import.cy.ts`.
  - Estimativa: 5 · **Must** · Dependências: IMP-5
- ✅ **IMP-8** Mapeamento de colunas do CSV na SPA. → `views/CsvMappingStep.tsx`.
  - *Dado* um CSV enviado, *quando* o servidor propõe o mapeamento, *então* vejo as primeiras linhas
    do arquivo e escolho qual coluna é data, descrição e valor (ou débito e crédito) antes da prévia.
  - Feito assim: todo CSV passa pelo passo de colunas, que parte da proposta do servidor (ou das
    três primeiras colunas, quando não há proposta) e mostra a amostra com o papel de cada coluna
    escrito no cabeçalho. Além das colunas: valor único ou débito/crédito, separador decimal,
    "primeira linha é cabeçalho" e "inverter sinal" (marcado por padrão na fatura). Colunas
    repetidas ou faltando bloqueiam a prévia.
  - Estimativa: 3 · **Must** · Dependências: IMP-3, IMP-7
- ✅ **IMP-9** Desfazer uma importação. → `POST /imports/{id}/undo`, `UndoImportHandler`; botão na tela de resultado da aba Importar.
  - *Dado* um lote confirmado, *quando* desfaço, *então* os lançamentos criados por ele (e ainda não
    excluídos) são excluídos, cada um com seu `TransactionDeleted`, e o Resumo e os orçamentos voltam
    ao que eram depois da projeção.
  - *Dado* um lote de outro usuário, *quando* tento desfazer, *então* recebo 404.
  - Feito assim: o lote vai para `Undone` (com `undoneAtUtc`, migração `ImportUndo`) na mesma
    gravação que exclui os lançamentos e escreve os eventos no outbox; as linhas ficam como registro
    do que foi importado. Idempotente: desfazer de novo devolve o mesmo lote sem excluir nada. Lote
    pendente → 409 `import.not_confirmed`; um lote desfeito não volta a ser confirmado (reenvia-se o
    arquivo, e as linhas voltam como novas). Corrida (o mesmo lote desfeito em paralelo, ou um
    lançamento excluído a mão no meio) recarrega e decide de novo, como a confirmação; provada ao
    vivo no MySQL: 5 desfazer em paralelo e desfazer junto com exclusões manuais dão sempre um único
    `TransactionDeleted` por lançamento. Na SPA, "Desfazer importação" pede confirmação antes. Só a
    importação recém-confirmada pode ser desfeita pela tela (não há lista de lotes). Métrica
    `transactions.imports.undone`; smoke com o passo de desfazer; E2E em `import.cy.ts`.
  - Estimativa: 3 · **Should** · Dependências: IMP-5
- ✅ **IMP-10** E2E Cypress: importações PF e PJ. → `cypress/e2e/import.cy.ts`.
  - *Dado* uma conta PF e um OFX sintético de extrato, *quando* importo com categorias e confirmo,
    *então* os lançamentos aparecem na aba Lançamentos e o Resumo reflete o total (esperando a
    projeção, como o `cy.waitForSummary`).
  - *Dado* uma conta PJ com cartão e um CSV sintético de fatura, *quando* importo, *então* o
    pagamento da fatura fica fora e o orçamento PJ da categoria sobe (`cy.waitForBudget`).
  - *Dado* o mesmo arquivo importado de novo, *quando* vejo a prévia, *então* todas as linhas vêm
    como duplicadas.
  - Feito assim: o teste OFX da IMP-7 passou a esperar o Resumo (os blocos do Resumo somam só
    gastos, então a receita do extrato não entra em "Por categoria"), e um teste novo importa uma
    fatura CSV num cartão PJ com orçamentos PJ e PF da mesma categoria: o pagamento da fatura vem
    "fora por padrão" e não vira lançamento, o orçamento PJ sobe e o PF fica zerado. As datas da
    fatura são do mês corrente, porque o acompanhamento abre no mês do navegador. No reenvio, as
    linhas já lançadas vêm duplicadas; o pagamento da fatura, que nunca foi lançado, continua fora
    por padrão (não há o que deduplicar). Suíte E2E 7/7, já no job do CI.
  - Estimativa: 5 · **Must** · Dependências: IMP-7, IMP-8
- ✅ **IMP-11** Guardar o mapeamento de CSV por banco. → `SavedCsvMapping`, `/imports/csv/mappings`; seletor e "Salvar como" em `views/CsvMappingStep.tsx`.
  - *Dado* um mapeamento confirmado para um CSV, *quando* salvo com um nome ("Banco X — fatura"),
    *então* nas próximas importações ele aparece para escolher, sem remapear.
  - Feito assim: tabela `csv_mappings` no `transactions` (migração `SavedCsvMappings`), com o
    mapeamento e o cabeçalho do arquivo em JSON (só os nomes das colunas, nunca as linhas). Nome
    único por usuário sem diferença de maiúsculas: salvar de novo com o mesmo nome substitui (200;
    novo = 201). Até 50 por usuário (409 `import.mappings_limit`); `GET`, `POST` e
    `DELETE /imports/csv/mappings`, sempre do dono (404 para o de outro). A validação do mapeamento
    passou para `CsvColumnMapping.IsValid`, a mesma usada na leitura do CSV. Na SPA, o passo de
    colunas lista os salvos e já escolhe o do arquivo com o mesmo cabeçalho; "Salvar como" grava
    ao ver a prévia; dá para excluir o escolhido. Mapeamento que aponta para coluna que o arquivo
    não tem bloqueia a prévia.
  - Estimativa: 3 · **Could** · Dependências: IMP-8

**Fora do escopo (Won't, nesta versão):** conexão direta com o banco (Open Finance); importar PDF de
fatura; editar lançamento já confirmado (continua sendo excluir e lançar de novo); conciliação de
fatura de cartão (fica com o épico de fechamento de fatura); regras automáticas de categorização
além do histórico; conversão de moeda (o lote usa a moeda do arquivo, padrão BRL).

**Priorização (MoSCoW):** Must = IMP-1, 2, 3, 4, 5, 7, 8, 10 (35 pts), o mínimo para importar os dois
formatos com prévia e provar ponta a ponta nos dois escopos. Should = IMP-6, 9 (6 pts): a IMP-6 tira
o trabalho manual que sobra (categorizar linha a linha) e a IMP-9 é a rede de segurança de uma
importação errada, já que não há edição. Could = IMP-11 (3 pts). Total: 44 pts.

**Definition of Ready (por história):** decisões da IMP-1 aceitas; arquivos de exemplo sintéticos
(OFX 1.x, OFX 2.x, CSV de extrato e de fatura) no repositório antes das histórias de leitura;
contrato da API descrito na história de backend antes da de frontend.

**Definition of Done do épico:**
- CI verde no PR: `build & test` dos serviços afetados e `Cypress E2E` (os 5 checks exigidos no
  `main` depois do épico IDX).
- Testes unitários da leitura de OFX e CSV (formatos, codificações, linhas inválidas, XXE) e da
  deduplicação.
- Testes de integração (WebApplicationFactory): isolamento por dono (404 em lote, conta ou cartão
  alheio), 401/403 por escopo, limites de arquivo, confirmação idempotente, corrida no índice único,
  eventos no outbox.
- Revisão de segurança do upload (tamanho, formato, XXE, dados sensíveis fora de log e trace) pelo
  `security-specialist` antes do merge da IMP-4.
- E2E das jornadas PF e PJ (IMP-10) no job do CI; smoke test (`build/smoke-test.mjs`) cobrindo
  enviar, confirmar e ler o lançamento pelo gateway.
- Migração EF no `expense_transactions`; logs, traces e métricas HTTP chegando pelo pipeline da OBS
  (contadores de linhas importadas e duplicadas).
- `docs/architecture.md`, a ADR da IMP-1 e o histórico do `docs/PROMPT.md` atualizados; validado ao
  vivo no `docker compose`.

---

## 🟢 EPIC INV — Fatura de cartão e previsão de caixa  · *Done*
**Problema:** o cartão de crédito é o meio mais usado, mas o produto só enxerga a compra no dia em
que ela aconteceu. O usuário não sabe quanto vai pagar na próxima fatura, em que dia, nem quanto as
compras de hoje vão pesar no caixa dos próximos meses. Para piorar, pagar a fatura pela conta e
registrar esse pagamento como gasto conta o mesmo dinheiro duas vezes (as compras no cartão e o
pagamento delas).
**Objetivo (outcome):** para cada cartão, o usuário vê a fatura atual (aberta), as próximas e as já
fechadas, com total, data de fechamento, vencimento e situação (aberta, fechada, paga, parcialmente
paga, vencida). Registra o pagamento da fatura sem que ele vire gasto. Vê uma previsão de caixa
mês a mês, que junta o que já está lançado com data futura e as faturas a vencer, separada por
escopo PF/PJ.
**Métricas de sucesso:** (1) o total de uma fatura fechada bate com a soma das compras do ciclo
(conferido contra a fatura importada do banco); (2) nenhum pagamento de fatura entra como gasto no
Resumo ou nos orçamentos; (3) a previsão dos próximos meses mostra o que sai por causa do cartão,
inclusive parcelas; (4) as jornadas PF e PJ (comprar, fechar, pagar, prever) passam no E2E do CI.
**Rastreabilidade:** `docs/PROMPT.md` → Domínio ("controle de gastos PF e PJ"; cartões de crédito com
ciclo de cobrança). Ideia promovida de "Ideias de produto" a pedido do dono do produto
(2026-11-21). Recebe a "conciliação de fatura de cartão" que o épico IMP deixou fora do escopo.

**Achados no código que moldam o épico:**
- **O cartão já tem ciclo:** `CreditCard` (accounts) guarda `ClosingDay` e `DueDay` (1 a 28, de
  propósito, para todo mês ter o dia) e `CreditLimit`. Não há edição de cartão: só criar e
  desativar. Mudar o dia de fechamento hoje é criar outro cartão.
- **Não existe fatura.** Nada agrupa as compras de um cartão por ciclo; o lançamento só tem
  `OccurredOn` e `CreditCardId`. Resumo e orçamentos contam pela data da compra (competência), e
  isso continua certo para eles: a fatura é a visão de **caixa**, não de competência.
- **O evento não leva o cartão.** `TransactionRegistered` (EVT-1) tem conta, escopo, meio, categoria,
  valor e data, mas **não** o `CreditCardId`. A projeção do `reporting` (`ProjectedTransaction`)
  também não. Sem isso, o lado de leitura não sabe de que cartão é cada compra.
- **O reporting não fala com o accounts.** Desde o EVT, ele só lê o feed de eventos; não conhece o
  ciclo nem o limite dos cartões.
- **Não há parcelamento.** Uma compra em 10x vira um lançamento só, com o valor cheio na data da
  compra. Para a fatura e para o caixa, isso está errado: cada parcela cai numa fatura.
- **Não há saldo de conta.** `Account` não tem saldo inicial nem saldo corrente; o produto registra
  fluxos, não posição. Uma previsão de "saldo" precisaria de um ponto de partida que não existe.
- **Pagamento de fatura vira gasto hoje.** Na importação, o crédito da fatura (o pagamento) já fica
  fora por padrão (IMP). Mas no **extrato da conta** a mesma saída ("PAG FATURA") é um débito comum
  e entra como gasto, duplicando as compras do cartão. Lançar o pagamento à mão tem o mesmo efeito.
- **Lançamento com data futura é aceito** (`Transaction.Create` não limita a data). É o que permite
  uma previsão a partir do que já está lançado, sem recorrência.
- **Exclusão é física e não há edição.** Uma compra parcelada errada se corrige excluindo e lançando
  de novo; a fatura e a previsão precisam acompanhar a exclusão (`TransactionDeleted`).

**Decisões (aceitas na [ADR-0005](adr/0005-fatura-de-cartao-e-previsao-de-caixa.md), INV-1; ficou a recomendação de cada uma):**
1. **Onde a fatura é calculada.** Opções: (a) no `reporting`, a partir da projeção, como os
   orçamentos (ADR-0002); (b) no `transactions`, que já tem o `CreditCardId` e chama o accounts.
   *Recomendação: (a)*. Fatura e previsão são leitura e agregação; as escritas (compra, parcelas,
   pagamento) ficam no `transactions`. Custa levar o cartão e as parcelas no evento (decisão 2) e o
   `reporting` ler o ciclo dos cartões no accounts (decisão 3). A (b) evita mudar o evento, mas
   espalha o lado de leitura em dois serviços: a previsão de caixa precisaria das faturas e dos demais
   lançamentos juntos.
2. **Contrato do evento.** *Recomendação:* `TransactionRegistered` **versão 2**, com `CreditCardId` e
   `Installments` (1 quando à vista). O `reporting` aceita v1 e v2. Os lançamentos anteriores ganham
   os campos por um backfill no `transactions`, no padrão da EVT-4: um
   `TransactionCardDetailsResolved` por lançamento de cartão já existente, idempotente.
3. **Ciclo do cartão no reporting.** Opções: (a) ler `GET /credit-cards` no accounts na hora da
   consulta, com o token do usuário (poucos cartões por usuário); (b) eventos de cartão do accounts
   para uma projeção. *Recomendação: (a)*. O ciclo não muda (não há edição de cartão), a consulta é
   barata e evita um segundo feed de eventos. Se um dia o cartão ganhar edição, a (b) volta à mesa.
4. **Em que fatura cai uma compra.** *Recomendação:* compra **até o dia de fechamento** (inclusive)
   cai na fatura que fecha naquele mês; **depois** dele, na do mês seguinte. O vencimento é no mesmo
   mês do fechamento quando `DueDay > ClosingDay`, e no mês seguinte quando não. A fatura é
   identificada por cartão e **mês de vencimento** (`2026-12`), como o banco a chama. Sem calendário
   de dias úteis: fechamento e vencimento no fim de semana ficam no dia cadastrado (o banco costuma
   adiar o vencimento; registrar como limitação conhecida).
5. **Parcelamento.** Opções: (a) fora do escopo: a compra entra cheia na fatura da compra;
   (b) compra com número de parcelas (2 a 24), **um lançamento só**, e a fatura distribui as parcelas
   nas faturas seguintes; (c) N lançamentos, um por parcela. *Recomendação: (b)*. Resumo e orçamentos
   seguem contando a compra inteira na data da compra (competência, sem mudar a BDG nem o Resumo);
   só fatura, limite e previsão olham as parcelas. Excluir a compra tira todas as parcelas de uma vez.
   A parcela é `valor / n` arredondada a centavos, com a diferença na primeira. Juros de parcelamento
   ficam de fora (o valor lançado já é o total).
6. **Pagamento de fatura.** Opções: (a) lançamento de um tipo novo (nem gasto nem receita);
   (b) registro próprio, `card_payments` no `transactions` (cartão, mês de vencimento da fatura, conta
   de onde saiu, valor, data), com o evento `CardInvoicePaid` no outbox. *Recomendação: (b)*. O
   pagamento não é gasto: não pode cair no Resumo, nos orçamentos, no "Sem orçamento" nem nas
   sugestões de categoria, e um tipo novo de lançamento obrigaria todos esses pontos a filtrá-lo.
   Pagamento parcial e pagamento acima do total são aceitos (a diferença fica visível na fatura).
   Excluir o pagamento é permitido (`CardInvoicePaymentDeleted`).
7. **Situação da fatura.** *Recomendação:* calculada, nunca guardada. **Aberta** até o fechamento;
   **Fechada** entre o fechamento e o vencimento; **Paga** quando os pagamentos cobrem o total;
   **Parcialmente paga** quando cobrem parte; **Vencida** depois do vencimento sem cobrir o total.
   Estorno (receita no cartão) abate do total da fatura em que cai.
8. **Previsão de caixa.** Opções: (a) fluxo mês a mês (entradas, saídas fora do cartão, faturas a
   vencer e o resultado do mês), sem saldo; (b) saldo projetado, exigindo saldo inicial por conta.
   *Recomendação: (a)* no Must, com horizonte de 6 meses a partir do mês atual, por escopo PF/PJ e
   total. Cartão entra pela **fatura no mês de vencimento**, nunca pela data da compra (senão conta
   duas vezes). Pagamentos de fatura já feitos saem do que falta pagar. A (b) vira uma história Could
   (saldo inicial informado pelo usuário). A previsão só conhece o que está lançado: não há
   recorrência, e isso precisa estar escrito na tela.
9. **Limite disponível.** *Recomendação:* limite menos o que ainda não foi pago de todas as faturas
   do cartão, **incluindo parcelas futuras** (é assim que o banco reserva o limite). Só leitura, sem
   bloquear lançamento acima do limite (o produto registra, não autoriza).
10. **Pagamento de fatura no extrato importado.** *Recomendação:* na prévia de um extrato de conta, a
    linha cujo memo indica pagamento de fatura ("PAG FATURA", "PAGAMENTO FATURA", "PGTO CARTAO") vem
    sugerida como **pagamento de fatura** de um cartão da conta (escolhido pelo usuário quando houver
    mais de um), e a confirmação cria o `card_payment` em vez de um gasto. Reaproveita as pistas no
    memo da IMP-6.
11. **Rotas e escopos.** *Recomendação:* faturas em `GET /reports/cards/{id}/invoices` e previsão em
    `GET /reports/cash-forecast` (reporting, escopo `expenses.read`); pagamentos em
    `/transactions/card-payments` (escopo `expenses.write`). Tudo pela rota existente do gateway; o
    cartão de outro usuário dá 404.

- ✅ **INV-1** ADR: fatura, parcelas, pagamento e previsão de caixa. → [ADR-0005](adr/0005-fatura-de-cartao-e-previsao-de-caixa.md), Aceita.
  - *Dado* as decisões em aberto 1 a 11 deste épico, *quando* o dono do produto revisa as opções,
    *então* a ADR é registrada como **Aceita** em `docs/adr/` (0005, com o índice atualizado) e o PO
    ajusta as histórias seguintes ao que foi decidido.
  - Refinamentos registrados na ADR além das recomendações: `TransactionCardDetailsResolved` aplicado
    só em linha que já existe na projeção; cartão que o accounts não devolve ao usuário = 404;
    parcelas acima de 1 só com `CreditCard`; saldo devedor de fatura vencida não passa para a seguinte
    (rotativo fora); na previsão, faturas pelo total do mês de vencimento, com pago e a pagar, e o
    pagamento de fatura não conta de novo; conta de origem do pagamento = qualquer conta ativa do
    usuário; `expenses.read` para ler pagamentos.
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **INV-2** Cartão e parcelas no evento, com backfill dos lançamentos anteriores. → `TransactionRegistered` v2, `CardDetailsBackfill`, projeção com `description`/`credit_card_id`/`installments`.
  - *Dado* uma compra no cartão, *quando* é registrada, *então* o `TransactionRegistered` sai na
    versão 2 com `creditCardId` e `installments`, e a projeção do `reporting` guarda os dois.
  - *Dado* lançamentos de cartão gravados antes da v2, *quando* o `transactions` sobe, *então* cada um
    ganha um `TransactionCardDetailsResolved` uma vez só (reiniciar não duplica) e a projeção é
    completada.
  - *Dado* um evento v1 ainda no feed, *quando* o `reporting` o lê, *então* ele é aplicado como antes
    (à vista, sem cartão até o backfill).
  - **Acréscimo à ADR-0005 (decisão 2), pedido pelo dono do produto na INV-2:** a v2 e o
    `TransactionCardDetailsResolved` também levam a **descrição** do lançamento, para a fatura listar
    "Notebook Loja X — parcela 1/3" em vez de só categoria e valor.
  - Feito assim: o lançamento ganhou `installments` (sempre 1 até a INV-5; migração
    `TransactionInstallments`). O backfill roda na subida, logo depois da carga inicial do outbox,
    sem depender do accounts (os dados estão no próprio lançamento). Ele grava o evento de detalhes
    para toda compra no cartão sem `TransactionRegistered` v2 nem detalhes já resolvidos, e por isso
    é idempotente pelo próprio outbox. O `reporting` lê v1 como à vista, sem cartão nem descrição, e
    aceita até a v2 do `TransactionRegistered` (v3 aborta o lote, como antes). O evento de detalhes
    só vale em linha que já existe. A projeção ganhou `description`, `credit_card_id` e `installments`
    (migração `ProjectionCardDetails`, índice dono + cartão). Lançamentos fora do cartão registrados
    em v1 ficam sem descrição na projeção, porque só a fatura usa a descrição. Validado ao vivo: 26
    compras de cartão legadas completadas, as 26 com cartão e descrição na projeção, reinício sem
    regravar, evento novo na v2, smoke 25/25 e E2E 8/8.
  - Estimativa: 5 · **Must** · Dependências: INV-1
- ✅ **INV-3** Regra do ciclo: fatura de cada compra e de cada parcela (domínio, sem I/O). → `reporting/.../Domain/Invoices/` (`CardCycle`, `Installments`, `InvoiceCalculator`).
  - *Dado* um cartão que fecha no dia 10 e vence no dia 17, *quando* a compra é no dia 10, *então*
    cai na fatura que vence dia 17 do mesmo mês; no dia 11, na do mês seguinte.
  - *Dado* um cartão que fecha no dia 25 e vence no dia 5, *quando* a compra é no dia 20 de
    novembro, *então* a fatura vence em 5 de dezembro.
  - *Dado* uma compra de R$ 100,00 em 3x, *quando* é distribuída, *então* as parcelas são 33,34,
    33,33 e 33,33 em três faturas seguidas, a partir da fatura da compra.
  - *Dado* uma receita no cartão (estorno), *quando* cai num ciclo, *então* abate do total daquela
    fatura.
  - Feito assim: `CardCycle` dá o mês de vencimento de cada data e as datas de cada fatura (início
    do ciclo, fechamento, vencimento); vencimento no mesmo dia do fechamento conta como mês seguinte.
    `Installments.Split` arredonda para baixo em centavos e põe a diferença na primeira parcela.
    `InvoiceCalculator` monta uma fatura por mês de vencimento com item ou pagamento, com total, pago,
    a pagar (nunca negativo) e situação, nesta ordem: aberta até o fechamento (mesmo com pagamento
    antecipado), paga se os pagamentos cobrem o total, vencida depois do vencimento, parcialmente
    paga, fechada. Fatura fechada só com estorno fica paga (nada a pagar). O item ainda não tem a
    descrição da compra, porque o evento não a leva (ponto para a INV-2).
  - Estimativa: 3 · **Must** · Dependências: INV-1
- ✅ **INV-4** API das faturas de um cartão. → `GET /reports/cards/{id}/invoices` (`GetCardInvoicesHandler`, `AccountsCardDirectory`).
  - *Dado* um cartão do usuário, *quando* consulto `GET /reports/cards/{id}/invoices`, *então*
    recebo as faturas (fechadas, a atual e as próximas com parcelas), cada uma com mês de
    vencimento, datas de fechamento e vencimento, total, pago, a pagar, situação e os itens (compra
    ou parcela "3/10").
  - *Dado* o cartão de outro usuário, *quando* consulto, *então* recebo 404; sem token, 401.
  - *Dado* uma compra excluída, *quando* a projeção aplica o `TransactionDeleted`, *então* ela e suas
    parcelas somem de todas as faturas.
  - Feito assim: o `reporting` lê o cartão em `GET /credit-cards/{id}` do accounts com o token do
    usuário (404 do accounts = `card.not_found`; outra falha vira erro 500, porque sem ciclo não há
    fatura). As compras vêm da projeção (índice dono + cartão) e a regra é a da INV-3. A resposta traz
    o cartão (apelido, final, dias), a moeda, `currentDueMonth` e as faturas, cada uma com
    `dueMonth`, início, fechamento, vencimento, total, pago, a pagar, situação e itens (descrição,
    categoria, data da compra, valor, parcela/de). A fatura atual sempre vem, mesmo vazia. Recorte
    por `from`/`to` (`yyyy-MM`, mês de vencimento; 400 `invoice.month_invalid`); sem `from`, começa
    12 faturas antes da atual, e sem `to`, vai até a última parcela. O "hoje" que decide a situação
    usa `Reports:TimeZone` (padrão `America/Sao_Paulo`; o container tem o banco de fusos); sem o
    fuso na máquina, cai para UTC. Pago fica 0 até a INV-6, então faturas antigas aparecem como
    vencidas. Compose: `reporting` ganhou `Services__AccountsBaseUrl` e passa a depender do
    `accounts`. Smoke com fatura e 404 (28 passos).
  - Estimativa: 5 · **Must** · Dependências: INV-2, INV-3
- ✅ **INV-5** Compra parcelada.
  - *Dado* o formulário de lançamento com meio cartão de crédito, *quando* informo 10 parcelas,
    *então* o lançamento é criado com `installments = 10` e o valor total.
  - *Dado* parcelas fora de 1 a 24, ou parcelas num meio que não é cartão de crédito, *quando* envio,
    *então* recebo 400 com código estável.
  - *Dado* uma compra parcelada, *quando* olho o Resumo e os orçamentos, *então* ela conta inteira no
    mês da compra (sem mudança de comportamento).
  - Feito assim: `POST /transactions` aceita `installments` (opcional, padrão 1) e a resposta o
    devolve. Fora de 1 a 24 = 400 `transaction.installments_invalid`; acima de 1 num meio que não é
    `CreditCard` (inclusive débito, que também pede cartão) = 400
    `transaction.installments_not_allowed`. O valor lançado é o total; o evento v2 já levava as
    parcelas (INV-2), então fatura e previsão passam a dividi-las sem mudança no reporting. Na SPA,
    o seletor "À vista, 2x … 24x" aparece só com cartão de crédito, e a lista de lançamentos mostra
    "em 3x" abaixo do valor. Cypress `card-invoices.cy.ts` ganhou a compra em 3x (1/3 na fatura
    atual, 2/3 e 3/3 nas seguintes); smoke com 3x e Pix parcelado recusado (34 passos).
  - Estimativa: 5 · **Should** · Dependências: INV-2
- ✅ **INV-6** Registrar e excluir o pagamento de uma fatura.
  - *Dado* uma fatura de um cartão do usuário, *quando* registro um pagamento (conta de origem,
    valor, data), *então* ele é gravado com um `CardInvoicePaid` no outbox e a fatura passa a Paga ou
    Parcialmente paga.
  - *Dado* um pagamento registrado, *quando* olho o Resumo, os orçamentos e o "Sem orçamento",
    *então* ele não aparece como gasto em nenhum deles.
  - *Dado* um pagamento registrado por engano, *quando* o excluo, *então* a fatura volta à situação
    anterior.
  - *Dado* cartão ou conta de outro usuário, *quando* tento pagar, *então* recebo 404.
  - Feito assim: `POST /transactions/card-payments` (`creditCardId`, `invoiceDueMonth` em
    `yyyy-MM`, `accountId`, `amount`, `paidOn`; `expenses.write`) grava em `card_payments` e põe o
    `CardInvoicePaid` no outbox no mesmo commit; `DELETE /transactions/card-payments/{id}` grava o
    `CardInvoicePaymentDeleted`; `GET` (`expenses.read`) lista os do dono, filtrando por cartão e
    mês. Cartão e conta são validados no accounts com o token do usuário (404 `card.not_found` /
    `account.not_found`); conta desativada = 400 `card_payment.account_inactive`. Mês inválido = 400
    `card_payment.month_invalid`; valor ≤ 0 ou com mais de 2 casas = 400 do `money.*`. Os eventos têm
    o pagamento como agregado: o payload traz `paymentId` e não traz `transactionId`. O reporting
    projeta os pagamentos em `projected_card_payments`, separada dos lançamentos (por isso Resumo,
    orçamentos e "Sem orçamento" não os enxergam), e a fatura passa a ter `paid`, `remaining` e a
    lista `payments` (id, conta, valor, data), que a tela da INV-7 usa para excluir. Smoke com
    pagamento, resumo inalterado e exclusão (31 passos).
  - Estimativa: 5 · **Must** · Dependências: INV-4
- ✅ **INV-7** Faturas na SPA.
  - *Dado* o usuário logado, *quando* abre a fatura de um cartão (a partir da aba Cartões), *então*
    vê a fatura atual e navega pelas anteriores e próximas, com total, vencimento, situação e itens.
  - *Dado* uma fatura fechada ou vencida, *quando* clica em "Registrar pagamento", *então* informa
    conta, valor (o que falta, por padrão) e data, e a situação muda.
  - Feito assim: botão "Faturas" em cada cartão da aba Cartões abre a tela da fatura atual, com
    setas para as anteriores e próximas (só as que têm compra ou pagamento, mais a atual). Mostra
    situação (Aberta, Fechada, Parcialmente paga, Vencida, Paga), total, pago, falta pagar,
    fechamento e vencimento, as compras com a parcela ("2/3" ou "à vista") e os pagamentos, cada um
    com "excluir". "Registrar pagamento" aparece enquanto falta pagar, inclusive na fatura aberta
    (pagamento antecipado); o formulário sugere a conta do cartão, o que falta e a data de hoje.
    Como a fatura lê a projeção, depois de pagar ou excluir a tela recarrega até a mudança chegar
    (até 20 s). Sem framework de teste unitário na SPA: typecheck, build e o Cypress
    `card-invoices.cy.ts` (fatura atual aberta, fatura vencida de dois meses atrás, pagamento
    parcial, pagamento do resto, exclusão). Suíte com 9 testes.
  - Estimativa: 5 · **Must** · Dependências: INV-4, INV-6
- ✅ **INV-8** API da previsão de caixa.
  - *Dado* lançamentos com data futura, compras no cartão e pagamentos, *quando* consulto
    `GET /reports/cash-forecast?months=6`, *então* recebo, por mês e por escopo (PF, PJ e total):
    entradas, saídas fora do cartão, faturas a vencer (o que falta pagar) e o resultado do mês.
  - *Dado* uma compra no cartão em 10x, *quando* olho a previsão, *então* cada parcela aparece no mês
    de vencimento da sua fatura, e a compra não aparece de novo na data em que foi feita.
  - *Dado* uma fatura já paga, *quando* olho a previsão, *então* ela não conta como saída a vencer.
  - Feito assim: `GET /reports/cash-forecast?months=N` (1 a 12, padrão 6; fora disso 400
    `forecast.months_invalid`) começa no mês atual (fuso `Reports:TimeZone`). Cada mês traz
    `personal`, `business` e `total`, cada um com `income`, `expense`, `invoices` (total das faturas
    que vencem no mês), `invoicesPaid`, `invoicesRemaining` (o que falta: fatura paga = 0) e
    `result` = entradas − saídas − faturas (total, como na ADR, para o resultado não mudar quando a
    fatura é paga antes). Fora do cartão = sem cartão e meio diferente de `CreditCard`, pela data.
    As faturas são as da INV-4, de todos os cartões do usuário (inclusive desativados, que ainda
    podem ter parcelas), no escopo da conta do cartão: o reporting lê `GET /credit-cards` e
    `GET /accounts` do accounts com o token do usuário e junta pelo id da conta. Lançamento sem
    escopo conhecido entra só no total. A resposta traz `notice` ("só o que já está lançado") para
    a tela da INV-9. Regra pura em `CashForecastCalculator` (Domain/Forecast). Smoke com previsão e
    400 (33 passos).
  - Estimativa: 5 · **Must** · Dependências: INV-4, INV-6
- ✅ **INV-9** Previsão de caixa na SPA.
  - *Dado* o usuário logado, *quando* abre a previsão, *então* vê os próximos 6 meses com entradas,
    saídas, faturas e resultado, com filtro PF/PJ, e um aviso de que só entra o que já está lançado.
  - Feito assim: aba "Previsão" (depois do Resumo) com tabela por mês (entradas, saídas, faturas
    com "falta pagar"/"paga" e resultado em verde ou vermelho) e linha de soma do período;
    filtro de escopo (Total, PF, PJ) e horizonte (3, 6 ou 12 meses); o aviso vem da API, com a nota
    de que o cartão entra pela fatura. A tabela rola na horizontal em tela estreita. Cypress
    `cash-forecast.cy.ts` compara antes e depois (o usuário demo acumula dados): gasto Pix com data
    daqui a 3 meses entra nas saídas daquele mês; compra no cartão hoje entra nas faturas do mês de
    vencimento e não nas saídas do mês atual. O formulário de lançamento só registra gastos, então
    receita futura fica coberta pelos testes de integração da INV-8. Suíte com 10 testes.
  - Estimativa: 3 · **Must** · Dependências: INV-8
- ✅ **INV-10** Limite disponível do cartão.
  - *Dado* um cartão com limite de R$ 5.000,00 e R$ 1.200,00 a pagar (incluindo parcelas futuras),
    *quando* olho o cartão, *então* vejo R$ 3.800,00 disponíveis, sem bloquear novos lançamentos.
  - Feito assim: em aberto = soma do que falta pagar de todas as faturas do cartão (as vencidas, a
    atual e as das parcelas futuras); pagamento acima do total de uma fatura não abate as outras;
    estorno abate a fatura em que cai. Disponível = limite − em aberto, negativo quando passou (nada
    é bloqueado). `GET /reports/cards/limits` devolve `creditLimit`, `outstanding` e
    `availableLimit` de todos os cartões do usuário numa chamada (cartões lidos do accounts com o
    token); a resposta da fatura (`/reports/cards/{id}/invoices`) traz os mesmos três campos, sem
    depender do recorte `from`/`to`. O cálculo de faturas de um cartão virou um ponto só
    (`CardInvoiceSource`), usado por fatura, previsão e limite. Na SPA, a lista de cartões ganhou a
    coluna "Disponível" (em vermelho se negativo; o título mostra o em aberto), e a tela de faturas,
    a linha "Limite · em aberto · disponível". Ao voltar da fatura, a lista recarrega (um pagamento
    muda o disponível). Cypress `card-invoices.cy.ts` confere 2.530,00 nas duas telas; smoke com o
    limite (35 passos).
  - Estimativa: 3 · **Should** · Dependências: INV-4
- ✅ **INV-11** Pagamento de fatura reconhecido na importação do extrato.
  - *Dado* um extrato de conta com a linha "PAG FATURA CARTAO", *quando* vejo a prévia, *então* ela
    vem sugerida como pagamento de fatura de um cartão da conta, e confirmar cria o pagamento, não
    um gasto.
  - *Dado* a sugestão errada, *quando* troco a linha para lançamento comum, *então* ela entra como
    gasto, como hoje.
  - Feito assim: na prévia de extrato de conta, a saída cujo memo tem "PAG FATURA", "PAGAMENTO
    (DE) FATURA", "PAGTO/PGTO FATURA" ou "PAG/PAGTO/PGTO/PAGAMENTO (DE) CARTAO" (palavras inteiras,
    sem diferença de maiúsculas e acentos; entrada e fatura de cartão nunca) vem com
    `invoicePayment = true`. Se a conta tem um só cartão ativo, a linha já vem com ele e com a
    fatura de vencimento mais próximo da data paga (mês anterior, mesmo ou seguinte); com mais de
    um, o usuário escolhe. Na confirmação, a linha vira `card_payments` com a chave externa e o lote
    (`CardInvoicePaid` no outbox), sem categoria; faltando cartão ou mês, 400
    `import.invoice_required` com as linhas; cartão de outro usuário = 404. A decisão aceita
    `invoicePayment`, `invoiceCardId` e `invoiceDueMonth` (`yyyy-MM`; inválido = 400
    `import.invoice_month_invalid`); `invoicePayment = false` faz a linha voltar a gasto comum, com
    categoria. Reimportar o extrato traz a linha paga como duplicada (a dedup olha lançamentos e
    pagamentos; índice único dono + conta + chave em `card_payments`), e desfazer o lote exclui
    também os pagamentos (`CardInvoicePaymentDeleted`). Na SPA, o meio da linha ganhou a opção
    "Pagamento de fatura", que troca a categoria por cartão + mês. Cypress
    `import-invoice-payment.cy.ts` (sugestão, confirmação, Lançamentos sem o pagamento, pagamento
    na fatura).
  - Estimativa: 3 · **Should** · Dependências: INV-6, IMP-6
- ✅ **INV-12** E2E Cypress: fatura e previsão nas jornadas PF e PJ.
  - *Dado* um cartão PF, *quando* lanço uma compra à vista e outra em 3x, *então* a fatura atual e as
    duas seguintes mostram as parcelas e a previsão mostra cada uma no mês de vencimento.
  - *Dado* um cartão PJ com fatura fechada, *quando* registro o pagamento, *então* a fatura fica Paga,
    a previsão deixa de contá-la e o Resumo PJ não ganha gasto novo.
  - Feito assim: `invoice-forecast-journeys.cy.ts`, dois testes pela SPA, comparando previsão e
    Resumo por diferença (o usuário demo acumula dados). PF: cartão fecha 10/vence 17, compra de
    R$ 50 à vista e R$ 300 em 3x hoje; a previsão PF ganha 150, 100 e 100 nas faturas dos três meses
    de vencimento, as saídas do mês atual não mudam, e as faturas mostram à vista + 1/3, 2/3 e 3/3.
    PJ: o ciclo do cartão é montado a partir da data do teste para haver uma fatura já fechada e
    ainda não vencida (fecha ontem e vence dia 28; nos dias 1 e 29 a 31, fecha 28 e vence 5), com a
    compra no dia do fechamento; o pagamento do valor sugerido deixa a fatura Paga, a previsão PJ
    mantém o total da fatura no mês e tira 456,78 do "falta pagar", e o Resumo PJ fica só com a
    compra. Suíte com 12 testes, sem retry.
  - Estimativa: 5 · **Must** · Dependências: INV-7, INV-9
- ✅ **INV-13** Saldo inicial por conta na previsão. → saldo na conta (`accounts`), saldo projetado na previsão.
  - *Dado* uma conta com saldo informado pelo usuário numa data, *quando* olho a previsão, *então*
    vejo também o saldo projetado no fim de cada mês, por escopo.
  - Feito assim (decisões no refinamento da [ADR-0005](adr/0005-fatura-de-cartao-e-previsao-de-caixa.md)):
    a conta ganhou `openingBalance` e `openingBalanceOn` (saldo no fim do dia; colunas na migração
    `AccountOpeningBalance`), com `PUT /accounts/{id}/opening-balance` (data futura = 400
    `account.opening_balance_date_future`; mais de 2 casas = 400 `account.opening_balance_invalid`;
    faltando campo = 400 `account.opening_balance_required`) e `DELETE` para tirar. O reporting lê as
    contas na hora (`ICardDirectory.ListAccountsAsync`) e calcula, numa regra pura
    (`ProjectedBalanceCalculator`), o saldo de cada conta com saldo: o informado, mais entradas e menos
    saídas fora do cartão depois do dia, menos os pagamentos de fatura feitos dela depois do dia,
    menos o que falta pagar das faturas dos cartões dela que vencem do mês atual até o mês. Cada
    escopo da previsão ganhou `balance` (nulo sem conta com saldo) e a resposta, `accountsWithoutBalance`.
    Na SPA, a aba Contas tem a coluna "Saldo" (`account-balance`, informar/alterar/remover na própria
    linha) e a Previsão, a coluna "Saldo no fim do mês" (`forecast-balance`) e o aviso das contas sem
    saldo (`forecast-balance-notice`). **Lição:** o provider Oracle MySQL não lê `DateOnly` (devolve
    `DateTime` e o EF falha no cast); o accounts ganhou o mesmo `DateOnlyConverter` do transactions, e
    os testes de integração (sem MySQL) não pegam isso: o smoke pega. Smoke passo 23 (45 passos).
    Cypress `forecast-balance.cy.ts` (20 testes na suíte).
  - Estimativa: 3 · **Could** · Dependências: INV-8

**Fora do escopo (Won't, nesta versão):** editar o ciclo do cartão (continua sendo criar outro
cartão); calendário de dias úteis e feriados no fechamento e no vencimento; juros, multa e
rotativo; IOF e compras em moeda estrangeira; recorrência de lançamentos (assinaturas, salário) na
previsão; antecipação de parcelas; notificação de vencimento por e-mail ou push; conciliação
automática do total calculado com o total da fatura importada do banco.

**Priorização (MoSCoW):** Must = INV-1, 2, 3, 4, 6, 7, 8, 9, 12 (38 pts), o mínimo para ver a
fatura, pagá-la sem duplicar gasto e prever o caixa, provado ponta a ponta nos dois escopos.
Should = INV-5, 10, 11 (11 pts): parcelamento é o que mais muda a previsão no uso real e o Should
mais valioso; limite e o pagamento reconhecido na importação vêm em seguida. Could = INV-13 (3 pts).
Total: 52 pts. Sugestão de ordem: INV-1 → 3 (domínio puro, em paralelo com a 2) → 2 → 4 → 6 → 7 →
8 → 9 → 12, e os Should depois.

**Definition of Ready (por história):** decisões da INV-1 aceitas; exemplos de ciclo (fechamento
antes e depois do vencimento, compra no dia do fechamento, parcelas atravessando o ano) escritos
como casos de teste antes da INV-3; contrato da API descrito na história de backend antes da de
frontend.

**Definition of Done do épico:**
- CI verde no PR: `build & test` dos serviços afetados e `Cypress E2E` (os 5 checks exigidos no
  `main`).
- Testes unitários da regra do ciclo e das parcelas (INV-3), da situação da fatura e da previsão.
- Testes de integração: isolamento por dono (404 em cartão, conta e pagamento alheios), 401/403 por
  escopo, evento v1 e v2 lado a lado, backfill idempotente, pagamento fora do Resumo e dos
  orçamentos.
- E2E das jornadas PF e PJ (INV-12) no job do CI; smoke test (`build/smoke-test.mjs`) cobrindo
  compra no cartão, fatura, pagamento e previsão pelo gateway.
- Migrações EF no `expense_transactions` e no `expense_reporting`; métricas da projeção
  (`reporting.projection.lag`) acompanhadas durante o backfill.
- `docs/architecture.md`, a ADR da INV-1 e o histórico do `docs/PROMPT.md` atualizados; validado ao
  vivo no `docker compose`.

---

## 🟢 EPIC CTB — Exportação contábil PJ  · *Done*
**Problema:** todo mês, o PJ manda ao contador o movimento da empresa: o que entrou, o que saiu, em
que categoria e por qual conta. Hoje o produto só mostra isso na tela (Lançamentos e Resumo), sem
um arquivo para enviar. O usuário acaba copiando valores à mão ou reexportando o extrato do banco,
que não tem as categorias nem separa o que é da empresa do que é pessoal. O contador recebe
informação incompleta e pergunta de novo, e o fechamento do mês atrasa.
**Objetivo (outcome):** em poucos cliques, o usuário baixa o movimento PJ de um mês num arquivo que
o contador abre direto no Excel ou importa no sistema dele: cada lançamento com data, descrição,
categoria, valor com sinal, meio, conta e CNPJ, mais os pagamentos de fatura do mês e um resumo por
categoria. Só entra o escopo PJ; o que é pessoal nunca vai para o contador.
**Métricas de sucesso:** (1) o total de saídas e de entradas do arquivo de um mês bate com o Resumo
PJ do mesmo mês; (2) o arquivo abre no Excel pt-BR sem configurar nada (acentos, datas e valores
certos); (3) nenhum lançamento de conta PF aparece no arquivo; (4) a jornada "lançar, exportar,
conferir" passa no E2E do CI.
**Rastreabilidade:** `docs/PROMPT.md` → Contexto ("como sou PJ, tenho conta e gastos da minha
empresa e gastos pessoais") e Próximos passos ("exportação contábil PJ"). Ideia promovida de
"Ideias de produto" a pedido do dono do produto (2026-11-22).

**Achados no código que moldam o épico:**
- **O `transactions` tem o lançamento completo**: descrição, categoria, valor, data, meio, cartão,
  parcelas, observações e a origem (chave externa e lote, quando importado). A listagem por conta e
  período já existe (`ITransactionRepository.ListAsync`).
- **O lançamento não guarda o escopo.** O PF/PJ é da **conta** (`Account.Scope` no accounts); o
  `transactions` o busca no accounts na hora de publicar o evento. Para filtrar "só PJ", é preciso
  saber quais contas do usuário são `Business`.
- **A projeção do `reporting` não serve de fonte completa.** Ela tem o escopo, mas os lançamentos
  fora do cartão registrados antes do evento v2 (INV-2) ficaram **sem descrição** (o backfill só
  completou as compras no cartão). Também não tem observações nem a origem, e atrasa alguns
  segundos em relação ao que acabou de ser lançado ou importado.
- **A conta PJ tem CNPJ** (`Account.TaxId`, obrigatório e validado quando o escopo é `Business`) e
  nome. O contador identifica a empresa pelo CNPJ.
- **Categoria é texto livre**, sem plano de contas. O contador trabalha com códigos de conta do
  plano dele (ex.: "3.1.02 — Despesas com software"). Hoje não há onde guardar essa
  correspondência.
- **Pagamento de fatura não é lançamento** (INV-6): fica em `card_payments`, também no
  `transactions`. Para o contador, ele explica a saída do banco no dia do pagamento.
- **Exclusão é física e não há edição.** Um mês já enviado ao contador pode mudar depois (lançamento
  excluído, importação desfeita), e nada avisa.
- **Não há geração de arquivo no backend** hoje (só leitura de CSV/OFX na importação), nem
  armazenamento de arquivos (anexo, nota fiscal). Bibliotecas de planilha e PDF têm licença a
  checar: o projeto evita libs que viraram pagas para uso comercial (FluentAssertions, MediatR).

**Decisões (aceitas na [ADR-0006](adr/0006-exportacao-contabil-pj-no-transactions.md), CTB-1; ficou a recomendação de cada uma, com os refinamentos da CTB-1):**
1. **Onde o arquivo é gerado.** Opções: (a) no `transactions`, que tem o lançamento completo e é a
   fonte da verdade; (b) no `reporting`, a partir da projeção, como o Resumo. *Recomendação: (a)*.
   O contador precisa de descrição e observações de todos os lançamentos, e do que acabou de ser
   importado, sem esperar a projeção. Custa o `transactions` buscar no accounts as contas do usuário
   (nome, escopo, CNPJ), com o token dele, como já faz para validar a conta.
2. **Formato.** Opções: (a) CSV no padrão do Excel pt-BR: `;` como separador, vírgula decimal, data
   `dd/MM/aaaa`, UTF-8 com BOM (sem o BOM, o Excel erra os acentos); (b) planilha XLSX (exige
   biblioteca; ClosedXML é MIT); (c) PDF (exige biblioteca; QuestPDF tem licença comunitária com
   limite de receita). *Recomendação: (a)*, sem dependência nova; é o que os sistemas contábeis
   importam e o que o Excel abre direto. (b) fica para depois, se o contador pedir abas; (c) fica
   fora (é para ler, não para lançar).
3. **O que vai no arquivo.** *Recomendação:* um **pacote ZIP** do mês (`System.IO.Compression`,
   nativo) com três CSVs: **lançamentos** (data, descrição, categoria, conta contábil quando
   houver, tipo, valor com sinal — saída negativa —, meio, cartão, parcelas, conta, CNPJ,
   observações, origem "digitado"/"importado"); **pagamentos de fatura** do mês (data, cartão,
   fatura paga, conta de origem, valor); **resumo** por categoria (entradas, saídas, saldo) com o
   total. Um só arquivo para anexar no e-mail, e cada CSV abre sozinho.
4. **Período e base de datas.** *Recomendação:* um **mês** (padrão: o anterior, que é o que se manda
   ao contador), com intervalo livre de datas como opção. Lançamentos entram pela **data do
   lançamento** (competência), como o Resumo; a compra parcelada entra **inteira** na data da compra
   (a fatura é visão de caixa, não contábil). Pagamentos de fatura entram pela data do pagamento.
5. **Escopo.** *Recomendação:* só contas **PJ** (`Business`) ativas e inativas do usuário — todas por
   padrão, ou uma conta escolhida (empresas com mais de uma conta ou mais de um CNPJ). Conta PF nunca
   entra, nem por filtro. Sem conta PJ, o pedido responde 400 com código estável.
6. **Plano de contas.** Opções: (a) fora do escopo; (b) o usuário associa cada categoria PJ a um
   código e nome de conta do plano do contador, e o código sai numa coluna; (c) importar o plano
   inteiro do contador. *Recomendação: (b)* como Should, guardado no `transactions` (as categorias
   são dele) e por usuário; categoria sem código sai com a coluna vazia, sem bloquear a exportação.
   (c) é Won't: cada sistema contábil tem um layout.
7. **Mês já enviado que muda depois.** *Recomendação:* guardar um **registro de cada exportação**
   (mês, contas, quantidade de linhas, totais e uma impressão digital do conteúdo, nunca o arquivo) e,
   ao abrir a tela, avisar quando o mesmo mês exportado antes mudou. Could: o produto não bloqueia
   exclusão de mês fechado (não há "fechamento contábil").
8. **Rota, escopo OAuth e limites.** *Recomendação:* `GET /transactions/accounting-export?month=…`
   (ou `from`/`to`, no máximo 12 meses) com `expenses.read`, devolvendo o ZIP como download
   (`Content-Disposition` com nome `contabil-<CNPJ>-<aaaa-mm>.zip`). Pela rota existente do gateway;
   nenhum escopo novo. O conteúdo do arquivo nunca vai para log ou trace (mesma regra da importação,
   ADR-0004, decisão 8).

- ✅ **CTB-1** ADR: exportação contábil PJ. → [ADR-0006](adr/0006-exportacao-contabil-pj-no-transactions.md), Aceita.
  - *Dado* as decisões em aberto 1 a 8 deste épico, *quando* o dono do produto revisa as opções,
    *então* a ADR-0006 é registrada como **Aceita** em `docs/adr/` (com o índice atualizado) e o PO
    ajusta as histórias seguintes ao que foi decidido.
  - Refinamentos registrados na ADR além das recomendações: só **mês** nesta versão (intervalo livre
    fica fora); valor sem separador de milhar (`-1234,56`), CRLF e cabeçalho; texto que começa com
    `=`, `+`, `-`, `@` ganha `'` na frente (CSV injection); coluna de **moeda** e resumo somado por
    moeda; pagamento de fatura entra quando o cartão **ou** a conta de origem é PJ; códigos
    `accounting_export.month_invalid`, `accounting_export.no_business_account`,
    `accounting_export.account_not_business` (conta de outro usuário = 404 `account.not_found`);
    nome do ZIP sem CNPJ quando há mais de um; registro da CTB-6 com hash SHA-256, nunca o arquivo.
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **CTB-2** Lançamentos PJ do mês em CSV. → `GET /transactions/accounting-export`, `lancamentos.csv` num ZIP.
  - *Dado* lançamentos em contas PJ e PF no mês, *quando* peço a exportação do mês, *então* recebo um
    CSV só com os lançamentos das contas PJ, um por linha, com data, descrição, categoria, tipo,
    valor com sinal, moeda, meio, cartão, parcelas, conta, CNPJ, observações e origem.
  - *Dado* o arquivo, *quando* o abro no Excel pt-BR, *então* acentos, datas (`dd/MM/aaaa`) e valores
    (`-1234,56` → número) aparecem certos, sem configurar a importação.
  - *Dado* uma descrição importada que começa com `=`, `+`, `-` ou `@`, *quando* abro o CSV no Excel,
    *então* ela aparece como texto, nunca como fórmula.
  - *Dado* uma compra em 3x no mês, *quando* exporto, *então* ela aparece uma vez, com o valor inteiro
    e "3" na coluna de parcelas.
  - *Dado* uma descrição com `;`, aspas ou quebra de linha, *quando* exporto, *então* o CSV continua
    com uma linha por lançamento (campo entre aspas, aspas dobradas).
  - *Dado* um usuário sem conta PJ, ou um mês inválido, *quando* peço a exportação, *então* recebo 400
    com código estável; sem token, 401; a conta de outro usuário, 404.
  - Feito assim: `GET /transactions/accounting-export?month=aaaa-mm[&accountId=]` (`expenses.read`)
    devolve `application/zip` com o `lancamentos.csv` (a CTB-3 junta os outros dois). Contas e
    cartões vêm do accounts com o token (`GET /accounts` e `GET /credit-cards`, inativos incluídos);
    os lançamentos, de cada conta PJ, pela data, em ordem de data e de criação. Colunas: Data,
    Descrição, Categoria, Conta contábil (vazia até a CTB-5), Tipo (Receita/Despesa), Valor (saída
    negativa), Moeda, Meio (em português), Cartão ("apelido final 1234"), Parcelas, Conta, CNPJ
    (pontuado), Observações, Origem (digitado/importado). Formatação a mão (`AccountingCsv`), porque o
    serviço roda com InvariantGlobalization. Erros: `accounting_export.month_invalid`,
    `accounting_export.no_business_account`, `accounting_export.account_not_business` (400) e
    `account.not_found` (404). O log registra só mês e quantidades. Smoke passo 18 (baixa o ZIP pelo
    gateway, abre o CSV e confere; conta PF = 400). Smoke com 37 passos.
  - Estimativa: 5 · **Must** · Dependências: CTB-1
- ✅ **CTB-3** Pacote do mês: pagamentos de fatura e resumo por categoria. → `pagamentos-fatura.csv` e `resumo.csv` no ZIP.
  - *Dado* pagamentos de fatura no mês com cartão ou conta de origem PJ, *quando* exporto, *então* o ZIP traz,
    além dos lançamentos, um CSV de pagamentos de fatura (data, cartão, fatura paga, conta de origem,
    valor) e um CSV de resumo por moeda e categoria (entradas, saídas, saldo, e a linha de total).
  - *Dado* o mês exportado, *quando* comparo o resumo com o Resumo PJ da SPA no mesmo mês, *então* os
    totais de entradas e saídas batem.
  - Feito assim: o ZIP traz `lancamentos.csv`, `pagamentos-fatura.csv` e `resumo.csv`, nessa ordem.
    Pagamentos pela data do pagamento (`ICardPaymentRepository.ListPaidBetweenAsync`); entra o que
    tem o cartão ou a conta de origem entre as contas exportadas (com `accountId`, só aquela). Colunas:
    Data, Cartão, Fatura (`mm/aaaa` do vencimento), Conta de origem, CNPJ da conta de origem (vazio se
    PF), Valor e Moeda. Resumo por moeda e categoria (ordem alfabética), com Entradas, Saídas
    (positivas), Saldo e uma linha "Total" por moeda; a compra parcelada conta inteira. Smoke passo 18
    lê os três arquivos do ZIP e confere que o resumo bate com `GET /reports/summary` da conta PJ.
    Smoke com 38 passos.
  - Estimativa: 3 · **Must** · Dependências: CTB-2
- ✅ **CTB-4** "Exportar para o contador" na SPA. → aba "Contador" e `GET /transactions/accounting-export/preview`.
  - *Dado* o usuário logado com conta PJ, *quando* abre a exportação, *então* escolhe o mês (padrão: o
    anterior) e, se tiver mais de uma conta PJ, todas ou uma; vê quantos lançamentos e os totais
    antes de baixar; e baixa o ZIP com um clique.
  - *Dado* um usuário sem conta PJ, *quando* abre a exportação, *então* vê o aviso de que ela é só
    para contas PJ, sem botão de baixar.
  - Feito assim: a prévia é a rota irmã `GET /transactions/accounting-export/preview?month=&accountId=`
    (`expenses.read`), que monta o mesmo pacote do ZIP sem gerar o arquivo e devolve `fileName`,
    `accounts`, `lines`, `payments` e `totals` por moeda (entradas, saídas e saldo, os mesmos da
    linha "Total" do `resumo.csv`); mesmos erros da exportação. Na SPA, a aba "Contador"
    (`AccountingExportView`) tem o mês (anterior por padrão), a conta (só com mais de uma PJ; as
    inativas aparecem marcadas), a prévia e o botão "Baixar <arquivo>". O download usa o novo
    `apiDownload` (mesmo token e renovação do `apiFetch`) e o nome da prévia, sem depender do
    `Content-Disposition` pelo CORS. Smoke passo 18 confere a prévia (39 passos). Cypress
    `accounting-export.cy.ts`: mês anterior por padrão, mês sem movimento e o ZIP baixado começa com
    `PK` (o demo sempre tem conta PJ; o aviso sem PJ não tem E2E). Suíte com 14 testes.
  - Estimativa: 3 · **Must** · Dependências: CTB-3
- ✅ **CTB-5** Plano de contas: categoria → conta contábil. → `/transactions/ledger-accounts`, seção "Plano de contas" na aba Contador.
  - *Dado* as categorias usadas em contas PJ, *quando* associo uma delas a um código e nome de conta
    do plano do contador, *então* a coluna "conta contábil" do CSV de lançamentos sai preenchida para
    ela, e a do resumo também.
  - *Dado* uma categoria sem associação, *quando* exporto, *então* a coluna sai vazia e a tela mostra
    quantos lançamentos ficaram sem conta, sem impedir a exportação.
  - Feito assim: tabela `ledger_accounts` no `transactions` (dono, categoria, código até 20, nome
    opcional até 100; único por dono e categoria). A categoria é comparada exatamente como foi
    digitada ("Software" ≠ "software", como no Resumo): coluna com collation `utf8mb4_bin`, aplicada
    por SQL explícito na migração, porque o provider da Oracle ignora a collation no CREATE TABLE (o
    smoke pegou o índice único juntando as duas). Rotas: `GET /transactions/ledger-accounts` (categorias
    das contas PJ com a quantidade de lançamentos, mais as já associadas sem uso), `PUT` com
    `{category, code, name}` (associa ou troca, `expenses.write`) e `DELETE ?category=` (a categoria vai
    na query por ser texto livre). Erros `ledger_account.*`. O código sai na coluna "Conta contábil"
    do `lancamentos.csv` e do `resumo.csv`; a prévia ganhou `unmappedLines`. SPA: seção "Plano de
    contas" na aba Contador (`LedgerAccountsSection`), com código e nome por categoria, e o aviso "N
    lançamento(s) sem conta contábil" na prévia. Smoke passo 19 (40 passos); a jornada Cypress da
    CTB-7 associa a categoria e confere o código no ZIP.
  - Estimativa: 5 · **Should** · Dependências: CTB-2
- ✅ **CTB-6** Aviso de mês alterado depois da exportação. → tabela `accounting_exports`, `lastExport` na prévia.
  - *Dado* um mês já exportado, *quando* um lançamento dele é excluído ou importado depois, *então* a
    tela de exportação mostra "mudou desde a exportação de dd/mm", com a diferença de linhas e de
    totais. O registro guarda só totais e a impressão digital, nunca o conteúdo.
  - Feito assim: cada download grava (ou substitui) um registro por usuário, mês e conta
    (`accounting_exports`; "todas as contas PJ" = `Guid.Empty`, porque o índice único do MySQL não
    compara nulos) com quantidade de lançamentos e pagamentos, totais por moeda (JSON) e o SHA-256 das
    três planilhas; nunca o conteúdo. Dois downloads simultâneos do mesmo mês não falham (o
    conflito de chave única é ignorado). A prévia ganhou `lastExport` (`exportedAtUtc`, `lines`,
    `payments`, `totals` e `changed` = a impressão digital de agora difere). Qualquer mudança no que
    sairia no arquivo avisa: lançamento novo, excluído ou importado, pagamento de fatura, código
    contábil trocado. A prévia é só leitura; o registro sai no download (GET), como um log de envio.
    SPA: aviso "Mudou desde a exportação de dd/mm/aaaa hh:mm: eram N lançamento(s)…" (borda âmbar) ou
    "Exportado em …, sem mudanças". Smoke passo 18 confere `changed=false` logo depois do download;
    a jornada Cypress da CTB-7 lança um gasto depois do download e confere o aviso.
  - Estimativa: 3 · **Could** · Dependências: CTB-4
- ✅ **CTB-7** E2E Cypress: lançar, exportar e conferir (PJ e PF). → `accounting-export-journey.cy.ts`.
  - *Dado* uma conta PJ e uma PF com lançamentos no mês, *quando* exporto pela SPA, *então* o ZIP
    baixado tem só os lançamentos PJ, o pagamento de fatura PJ e o resumo com os totais da tela.
  - Feito assim: tudo pela SPA, no mês de hoje: conta PJ com CNPJ próprio do teste (14 dígitos do
    carimbo de tempo), conta PF e cartão PJ; gasto Pix e compra em 3x na PJ, gasto na PF; pagamento
    parcial da fatura atual do cartão PJ pela conta PJ (reabre a fatura até a projeção trazer a
    compra). Na aba Contador, escolhe a conta PJ, confere a prévia (2 lançamentos, 1 pagamento,
    saídas R$ 1.020,50), baixa o ZIP e o abre com a task `unzip` do `cypress.config.ts` (`zlib`
    nativo, o mesmo leitor do smoke): `lancamentos.csv` só com os dois PJ (a compra em 3x uma vez,
    inteira, com "3"), sem o gasto PF; `pagamentos-fatura.csv` com o pagamento e a fatura paga;
    `resumo.csv` com os totais da prévia. O formulário de lançamento só registra gastos, então a
    jornada não tem receita (coberta pelos testes de integração). Suíte com 15 testes.
  - Estimativa: 3 · **Must** · Dependências: CTB-4

**Fora do escopo (Won't, nesta versão):** exportação PF (para imposto de renda); XLSX e PDF;
layouts específicos de sistemas contábeis (Domínio, Alterdata etc.) e importação do plano de contas
inteiro; envio por e-mail ou acesso do contador ao produto (exigiria um papel novo no identity);
anexos e notas fiscais; "fechamento contábil" que bloqueie excluir lançamentos de mês exportado;
classificação de despesa pessoal paga pela conta PJ (pró-labore, distribuição de lucros).

**Priorização (MoSCoW):** Must = CTB-1, 2, 3, 4, 7 (16 pts), o mínimo para o contador receber o mês
PJ completo e conferível, provado ponta a ponta. Should = CTB-5 (5 pts), o que poupa o contador de
reclassificar. Could = CTB-6 (3 pts). Total: 24 pts.

**Ordem sugerida:** CTB-1 → CTB-2 → CTB-3 → CTB-4 → CTB-7 (fecha o Must) → CTB-5 → CTB-6.

## 🟢 EPIC LNC — Receita e edição de lançamentos  · *Done*
**Problema:** o lançamento é o dado mais usado do produto, mas só nasce e morre. O formulário da SPA
registra apenas gastos: salário, pró-labore e recebimento de cliente só entram por importação de
extrato ou direto pela API. E não há edição: errar a categoria, o valor, a data ou o número de
parcelas obriga a excluir e lançar de novo, o que perde a origem de um lançamento importado e
confunde quem já exportou o mês para o contador. Na lista, receita e gasto aparecem iguais, e as
observações não aparecem nem podem ser digitadas.
**Objetivo (outcome):** o usuário lança receita e gasto pelo mesmo formulário, com observações, vê na
lista o que é entrada e o que é saída, e corrige qualquer campo de um lançamento (inclusive
importado) sem excluí-lo. Resumo, orçamentos, fatura, previsão e exportação contábil acompanham a
correção.
**Métricas de sucesso:** (1) nenhuma receita precisa de importação ou API para entrar; (2) corrigir
um lançamento não muda o id nem a origem dele (reimportar o extrato continua achando a duplicata);
(3) depois de editar, Resumo, orçamento e fatura refletem o novo valor no mesmo tempo da projeção
que o lançamento novo leva; (4) a jornada "lançar receita, editar gasto, conferir" passa no E2E do CI.
**Rastreabilidade:** `docs/PROMPT.md` → "registrar gastos e receitas pessoais e da empresa"; lacunas
achadas nos épicos INV e CTB (o Cypress da previsão e o da exportação não conseguiram lançar receita
pela tela). Promovido a pedido do dono do produto (2026-11-22).

**Achados no código que moldam o épico:**
- **A API já aceita receita** (`type: "Income"` em `POST /transactions`); só a SPA fixa
  `type: 'Expense'` no envio. Receita no cartão é estorno e abate a fatura (ADR-0005, decisão 7).
  O domínio aceita receita parcelada no cartão, o que não tem sentido para estorno.
- **As observações existem** (`notes`, até 500 caracteres) na API e saem na exportação contábil,
  mas o formulário e a lista da SPA não têm o campo.
- **A lista não mostra o tipo**: receita e gasto aparecem com o mesmo valor positivo, sem cor nem
  sinal.
- **Não há edição.** `Transaction` tem `UpdatedAtUtc`, mas nenhum método muda o lançamento e não
  existe `PUT`/`PATCH`. Também não há token de concorrência (só o lote de importação tem).
- **O evento é de criação.** `TransactionRegistered` (v2) leva o estado completo e o escopo da conta;
  na projeção do `reporting`, aplicá-lo já é um *upsert* por id. Exclusão é o `TransactionDeleted`.
  Fatura, previsão, Resumo e orçamentos leem a projeção.
- **O escopo PF/PJ vem da conta** e é copiado no evento. A chave externa da importação é única por
  dono **e conta**, e o lote de importação aponta para os lançamentos que criou (desfazer apaga).
- **A sugestão de categoria da importação** (IMP-6) usa a categoria do lançamento **criado** por
  último com a mesma descrição (`CreatedAtUtc`), então recategorizar não ensina a importação.
- **A exportação contábil** lê o `transactions` direto e já avisa mês alterado (CTB-6): editar um
  lançamento de um mês exportado faz o aviso aparecer sem trabalho extra.

**Decisões (aceitas na [ADR-0007](adr/0007-edicao-de-lancamentos-e-receita-na-spa.md), LNC-1; ficou a recomendação de cada uma, com os refinamentos da LNC-1):**
1. **Contrato do evento de edição.** Opções: (a) evento novo `TransactionUpdated` (v1) com o estado
   completo depois da edição, aplicado na projeção **só em linha que já existe** (como o
   `TransactionCardDetailsResolved`); (b) reemitir `TransactionRegistered`, que a projeção já aplica
   como *upsert*. *Recomendação: (a).* "Registrado" e "alterado" são fatos diferentes para quem lê o
   feed, e com (b) uma edição que chegue depois da exclusão recriaria a linha na projeção.
2. **O que pode mudar.** *Recomendação:* tipo, meio, cartão (da mesma conta), parcelas, descrição,
   categoria, valor, moeda, data e observações, com as mesmas regras da criação. **A conta não
   muda** (400 `transaction.account_immutable`): trocar de conta pode trocar o escopo PF/PJ, e a
   chave da importação é por conta; quem errou a conta exclui e lança de novo. Por isso o evento não
   precisa levar o escopo.
3. **Lançamento importado.** *Recomendação:* editável como os outros; a chave externa e o lote são
   preservados. Reimportar o mesmo extrato continua acusando duplicata, e desfazer a importação
   apaga o lançamento mesmo editado (a tela avisa ao editar um importado).
4. **Concorrência.** Opções: (a) a última gravação vence; (b) concorrência otimista: o lançamento
   ganha `version` (inteiro), a edição manda a versão que leu e recebe 409
   `transaction.concurrency_conflict` se ela mudou; a SPA recarrega e mostra o valor atual.
   *Recomendação: (b)*, com o mesmo padrão do lote de importação. Duas abas, ou celular e
   computador, editando o mesmo lançamento não devem sobrescrever uma à outra em silêncio.
5. **Forma da rota.** Opções: (a) `PUT /transactions/{id}` com o lançamento inteiro (mesmo corpo da
   criação, sem a conta, mais `version`); (b) `PATCH` com só o que mudou. *Recomendação: (a).* Reusa
   a validação da criação e o formulário da SPA; `PATCH` com regras cruzadas (meio × cartão ×
   parcelas) fica ambíguo. `expenses.write`, pela rota existente do gateway.
6. **Receita na SPA.** *Recomendação:* o formulário ganha o tipo (Gasto/Receita, padrão Gasto). A
   lista mostra o tipo pelo sinal e pela cor do valor (+ verde, − vermelho) e as observações abaixo
   da descrição. Receita **não** é parcelada: o domínio passa a recusar `installments > 1` em
   receita (400 `transaction.installments_not_allowed`, o mesmo código de hoje para meio que não é
   cartão). Receita no cartão continua sendo estorno.
7. **Sugestão de categoria.** *Recomendação:* a sugestão da importação passa a considerar a última
   **alteração** (`UpdatedAtUtc`), não só a criação: recategorizar um lançamento ensina a próxima
   importação.
8. **Histórico e lote.** *Recomendação:* sem trilha de auditoria por lançamento nesta versão (o feed
   de eventos guarda a sequência de estados para quem precisar). Recategorizar vários lançamentos
   de uma vez (útil depois de importar) fica como **Could**.

- ✅ **LNC-1** ADR: edição de lançamentos e receita na SPA. → [ADR-0007](adr/0007-edicao-de-lancamentos-e-receita-na-spa.md), Aceita.
  - *Dado* as decisões em aberto 1 a 8 deste épico, *quando* o dono do produto revisa as opções,
    *então* a ADR-0007 é registrada como **Aceita** em `docs/adr/` (com o índice atualizado) e o PO
    ajusta as histórias seguintes ao que foi decidido.
  - Refinamentos registrados na ADR além das recomendações: payload do `TransactionUpdated` (estado
    completo mais `version`, sem escopo); `version` começa em 1 e os lançamentos existentes recebem 1
    na migração, é token de concorrência do EF e vem em toda leitura; edição sem mudança responde 200
    sem subir a versão e sem evento; `PUT` responde 200 com o lançamento atualizado; a sugestão de
    categoria usa `UpdatedAtUtc` (a criação também o preenche).
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **LNC-2** Receita e observações no formulário e na lista. → tipo e observações no formulário; sinal, cor e observações na lista.
  - *Dado* o formulário de lançamento, *quando* escolho "Receita", preencho e envio, *então* o
    lançamento entra como receita e aparece na lista com sinal + e em verde (gasto: − e vermelho).
  - *Dado* um lançamento com observações, *quando* olho a lista, *então* elas aparecem abaixo da
    descrição; *e* o formulário tem o campo (até 500 caracteres).
  - *Dado* uma receita no cartão, *quando* escolho 2 parcelas ou mais, *então* a API responde 400
    `transaction.installments_not_allowed` (e o formulário nem oferece parcelas para receita).
  - Feito assim: o domínio recusa `installments > 1` em receita com
    `transaction.installments_not_allowed` (o mesmo código do meio que não é cartão); receita no cartão
    à vista segue como estorno. Na SPA, o formulário ganhou o tipo (`tx-type`, Gasto/Receita, padrão
    Gasto) e as observações (`tx-notes`, até 500, limpas depois de registrar); as parcelas só
    aparecem para gasto no cartão de crédito. A lista mostra o valor com + e verde (receita) ou − e
    vermelho (gasto) e as observações abaixo da descrição. Smoke passo 20 (receita com observação;
    receita em 3x = 400; 41 passos). Cypress `transaction-income.cy.ts` (16 testes na suíte).
  - Estimativa: 3 · **Must** · Dependências: LNC-1
- ✅ **LNC-3** Editar lançamento na API. → `PUT /transactions/{id}`, `version`, `TransactionUpdated`.
  - *Dado* um lançamento meu, *quando* envio `PUT /transactions/{id}` com os campos e a `version`
    que li, *então* ele muda com as regras da criação, a `version` sobe, o id e a origem
    (chave externa e lote) ficam, e um `TransactionUpdated` sai no outbox no mesmo commit.
  - *Dado* uma edição que não muda nenhum campo, *quando* envio, *então* recebo 200 sem a `version`
    subir e sem evento.
  - *Dado* uma `version` antiga, *quando* edito, *então* recebo 409
    `transaction.concurrency_conflict` e nada muda.
  - *Dado* uma troca de conta, *quando* edito, *então* recebo 400 `transaction.account_immutable`;
    lançamento de outro usuário, 404; sem `expenses.write`, 403.
  - *Dado* que recategorizei um lançamento, *quando* importo um extrato com a mesma descrição,
    *então* a sugestão é a categoria nova.
  - Feito assim: `Transaction.Edit` valida com as mesmas regras da criação (monta um lançamento
    temporário com `Create`) e só copia os campos quando algo muda; aí sobe a `version` e anda o
    `UpdatedAtUtc`. Coluna `version` (migração `TransactionVersion`, padrão 1 nos existentes) é token
    de concorrência do EF; o handler compara a versão enviada e, na corrida entre leitura e gravação,
    converte o conflito do EF no mesmo 409 `transaction.concurrency_conflict`. `PUT` exige `version`
    (400 `transaction.version_required`); conta diferente = 400 `transaction.account_immutable`;
    cartão novo é conferido no accounts (404 `card.not_found`). O DTO ganhou `version` e `imported`
    (para o aviso da LNC-5). Evento `TransactionUpdated` v1 com o estado completo; a versão do
    lançamento vai em **`transactionVersion`** (e não `version`, que no envelope do feed é a versão do
    formato do evento; o primeiro rascunho colidia com ela e o teste do feed pegou). O reporting
    ainda ignora o tipo novo (LNC-4). Sugestão de categoria da importação pelo `UpdatedAtUtc`. Smoke
    passo 21 no MySQL real (edita, versão antiga = 409, outra conta = 400; 42 passos).
  - Estimativa: 5 · **Must** · Dependências: LNC-1
- ✅ **LNC-4** Projeção aplica a edição. → `TransactionUpdatedV1` no reporting.
  - *Dado* um `TransactionUpdated`, *quando* o `reporting` o lê, *então* a linha da projeção fica
    com o estado novo (tipo, meio, cartão, parcelas, categoria, valor, data, descrição) e Resumo,
    orçamentos, fatura e previsão refletem a mudança; *dado* que o lançamento já foi excluído,
    *então* o evento é ignorado (não recria a linha).
  - *Dado* uma compra em 3x editada para 6x, *quando* abro a fatura, *então* as parcelas se
    redistribuem nas faturas certas.
  - Feito assim: o reporting lê o `TransactionUpdated` v1 (`TransactionUpdatedV1`, versão mais nova
    suportada 1; v2 desconhecida para o lote, como os outros tipos) e corrige tipo, meio, cartão,
    parcelas, categoria, valor, moeda, data e descrição da linha da projeção, guardando a sequência.
    Só aplica em linha existente (edição depois da exclusão é ignorada) e mantém o escopo da linha.
    Resumo, orçamentos, fatura, limite e previsão não mudaram: leem a projeção. Uma reconstrução do
    zero chega ao último estado. **Ordem de implantação:** com o `transactions` da LNC-3 e um
    `reporting` anterior a esta história, o `TransactionUpdated` é pulado como tipo desconhecido e o
    checkpoint avança; para recuperar edições feitas nessa janela, zerar o checkpoint (reconstrução,
    EVT-2). Smoke passo 21 confere o Resumo da conta depois da edição (43 passos).
  - Estimativa: 3 · **Must** · Dependências: LNC-3
- ✅ **LNC-5** Editar na SPA. → "editar" na lista, formulário em modo edição, conflito tratado.
  - *Dado* a lista de lançamentos, *quando* clico em "editar", *então* o formulário abre preenchido
    (sem trocar a conta), salvo e a lista mostra o lançamento alterado; "cancelar" volta sem mudar.
  - *Dado* um lançamento importado, *quando* abro a edição, *então* vejo o aviso de que desfazer a
    importação ainda o apaga.
  - *Dado* que o lançamento mudou em outra aba, *quando* salvo, *então* vejo "Este lançamento mudou
    enquanto você editava" com os valores atuais, sem sobrescrever.
  - Feito assim: o mesmo formulário da criação entra em modo edição (`tx-editing` com "Editando:
    <descrição>", conta travada, botão "Salvar alteração" e "Cancelar"); o cartão do lançamento
    aparece mesmo se foi desativado depois. O envio manda a `version` lida (`transactionsApi.update`).
    No 409 `transaction.concurrency_conflict`, a tela relê o lançamento (`GET /transactions/{id}`),
    recarrega o formulário com o que está gravado e mostra o aviso `tx-conflict`, sem sobrescrever.
    Importado mostra `tx-imported-notice`. Excluir o lançamento em edição cancela a edição. Cypress
    `transaction-edit.cy.ts` (edita valor, categoria e observações; cancela; simula a outra aba com
    um `PUT` direto no gateway e confere o 409 tratado); `import.cy.ts` confere o aviso do
    importado. Config do Cypress ganhou `gatewayUrl`. Suíte com 17 testes.
  - Estimativa: 3 · **Must** · Dependências: LNC-3
- ✅ **LNC-6** E2E Cypress: receita, edição e reflexo nos relatórios. → `transaction-edit-journey.cy.ts`.
  - *Dado* uma conta com orçamento e um cartão, *quando* lanço uma receita e edito um gasto (valor,
    categoria e parcelas) pela SPA, *então* Resumo, orçamento e fatura mostram os valores novos.
  - Feito assim: tudo pela SPA, no mês de hoje: conta PJ com cartão (fecha 10, vence 17) e dois
    orçamentos PJ em categorias únicas ("Antes" e "Depois"); receita Pix de R$ 2.500 e compra de
    R$ 300 em 3x na categoria "Antes" (o orçamento "Antes" chega a R$ 300). A compra é corrigida para
    R$ 600 em 6x na categoria "Depois": o orçamento "Depois" chega a R$ 600 e o "Antes" volta a zero;
    no Resumo só aparece "Depois" com R$ 600 e as receitas sobem R$ 2.500 (por diferença); a fatura
    atual tem a parcela "1/6" de R$ 100, e cinco faturas adiante está a "6/6", a última. Suíte com
    18 testes, sem retry.
  - Estimativa: 3 · **Must** · Dependências: LNC-2, LNC-4, LNC-5
- ✅ **LNC-7** Recategorizar em lote. → `POST /transactions/recategorize`, seleção na lista.
  - *Dado* vários lançamentos selecionados na lista, *quando* escolho uma categoria e confirmo,
    *então* todos mudam de categoria de uma vez (um `TransactionUpdated` por lançamento), e os que
    mudaram em outra aba são informados, sem sobrescrever.
  - Feito assim: `POST /transactions/recategorize` com `{ category, items: [{ id, version }] }`
    (`expenses.write`, pela rota existente do gateway; até 500 por vez). Cada lançamento segue a
    concorrência da LNC-3 (`Transaction.Recategorize` reusa o `Edit`): na versão lida, muda e gera um
    `TransactionUpdated`; já na categoria, fica sem versão nem evento; versão antiga volta em
    `conflicts` com o estado gravado; id inexistente ou de outro usuário em `notFound` (sem distinguir);
    dado antigo que a regra de hoje recusa em `rejected`. Tudo num commit só; se outra gravação passar
    entre a leitura e o commit, nada do lote grava (409 `transaction.concurrency_conflict`). Categoria
    inválida, lote vazio ou id repetido = 400 (`transaction.category_*`, `transaction.batch_empty`,
    `transaction.batch_duplicated`). Na SPA, a lista ganhou caixas de seleção (`tx-select`,
    `tx-select-all`) e, com algo marcado, a barra `tx-bulk` (categoria com as sugestões, "Recategorizar",
    "Limpar seleção"); o resultado (`tx-bulk-result`) lista os conflitos (`tx-bulk-conflict`), que
    continuam marcados para aplicar de novo sobre o valor atual. Smoke passo 22 (44 passos). Cypress
    `transaction-recategorize.cy.ts` (19 testes na suíte).
  - Estimativa: 3 · **Could** · Dependências: LNC-5

**Fora do escopo (Won't, nesta versão):** trocar a conta de um lançamento; histórico/auditoria de
alterações na tela; editar pagamento de fatura (continua excluir e registrar de novo); desfazer
exclusão; lançamentos recorrentes (candidato a épico próprio); edição offline.

**Priorização (MoSCoW):** Must = LNC-1 a LNC-6 (19 pts): receita pela tela e edição que os
relatórios acompanham, provada ponta a ponta. Could = LNC-7 (3 pts). Total: 22 pts.

**Ordem sugerida:** LNC-1 → LNC-2 → LNC-3 → LNC-4 → LNC-5 → LNC-6 (fecha o Must) → LNC-7.

## 🟢 EPIC REC — Lançamentos recorrentes  · *Done*
**Problema:** salário, pró-labore, aluguel, condomínio, escola, internet e assinaturas se repetem todo
mês, mas o produto só conhece o que já foi lançado. A previsão de caixa (INV-8) e o saldo projetado
(INV-13) subestimam o futuro: o mês que vem aparece sem salário e sem aluguel, e o saldo no fim do
semestre fica otimista ou pessimista conforme o que o usuário lembrou de lançar com data futura. O
único jeito de "prever" hoje é lançar cada mês à mão com data futura, o que suja Resumo, orçamentos e
exportação contábil com valores que ainda não aconteceram (e que mudam: a conta de luz nunca é igual).
**Objetivo (outcome):** o usuário cadastra uma vez o que se repete e a previsão passa a contar com isso
nos meses seguintes, sem lançar nada antes da hora. Quando a ocorrência acontece, ele a lança com um
clique (ajustando o valor real) ou a pula; Resumo, orçamentos, fatura e exportação continuam vendo só o
que foi lançado.
**Métricas de sucesso:** (1) com salário e aluguel cadastrados, a previsão dos próximos 6 meses mostra
os dois em todos os meses sem nenhum lançamento futuro; (2) lançar uma ocorrência tira ela da previsão
como recorrente na mesma hora em que o lançamento entra (nada conta duas vezes); (3) nenhuma recorrência
aparece no Resumo, nos orçamentos ou na exportação contábil antes de ser lançada; (4) a jornada
"cadastrar, prever, lançar, conferir" passa no E2E do CI.
**Rastreabilidade:** `docs/PROMPT.md` → "registrar gastos e receitas pessoais e da empresa" e previsão;
[ADR-0005](adr/0005-fatura-de-cartao-e-previsao-de-caixa.md), decisão 8 e consequências ("a previsão sem
recorrência subestima o futuro"); ideia de produto promovida a pedido do dono do produto (2026-11-22).

**Achados no código que moldam o épico:**
- **A previsão já aceita o futuro, mas só o lançado.** `CashForecastCalculator` soma lançamentos fora do
  cartão pela data e o cartão pela fatura, no mês de vencimento; `ProjectedBalanceCalculator` (INV-13)
  parte do saldo informado. Os dois leem a projeção do `reporting`, que nasce dos eventos do
  `transactions`.
- **Lançamento futuro contamina tudo o que lê a projeção ou a tabela.** Resumo, orçamentos (BDG),
  fatura e limite (INV) e a exportação contábil (CTB, que lê o `transactions` direto) contam qualquer
  lançamento que exista. Por isso a recorrência não pode virar lançamento antes da hora.
- **A importação deduplica só pela chave externa** (`ExternalId` por dono e conta, IMP-4). Um
  lançamento criado pelo sistema não tem chave: se ele fosse gerado sozinho, importar o extrato do mês
  traria o mesmo pagamento de novo como lançamento novo.
- **O `reporting` pula tipo de evento desconhecido e o checkpoint avança** (visto na LNC-4): evento novo
  exige implantar o `reporting` antes ou junto do `transactions`, senão o que sair nessa janela se perde
  até zerar o checkpoint.
- **"Hoje" só tem fuso no `reporting`** (`Reports:TimeZone`). O `transactions` não tem fuso, e as
  ocorrências "a lançar" dependem do dia de hoje.
- **Já existem workers no `transactions`** (`ImportBatchCleanupWorker`, backfills): gerar lançamentos
  sozinho seria possível tecnicamente; o impedimento é o achado da importação, não a técnica.
- **Parcelamento já existe e é outra coisa** (INV-5): uma compra em N parcelas é um lançamento só,
  distribuído nas faturas. Recorrência é um compromisso sem fim definido, um lançamento por ocorrência.

**Decisões (aceitas na [ADR-0008](adr/0008-lancamentos-recorrentes.md), REC-1; ficou a recomendação de cada uma, com os refinamentos da REC-1):**
1. **Modelo.** Opções: (a) **regra que só projeta**: a recorrência alimenta a previsão e nunca vira
   lançamento sozinha; o usuário lança cada ocorrência (um clique, com o valor real) ou a pula;
   (b) **lançamento automático**: um worker cria o lançamento no dia; (c) **lançamento previsto**: o
   lançamento nasce com status "previsto" e é "efetivado" depois. *Recomendação: (a).* (b) duplica com a
   importação de extrato e grava como real um valor que muda (luz, telefone); (c) obriga Resumo,
   orçamentos, fatura, limite, exportação e todos os eventos a filtrarem o status, o mesmo risco que a
   ADR-0005 evitou ao não fazer do pagamento de fatura um lançamento.
2. **Onde mora e como chega ao `reporting`.** Opções: (a) regra no `transactions` (dono dos
   lançamentos: lançar a ocorrência cria o lançamento no mesmo serviço), publicada no outbox com eventos
   próprios (`RecurrenceSaved` v1 com o estado completo, `RecurrenceDeleted` v1) e o estado de cada
   ocorrência (`RecurrenceOccurrenceLaunched`, `RecurrenceOccurrenceSkipped`,
   `RecurrenceOccurrenceReopened`); (b) regra no `transactions` e o `reporting` a lê por HTTP na consulta
   da previsão; (c) regra no `reporting`. *Recomendação: (a).* Segue o caminho do EVT (o `reporting` não
   chama o `transactions` na consulta) e não muda o `TransactionRegistered`; com (c), lançar a ocorrência
   teria de atravessar serviços para criar o lançamento. Implantar o `reporting` antes do
   `transactions` (achado do checkpoint).
3. **Frequência e datas.** *Recomendação:* **mensal** (dia 1 a 31; mês mais curto usa o último dia) no
   Must e **anual** (dia e mês: IPVA, anuidade, seguro) no Should. Semanal e quinzenal ficam fora. A
   regra começa numa data e termina opcionalmente noutra; sem "N ocorrências" (o fim por data cobre o
   caso).
4. **O que a regra guarda.** *Recomendação:* os mesmos campos do lançamento (conta, tipo, meio, cartão da
   conta, descrição, categoria, valor estimado, observações) mais frequência, dia, início e fim
   opcional. **Sem parcelas** (receita já não parcela; compra parcelada é a INV-5). Valem as regras e
   os códigos de erro do lançamento. A conta não muda depois de criada (como na ADR-0007).
5. **Editar, encerrar e excluir.** *Recomendação:* editar muda só as ocorrências **ainda não lançadas
   nem puladas**; o que já foi lançado fica como está (o lançamento se corrige pela LNC). Encerrar =
   definir o fim. Excluir a regra tira as ocorrências em aberto da previsão e mantém os lançamentos já
   feitos (eles só deixam de apontar para uma regra viva).
6. **Lançar, pular e "a lançar".** *Recomendação:* lançar uma ocorrência cria um lançamento comum (o
   mesmo `TransactionRegistered` de sempre) pré-preenchido com a regra, com valor e data ajustáveis, e
   grava o vínculo (regra + data da ocorrência). Excluir esse lançamento **reabre** a ocorrência. Pular
   grava a ocorrência como pulada (some da previsão). Ocorrências de dias que já passaram sem lançar nem
   pular ficam numa lista **"A lançar"** (últimos 60 dias) e **não** entram na previsão: ou aconteceram
   e foram lançadas sem vínculo, ou não aconteceram; nos dois casos, contar seria chute. "Hoje" no fuso
   de `Reports:TimeZone`, que o `transactions` passa a ler também.
7. **Na previsão.** *Recomendação:* de hoje em diante, cada ocorrência em aberto entra como um
   lançamento futuro: fora do cartão, no mês da data; **no cartão, na fatura do mês de vencimento**
   (mesma regra da compra à vista, ADR-0005 decisões 4 e 8). Entra também no saldo projetado (INV-13).
   A resposta separa a parte recorrente (`recurringIncome`, `recurringExpense`, `recurringInvoices`) para
   a tela mostrar o que é previsto. A **fatura exibida e o limite disponível não mudam**: mostram só o
   que foi lançado.
8. **Fora da previsão.** *Recomendação:* recorrência em aberto **não** entra no Resumo, nos orçamentos,
   no "Sem orçamento", nas sugestões de categoria nem na exportação contábil; só o lançamento feito a
   partir dela.
9. **Importação.** *Recomendação (Should):* na prévia do extrato, a linha da mesma conta e do mesmo tipo,
   com valor até 10% diferente do estimado e data até 5 dias antes ou depois de uma ocorrência em
   aberto, vem sugerida como "lançamento da recorrência <descrição>"; confirmar cria o lançamento com a
   chave externa **e** o vínculo. Continua sendo sugestão (como as pistas da ADR-0004, decisão 5). Até
   lá, quem importa o extrato pula a ocorrência à mão para não contar duas vezes na previsão, e a tela
   diz isso.

- ✅ **REC-1** ADR: lançamentos recorrentes. → [ADR-0008](adr/0008-lancamentos-recorrentes.md), Aceita.
  - *Dado* as decisões em aberto 1 a 9 deste épico, *quando* o dono do produto revisa as opções,
    *então* a ADR-0008 é registrada como **Aceita** em `docs/adr/` (com o índice atualizado) e o PO
    ajusta as histórias seguintes ao que foi decidido.
  - Refinamentos registrados na ADR além das recomendações: a ocorrência é identificada por regra +
    **período** (`yyyy-MM` na mensal, `yyyy` na anual), não pela data, para mudar o dia da regra não
    abrir de novo um mês já lançado; só as ocorrências fechadas são gravadas (as em aberto saem da
    agenda). A data de início é a âncora da agenda (dia, e mês na anual), e mês sem aquele dia usa o
    último. Os eventos ficaram `RecurrenceSaved`, `RecurrenceDeleted`, `RecurrenceOccurrenceClosed`
    (`Launched`/`Skipped`, com o `transactionId` do lançado) e `RecurrenceOccurrenceReopened`; o
    `TransactionRegistered` não muda. A regra tem `version` e 409 como o lançamento (ADR-0007), e a
    conta não muda (`recurrence.account_immutable`). Lançar aceita período futuro (conta paga
    adiantado) e outro valor, data e observações; período fora da agenda = 400
    `recurrence.period_invalid`, já fechado = 409 `recurrence.occurrence_closed`; pulo pode ser
    desfeito. O fuso vira a configuração `TimeZone`, lida pelo `transactions` e pelo `reporting`.
    Os totais da previsão já incluem a recorrência, e `recurringIncome`/`recurringExpense`/
    `recurringInvoices` são a parte dela. Rotas em `/transactions/recurrences` (decisão 12 da ADR).
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **REC-2** Regras de recorrência na API. → `/transactions/recurrences`, `RecurrenceSaved`/`RecurrenceDeleted`.
  - *Dado* uma conta minha, *quando* envio `POST /transactions/recurrences` com tipo, meio, descrição,
    categoria, valor estimado, frequência e início (a âncora da agenda), *então* a regra é criada com as validações do
    lançamento (400 com os mesmos códigos; parcelas recusadas) e um `RecurrenceSaved` sai no outbox no
    mesmo commit.
  - *Dado* minhas regras, *quando* consulto `GET /transactions/recurrences`, *então* vejo cada uma com a
    próxima ocorrência em aberto; *e* a regra de outro usuário responde 404.
  - *Dado* uma regra com ocorrências lançadas, *quando* edito o valor ou defino o fim, *então* só as
    ocorrências em aberto mudam; *quando* a excluo, *então* os lançamentos feitos a partir dela ficam.
  - *Dado* o dia 31, *quando* o mês tem 30 dias (ou é fevereiro), *então* a ocorrência cai no último dia.
  - Feito assim: `Recurrence` (Domain/Recurrences) valida com o próprio `Transaction.Create` (mesmos
    códigos, parcelas sempre 1) e guarda frequência (`Monthly`/`Yearly`), início (âncora) e fim
    opcional (antes do início = 400 `recurrence.ends_before_start`); a agenda (`RecurrenceSchedule`)
    dá o período de uma data, a data de um período (nula fora da agenda) e as ocorrências de uma
    janela. Tabelas `recurrences` e `recurrence_occurrences` (migração `Recurrences`; só períodos
    fechados, chave regra + período). Rotas `GET/POST /transactions/recurrences` e
    `GET/PUT/DELETE /transactions/recurrences/{id}`: a leitura traz `nextOccurrence` (período e data da
    primeira em aberto de hoje em diante); `PUT` exige `version` (400 `recurrence.version_required`; antiga =
    409 `recurrence.concurrency_conflict`; conta diferente = 400 `recurrence.account_immutable`; sem
    mudança = 200 sem evento). Conta e cartão conferidos no accounts. Eventos com base própria
    (`RecurrenceEvent`: o agregado é a regra, sem `transactionId` no payload); `RecurrenceSaved` leva o
    escopo da conta e `recurrenceVersion`. "Hoje" pelo `LocalCalendar`, da configuração `TimeZone`
    (padrão `America/Sao_Paulo`, mesma resolução do reporting).
  - Estimativa: 5 · **Must** · Dependências: REC-1
- ✅ **REC-3** Lançar e pular ocorrência; lista "A lançar". → `.../occurrences/{period}/launch|skip`, `/recurrences/due`.
  - *Dado* uma ocorrência em aberto, *quando* a lanço (podendo trocar valor e data), *então* nasce um
    lançamento comum vinculado à regra e ao período da ocorrência, e ela deixa de estar em aberto;
    lançar o mesmo período de novo responde 409 `recurrence.occurrence_closed`.
  - *Dado* uma ocorrência lançada, *quando* excluo o lançamento, *então* ela volta a ficar em aberto.
  - *Dado* uma ocorrência, *quando* a pulo, *então* ela some da previsão e da lista "A lançar"; *e*
    desfazer o pulo a reabre.
  - *Dado* ocorrências de dias que já passaram sem lançar nem pular, *quando* consulto
    `GET /transactions/recurrences/due`, *então* vejo as dos últimos 60 dias, das mais antigas para as
    mais novas.
  - Feito assim: `POST /transactions/recurrences/{id}/occurrences/{period}/launch` (corpo opcional com
    `amount`, `occurredOn`, `notes`; sem eles valem os da regra e a data agendada) cria um lançamento comum
    (`TransactionRegistered`, mesmas conferências da criação) com `recurrenceId`/`recurrencePeriod`
    (migração `TransactionRecurrenceLink`; o DTO do lançamento os devolve), grava o período como
    `Launched` em `recurrence_occurrences` e publica `RecurrenceOccurrenceClosed` (com o `transactionId`),
    tudo num commit; corrida na chave (regra, período) vira o mesmo 409. `POST .../skip` fecha como
    `Skipped` (`transactionId` nulo no evento) e `DELETE .../skip` reabre (`RecurrenceOccurrenceReopened`);
    desfazer pulo de período lançado = 409 `recurrence.occurrence_launched`, de período aberto = 404
    `recurrence.occurrence_not_skipped`. Excluir o lançamento de uma ocorrência remove o período fechado e
    publica o `RecurrenceOccurrenceReopened` no commit da exclusão (`RecurrenceLinks`). `GET
    /transactions/recurrences/due` lista os períodos em aberto com data de 60 dias atrás até ontem, das
    mais antigas para as mais novas.
  - Estimativa: 5 · **Must** · Dependências: REC-2
- ✅ **REC-4** Previsão conta com as recorrências. → projeção das regras no reporting, parte recorrente na previsão.
  - *Dado* um salário mensal (receita, Pix, dia 5) e um aluguel (gasto, boleto, dia 10), *quando* consulto
    a previsão de 6 meses, *então* cada mês a partir de hoje tem os dois nas entradas e saídas, com a
    parte recorrente separada, e o saldo projetado acompanha.
  - *Dado* uma assinatura no cartão, *quando* olho a previsão, *então* ela entra na fatura do mês de
    vencimento; *e* a fatura do cartão e o limite disponível não mudam.
  - *Dado* uma ocorrência lançada, pulada ou de um dia que já passou, *quando* olho a previsão, *então*
    ela não entra como recorrente (a lançada entra como lançamento, uma vez só).
  - *Dado* uma recorrência em aberto, *quando* olho Resumo e orçamentos, *então* ela não aparece.
  - Feito assim: o reporting lê os quatro eventos novos (v1) e projeta `projected_recurrences` (upsert
    pelo `RecurrenceSaved`; `RecurrenceDeleted` remove a regra e os períodos dela) e
    `projected_recurrence_occurrences` (upsert por regra + período no `RecurrenceOccurrenceClosed`;
    `RecurrenceOccurrenceReopened` remove), migração `ProjectedRecurrences`. A agenda tem cópia própria no
    reporting (`RecurrenceSchedule`, Domain/Forecast), como manda a ADR-0001. A previsão soma, de hoje
    até o fim da janela, cada período em aberto pelo valor estimado: fora do cartão no mês da data (e no
    saldo projetado, pela conta da regra); no cartão, na fatura do vencimento (`CardCycle.DueMonthOf`,
    à vista, receita abate) e no saldo pela conta do cartão; cartão que o accounts não devolve ao dono
    fica de fora. `MonthFlow` e o DTO ganharam `recurringIncome`, `recurringExpense` e
    `recurringInvoices` (a parte recorrente que já está nos totais). Resumo, orçamentos, fatura e limite
    não leem as tabelas novas. O compose passa o mesmo fuso aos dois serviços (`APP_TIME_ZONE`, padrão
    `America/Sao_Paulo`: `TimeZone` no transactions, `Reports__TimeZone` no reporting). Smoke passo 24
    no MySQL real (regra prevista, lançada com outro valor, relançar = 409, período inválido = 400,
    excluir devolve, pular tira; 46 passos).
  - Estimativa: 5 · **Must** · Dependências: REC-3
- ✅ **REC-5** Recorrências e "A lançar" na SPA. → aba "Recorrências".
  - *Dado* a aba Lançamentos, *quando* abro "Recorrências", *então* cadastro, edito, encerro e excluo
    regras com o mesmo formulário do lançamento (mais frequência, dia, início e fim) e vejo a próxima
    ocorrência de cada uma.
  - *Dado* ocorrências a lançar, *quando* abro "A lançar", *então* lanço cada uma com o valor real
    (formulário pré-preenchido) ou a pulo, e o lançamento aparece na lista de lançamentos.
  - Feito assim: em vez de uma sub-aba de Lançamentos, uma aba própria **"Recorrências"**
    (`tab-recurrences`, `RecurrencesView`), porque a tela de lançamentos já é grande. Ela tem a lista "A
    lançar" (`rec-due-row`, com lançar pelo valor e data reais e pular), o formulário da regra (`rec-form`:
    os campos do lançamento mais frequência, "a partir de" e "até"; conta travada na edição; conflito de
    versão recarrega e avisa, `rec-conflict`) e as regras cadastradas (`rec-row`: "todo dia N" ou "todo
    ano em dd/mm", valor estimado, próxima ocorrência, e lançar ou pular a próxima direto dali, para quem
    paga adiantado; editar e excluir). Na lista de lançamentos, o lançado a partir de uma regra mostra
    "recorrência" (`tx-recurring-label`). Cypress `recurrences.cy.ts`.
  - Estimativa: 5 · **Must** · Dependências: REC-3
- ✅ **REC-6** Previsão mostra o que é recorrente. → "previsto" em entradas, saídas e faturas.
  - *Dado* a previsão, *quando* um mês tem recorrências, *então* entradas, saídas e faturas mostram
    quanto delas é previsto por recorrência, e o aviso da tela diz que a recorrência conta até ser
    lançada ou pulada (e, até a REC-9, que quem importa extrato pula a ocorrência importada).
  - Feito assim: entradas, saídas e faturas ganham, abaixo do valor, "R$ X previsto"
    (`forecast-income-recurring`, `forecast-expense-recurring`, `forecast-invoices-recurring`) quando o mês
    tem recorrência em aberto; o aviso da tela (`forecast-recurring-notice`) explica que a recorrência conta
    até ser lançada ou pulada. Os specs da previsão passaram a ler só o valor da célula (primeiro nó de
    texto), como já faziam com as faturas.
  - Estimativa: 2 · **Must** · Dependências: REC-4, REC-5
- ✅ **REC-7** E2E Cypress: cadastrar, prever, lançar e conferir. → `recurrence-journey.cy.ts`.
  - *Dado* uma conta com saldo e um cartão, *quando* cadastro salário, aluguel e uma assinatura no cartão
    pela SPA, *então* a previsão mostra os três nos meses certos; *quando* lanço o aluguel do mês com
    outro valor, *então* a previsão conta o valor lançado uma vez só, o Resumo ganha o aluguel e o
    orçamento da categoria o vê; *quando* pulo o salário, *então* ele some daquele mês.
  - Feito assim: tudo pela SPA, comparando a previsão PF por diferença (o demo acumula dados): conta PF
    com cartão (fecha 10, vence 17) e orçamento na categoria do aluguel; salário (dia 5 do mês que vem),
    aluguel (hoje) e assinatura no cartão (hoje, que cai na fatura deste mês até o dia 10 e na do mês
    seguinte depois). Sem lançar nada, o mês atual ganha R$ 1.500 de saídas e o seguinte R$ 3.000 de
    entradas, R$ 1.500 de saídas e R$ 50 de fatura, com o "previsto" na tela. Lançado por R$ 1.650, o
    aluguel conta uma vez só pelo valor real e aparece no Resumo e no orçamento; pulado, o salário sai do
    mês seguinte. No fim as regras são excluídas.
  - Estimativa: 3 · **Must** · Dependências: REC-5, REC-6
- ✅ **REC-8** Recorrência anual. → frequência `Yearly`, período `yyyy`.
  - *Dado* um IPVA anual em 15 de março, *quando* olho a previsão de 12 meses, *então* ele aparece só em
    março; *e* uma regra em 29 de fevereiro cai no dia 28 nos anos não bissextos.
  - Feito assim: a frequência anual já nasceu com a REC-2 (agenda no dia e mês do início, período
    `yyyy`) e a tela tem "Todo ano"; a história fechou com os testes: previsão de 12 meses com o IPVA só
    em março (reporting) e regra em 29/02/2032 com período `2033` lançado em 28/02/2033 e próxima
    `2034` em 28/02 (transactions); período mensal numa regra anual = 400.
  - Estimativa: 2 · **Should** · Dependências: REC-4
- ✅ **REC-9** Importação reconhece a ocorrência. → sugestão na prévia, vínculo na confirmação.
  - *Dado* um aluguel recorrente em aberto (R$ 2.000, dia 10), *quando* importo um extrato com um boleto
    de R$ 2.050 no dia 11 na mesma conta, *então* a prévia sugere "lançamento da recorrência Aluguel";
    confirmar cria o lançamento com a chave externa e o vínculo, e a ocorrência sai da previsão.
  - *Dado* a sugestão, *quando* troco para lançamento comum, *então* nada é vinculado.
  - Feito assim: na prévia, o `PreviewImportHandler` busca as ocorrências em aberto das regras **da conta
    do extrato** entre a menor e a maior data das linhas (com 5 dias de folga) e a regra pura
    `RecurrenceMatcher` casa cada linha incluída (fora pagamento de fatura) com no máximo uma ocorrência
    do mesmo tipo, valor até 10% diferente e data até 5 dias, a de data mais próxima primeiro; cada
    ocorrência vai para uma linha só. A linha guarda `recurrenceId`/`recurrencePeriod`/
    `recurrenceDescription` (migração `ImportLineRecurrence`) e herda a categoria da regra. A decisão
    `recurrence: false` troca para lançamento comum. Na confirmação, o lançamento importado ganha o
    vínculo e o período fecha (`RecurrenceOccurrenceClosed`) no mesmo commit; se a regra sumiu ou o
    período foi fechado depois da prévia, o lançamento entra comum. Desfazer a importação reabre os
    períodos. Na SPA, a linha mostra "lançamento da recorrência <descrição> (<período>)" com uma caixa
    marcada (`import-row-recurrence`) que, desmarcada, manda `recurrence: false`. Cypress
    `recurrence-import-repeat.cy.ts`.
  - Estimativa: 5 · **Should** · Dependências: REC-3
- ✅ **REC-10** Repetir a partir de um lançamento. → "repetir" na lista, `fromTransactionId`.
  - *Dado* um lançamento na lista, *quando* escolho "repetir todo mês", *então* o formulário de
    recorrência abre preenchido com os dados dele, começando no mês seguinte, e o lançamento original
    fica vinculado à ocorrência do mês dele.
  - Feito assim: os dois critérios se contradiziam (se a regra começa no mês seguinte, o mês do
    lançamento não está na agenda). A regra é **ancorada na data do lançamento**, que fecha o período
    dele; a próxima em aberto fica sendo a do mês seguinte. `POST /transactions/recurrences` aceita
    `fromTransactionId` (lançamento do dono, da mesma conta, ainda sem vínculo e dentro da agenda; senão
    400 `recurrence.from_transaction_invalid`) e publica `RecurrenceSaved` e `RecurrenceOccurrenceClosed`
    no mesmo commit. Na lista de lançamentos, "repetir" (`tx-repeat`, só à vista e sem vínculo) abre a
    aba Recorrências preenchida (`rec-from-transaction`, com "desistir"). Cypress
    `recurrence-import-repeat.cy.ts`.
  - Estimativa: 2 · **Could** · Dependências: REC-5

**Fora do escopo (Won't, nesta versão):** gerar lançamentos sozinho; frequências semanal e quinzenal;
recorrência por "N ocorrências"; reajuste automático do valor (IGP-M, IPCA); lembrete por e-mail ou
push; recorrência de pagamento de fatura (a fatura já entra pela previsão); recorrência que muda de
conta; orçamento "previsto até o fim do mês" a partir das recorrências.

**Priorização (MoSCoW):** Must = REC-1 a REC-7 (27 pts): cadastrar, prever, lançar ou pular e ver na
previsão o que é recorrente, provado ponta a ponta. Should = REC-8, REC-9 (7 pts): o anual cobre IPVA e
anuidades; reconhecer na importação tira de quem importa extrato o trabalho de pular à mão. Could =
REC-10 (2 pts). Total: 36 pts.

**Ordem sugerida:** REC-1 → REC-2 → REC-3 → REC-4 (com o `reporting` implantado antes do `transactions`
que publica os eventos novos) → REC-5 → REC-6 → REC-7 (fecha o Must) → REC-8 → REC-9 → REC-10.

## 🟢 EPIC NTF — Lembretes de vencimento  · *Done*
**Problema:** o produto já sabe o que vai vencer (a fatura de cada cartão com a situação dela, as
ocorrências recorrentes e o que está "a lançar"), mas só mostra isso a quem abre a aba certa e procura. Fatura
fechada que ninguém olha vira fatura vencida, com juros no banco; boleto do aluguel cadastrado como
recorrência passa do dia sem ser pago nem lançado. Os orçamentos (BDG) e a fatura (INV) recusaram
"notificação ativa" justamente porque não havia infraestrutura para isso.
**Objetivo (outcome):** ao abrir o app, o usuário vê na hora o que vence nos próximos dias e o que já
venceu sem pagamento, e chega com um clique onde resolve (pagar a fatura, lançar ou pular a ocorrência).
Num segundo passo, quem quiser recebe um resumo por e-mail no dia, sem precisar abrir o app.
**Métricas de sucesso:** (1) toda fatura fechada ou vencida sem pagamento total e toda saída recorrente que
vence em até 3 dias aparece no aviso, sem o usuário abrir Cartões ou Recorrências; (2) pagar a fatura ou
lançar/pular a ocorrência tira o aviso na próxima leitura; (3) nenhum aviso de coisa já resolvida; (4) a
jornada "aviso → resolver → aviso some" passa no E2E do CI.
**Rastreabilidade:** `docs/PROMPT.md` → controle de gastos com cartão e boleto; recusas anteriores
("notificação ativa fica fora: não há infraestrutura", BDG; "notificação de vencimento por e-mail ou push",
Won't do INV); ideia de produto promovida a pedido do dono do produto (2026-11-23).

**Achados no código que moldam o épico:**
- **O reporting já tem o que vence.** A fatura de cada cartão (`InvoiceCalculator`, com situação Aberta,
  Fechada, Paga, Parcialmente paga, Vencida, data de vencimento e quanto falta) e as recorrências com os
  períodos fechados (projeção da REC-4) moram nele. O "a lançar" é calculado no `transactions`, mas o
  reporting tem a mesma agenda (`RecurrenceSchedule`) e os mesmos períodos fechados.
- **A fatura depende do ciclo do cartão, que vem do `accounts` com o token do usuário** (ADR-0005,
  decisão 3). Na consulta isso é de graça; um job em segundo plano (e-mail diário) não tem token de
  usuário e hoje não conseguiria calcular faturas.
- **Existe precedente para serviço consultar o `accounts`:** `/internal/**` (bloqueado no gateway) com um
  escopo de serviço (`accounts.scopes.read`, usado pelo `transactions` no backfill de escopo, EVT-4).
- **Nenhum serviço conhece o e-mail do usuário.** A SPA não pede o escopo `email`, nenhum serviço lê o
  claim, e o `identity` vive em repositório próprio (ADR-0003): mudar o que ele emite é trabalho fora deste
  repositório. Também não há SMTP, biblioteca de e-mail nem servidor de e-mail de desenvolvimento.
- **Já existe worker no reporting** (`ProjectionWorker`) e "hoje" no fuso configurado (`Reports:TimeZone`).
- **Os orçamentos já calculam Atenção e Estourado** (BDG-4) no reporting, mas não avisam.

**Decisões (aceitas na [ADR-0009](adr/0009-lembretes-de-vencimento.md), NTF-1; ficou a recomendação de cada uma, com os refinamentos da NTF-1):**
1. **Canal.** Opções: (a) **avisos no app**: um sino no topo com o número de avisos e um painel, calculados
   na consulta com o token do usuário; (b) **e-mail diário**, enviado por um job; (c) **push do navegador**.
   *Recomendação:* (a) no Must, porque resolve a maior parte do problema sem infraestrutura nova; (b) no
   Should, com as dependências abaixo; (c) fora, porque exige service worker, chaves VAPID e permissão do
   navegador, com pouco ganho sobre o e-mail para um app que se abre todo dia.
2. **Onde moram.** *Recomendação:* no `reporting`, ao lado da fatura, da previsão e dos orçamentos, que
   são a fonte dos avisos; sem serviço novo. Rota `GET /reports/reminders`, pela rota do gateway que já
   existe, com `expenses.read`. Os avisos são **calculados na consulta e nunca guardados**; só a
   dispensa (decisão 5) e as preferências (decisão 4) são gravadas.
3. **Do que avisar.** *Recomendação:*
   - **Fatura:** não paga por inteiro, com vencimento de hoje até N dias (situação Fechada ou Parcialmente
     paga), ou já Vencida (até 30 dias atrás). Mostra cartão, vencimento e quanto falta.
   - **Saída recorrente** (gasto ou compra no cartão de uma regra REC) com ocorrência em aberto de hoje até
     N dias. Receita recorrente não gera aviso.
   - **"A lançar"** (REC-3): um aviso só, com a quantidade, levando à aba Recorrências.
   - **Orçamento** em Atenção ou Estourado no mês corrente: Could.
4. **Antecedência e preferências.** *Recomendação:* N = **3 dias** por padrão, configurável de 0 a 15 por
   usuário, e cada tipo de aviso pode ser desligado. As preferências ficam no reporting (dado do usuário,
   como os orçamentos, ADR-0002). As preferências entram no Should; o Must usa o padrão.
5. **Dispensar.** *Recomendação:* "dispensar" esconde o aviso **até ele mudar de estado**. Uma fatura
   dispensada como "vence em 3 dias" volta quando vence sem pagamento; uma ocorrência dispensada não volta.
   A chave de dispensa é estável por item (cartão + mês de vencimento + estado; regra + período), guardada
   no reporting. Resolver (pagar, lançar, pular) tira o aviso sem precisar dispensar.
6. **E-mail diário** (Should). *Recomendação:*
   - Opt-in nas preferências, um resumo por dia no horário configurado (padrão 8h no fuso do app), só quando
     há aviso novo desde o último envio.
   - **Endereço:** o usuário informa e **confirma por um link** com token de uso único e validade de 24h
     (sem confirmação, um erro de digitação manda dados financeiros a terceiros). Pré-preenchido com o
     claim `email` se um dia o `identity` passar a emiti-lo, sem depender disso agora.
   - **Ciclo do cartão para o job:** endpoint interno no `accounts` (`/internal/credit-cards`, por dono)
     com um escopo de serviço novo (`accounts.cards.read`), no padrão do EVT-4.
   - **Envio:** SMTP configurável (`Smtp:*`), com MailKit (licença MIT) e o Mailpit no compose para
     desenvolvimento e E2E. O conteúdo traz só descrição, valor e data. O link leva ao app, que pede login:
     nada de link mágico.
7. **Atalhos.** *Recomendação:* cada aviso leva aonde se resolve: fatura → aba Cartões com aquela fatura
   aberta; ocorrência ou "a lançar" → aba Recorrências; orçamento → aba Orçamentos.

- ✅ **NTF-1** ADR: lembretes de vencimento. → [ADR-0009](adr/0009-lembretes-de-vencimento.md), Aceita.
  - *Dado* as decisões em aberto 1 a 7 deste épico, *quando* o dono do produto revisa as opções,
    *então* a ADR-0009 é registrada como **Aceita** em `docs/adr/` (com o índice atualizado) e o PO
    ajusta as histórias seguintes ao que foi decidido.
  - Refinamentos registrados na ADR além das recomendações: tipos `InvoiceDue`, `InvoiceOverdue`,
    `RecurrenceDue`, `RecurrencesToLaunch` e `Budget`, cada um com chave estável e urgência (`Overdue` >
    `Today` > `Soon`); a fatura "a vencer" e a "vencida" têm chaves diferentes (dispensar uma não esconde a
    outra) e o "a lançar" volta quando entra pendência nova. Tabelas `reminder_settings` (sem linha = padrão)
    e `reminder_dismissals` (limpas depois de 120 dias). E-mail: token de confirmação de uso único, 24 h,
    guardado como hash; o link abre a SPA logada, que confirma por rota autenticada (nada de rota anônima);
    job de hora em hora, na hora escolhida (padrão 8h), uma vez por dia e só com aviso novo desde o último
    envio; sem `Smtp:Host`, não envia. Escopo de serviço `accounts.cards.read` na semente do identity deste
    repositório; `/api/internal/**` já é bloqueado no gateway.
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **NTF-2** API de avisos. → `GET /reports/reminders`.
  - *Dado* um cartão com fatura Fechada de R$ 800 que vence em 2 dias, *quando* consulto
    `GET /reports/reminders`, *então* recebo um aviso de fatura com cartão, vencimento e R$ 800 a pagar;
    *e* com pagamento parcial de R$ 300, o aviso mostra R$ 500.
  - *Dado* uma fatura Vencida sem pagamento total, *quando* consulto, *então* o aviso vem como vencida
    (mais urgente) até 30 dias depois do vencimento.
  - *Dado* um aluguel recorrente (gasto) com ocorrência em aberto daqui a 3 dias, *quando* consulto,
    *então* recebo o aviso; *dado* um salário (receita), *então* não.
  - *Dado* ocorrências "a lançar", *quando* consulto, *então* recebo um aviso só, com a quantidade.
  - *Dado* uma fatura paga ou uma ocorrência lançada ou pulada, *quando* consulto, *então* o aviso sumiu.
  - Feito assim: regra pura `ReminderCalculator` (Domain/Reminders) sobre as faturas de todos os cartões do
    usuário (`CardInvoiceSource`, ciclo lido no accounts com o token dele) e as recorrências projetadas
    (agenda da REC-4): `InvoiceDue` (Fechada ou Parcialmente paga, falta pagar, vence de hoje até 3 dias;
    `Today` se vence hoje), `InvoiceOverdue` (Vencida, até 30 dias), `RecurrenceDue` (ocorrência em aberto de
    gasto de hoje até 3 dias) e um `RecurrencesToLaunch` com a quantidade (em aberto de 60 dias atrás até
    ontem, gasto ou receita; chave pela data mais recente). Cada aviso traz chave, urgência, data, valor,
    título e o destino do atalho (cartão e mês, ou regra e período); a lista vem por urgência, data e
    título, com `overdue` (quantos vencidos) e `leadDays`. Smoke passo 25 (aviso aparece e some ao pular;
    47 passos).
  - Estimativa: 5 · **Must** · Dependências: NTF-1
- ✅ **NTF-3** Sino e painel de avisos na SPA. → `RemindersBell` no topo.
  - *Dado* avisos pendentes, *quando* abro o app, *então* o topo mostra um sino com o número de avisos
    (vencidos em destaque) e, ao clicar, um painel do mais urgente para o menos urgente.
  - *Dado* um aviso de fatura, *quando* clico nele, *então* vou para Cartões com aquela fatura aberta; *e*
    um aviso de ocorrência ou "a lançar" leva a Recorrências.
  - *Dado* que resolvi um aviso em outra aba do app, *quando* volto ao painel, *então* ele está
    atualizado (o painel relê ao abrir e quando a janela volta ao foco).
  - Feito assim: `RemindersBell` no topo (`reminders-bell`, com `reminders-count` e a classe `has-overdue`
    quando há vencido) relê os avisos ao montar, a cada troca de aba, ao abrir o painel e quando a janela
    volta ao foco; falha na leitura só deixa o sino sem número. O painel (`reminders-panel`, fixo no canto
    da janela, fecha ao clicar fora) lista `reminder-item` com `data-kind`/`data-urgency`, título e o
    detalhe ("venceu em", "vence hoje", "vence em", "pendentes desde"). Fatura abre a aba Cartões direto na
    fatura do aviso (`CardsView.openInvoice`, `InvoicesView.initialDueMonth`); ocorrência e "a lançar" levam
    a Recorrências. Cypress `reminders.cy.ts` (fatura vencida de um cartão que fecha no dia da compra e
    vence 5 dias depois, com a compra de 15 a 25 dias atrás, e aluguel de amanhã).
  - Estimativa: 5 · **Must** · Dependências: NTF-2
- ✅ **NTF-4** E2E Cypress: aviso, resolver, aviso some. → `reminders-journey.cy.ts`.
  - *Dado* um cartão com fatura fechada a vencer e um aluguel recorrente a vencer, *quando* abro o app,
    *então* o sino mostra os dois; *quando* pago a fatura pelo atalho e lanço o aluguel, *então* os dois
    avisos somem.
  - Feito assim: tudo pela SPA, achando os avisos pelo título (único por execução: o demo acumula dados).
    Fatura vencida de um cartão que fecha no dia da compra e vence 5 dias depois (compra de 15 a 25 dias
    atrás, escolhida para os dois dias existirem em todo mês) e aluguel recorrente de amanhã. A fatura é
    aberta pelo aviso e paga pelo valor sugerido (fica "Paga"); o aluguel é lançado pela aba Recorrências;
    os dois avisos somem sem dispensar nada.
  - Estimativa: 3 · **Must** · Dependências: NTF-3
- ✅ **NTF-5** Dispensar aviso. → `POST /reports/reminders/dismissals`.
  - *Dado* um aviso de fatura "vence em 3 dias", *quando* o dispenso, *então* ele some; *e* volta se a
    fatura vencer sem pagamento.
  - *Dado* um aviso de ocorrência, *quando* o dispenso, *então* ele não volta para aquele período.
  - Feito assim: `POST /reports/reminders/dismissals` com `{ key }` (`expenses.write`; chave vazia, longa
    demais ou de prefixo desconhecido = 400 `reminders.key_invalid`; a mesma chave duas vezes = 204 sem
    duplicar) grava em `reminder_dismissals` (dono + chave, migração `ReminderDismissals`) e apaga as
    dispensas do usuário com mais de 120 dias. `GET /reports/reminders` esconde as chaves dispensadas; como
    a fatura "a vencer" e a "vencida" têm chaves diferentes, a vencida volta. Na SPA, cada aviso tem
    "dispensar" (`reminder-dismiss`), que o tira na hora e relê. Cypress: segundo caso do `reminders.cy.ts`
    (dispensado continua fora depois de recarregar a página).
  - Estimativa: 3 · **Should** · Dependências: NTF-3
- ✅ **NTF-6** Preferências dos avisos. → `GET/PUT /reports/reminder-settings`.
  - *Dado* as preferências, *quando* mudo a antecedência para 7 dias e desligo os avisos de recorrência,
    *então* o painel passa a mostrar faturas que vencem em até 7 dias e nenhuma recorrência.
  - *Dado* uma antecedência fora de 0 a 15, *quando* salvo, *então* recebo 400.
  - Feito assim: `reminder_settings` (uma linha por usuário, criada na primeira gravação; migração
    `ReminderSettings`) com antecedência e os tipos ligados (faturas, recorrências e orçamentos, este para a
    NTF-9). `GET /reports/reminder-settings` devolve os padrões (3 dias, tudo ligado) quando não há linha;
    `PUT` (`expenses.write`) recusa antecedência fora de 0 a 15 com 400 `reminders.lead_days_invalid`. A
    lista de avisos usa a antecedência gravada e nem lê os cartões quando as faturas estão desligadas. Na
    SPA, "preferências" no painel do sino (`reminders-settings`: dias, faturas, recorrências, orçamentos);
    o campo de dias guarda o texto digitado (apagar para redigitar não vira "0"). Cypress: terceiro caso do
    `reminders.cy.ts`, que devolve os padrões no fim (as preferências do demo valem para todos os specs).
    O primeiro caso passou a dispensar o aviso da fatura vencida que cria, para não acumular no demo.
  - Estimativa: 3 · **Should** · Dependências: NTF-2
- ✅ **NTF-7** Ciclo dos cartões para o serviço. → `GET /internal/credit-cards` no accounts.
  - *Dado* o `reporting` com token de serviço e o escopo `accounts.cards.read`, *quando* consulta
    `GET /internal/credit-cards?ownerUserId=...` no `accounts`, *então* recebe os cartões do dono com
    ciclo, conta e escopo; *e* pelo gateway a rota é bloqueada, e sem o escopo dá 403.
  - Feito assim: `GetOwnerCardsForServiceHandler` no accounts devolve os cartões do dono (ativos ou não:
    cartão desativado ainda pode ter fatura) com os campos que o reporting já lê do `/credit-cards` mais o
    `accountScope`; política `AccountCardsRead` (escopo `accounts.cards.read`), sem `ownerUserId` = 400
    `service_cards.owner_required`. O escopo entrou na semente do identity (`infra/identity/`), e o
    identity já em execução o concede ao cliente de serviço depois de reiniciar (a semente atualiza
    cliente existente). Conferido no stack real: com o escopo, 275 cartões do demo; sem ele, 403; pelo
    gateway, 404. Documentado em `docs/architecture.md`.
  - Estimativa: 3 · **Should** · Dependências: NTF-1
- ✅ **NTF-8** Resumo diário por e-mail. → `ReminderDigestWorker`, confirmação por link, MailKit + Mailpit.
  - *Dado* que ativei o e-mail e confirmei o endereço pelo link, *quando* chega o horário do resumo e
    tenho aviso novo desde o último envio, *então* recebo um e-mail com os avisos e um link para o app;
    *e* sem aviso novo, nada é enviado.
  - *Dado* um endereço não confirmado (ou link vencido depois de 24h), *quando* chega o horário, *então*
    nada é enviado.
  - *Dado* o compose de desenvolvimento, *quando* o resumo sai, *então* ele aparece no Mailpit (o E2E
    confere por lá).
  - Feito assim: `reminder_settings` ganhou endereço, liga/desliga, hora (0 a 23, padrão 8), confirmação
    (hash SHA-256 do token de uso único, validade de 24 h) e o registro do último envio (dia e chaves);
    migração `ReminderEmail`. `PUT /reports/reminder-settings/email` grava e, quando o endereço muda, zera a
    confirmação e o último envio e manda o link `/confirm-email?token=` (400 `reminders.email_invalid`,
    `email_required`, `send_hour_invalid`); `POST .../email/confirm` (logado; token errado, vencido, de outro
    usuário ou reusado = 400 `reminders.email_token_invalid`) e `POST .../email/resend` (token novo, o antigo
    deixa de valer). O `PUT` das preferências continua só com os tipos (um corpo sem e-mail não apaga o
    endereço). O cálculo dos avisos foi para o `ReminderComposer`, usado pela API (cartões com o token do
    usuário) e pelo `SendReminderDigestsJob` (cartões pelo `/internal/credit-cards`, NTF-7), que o
    `ReminderDigestWorker` roda a cada `Reminders:Digest:IntervalSeconds` (padrão 1 h; 20 s no compose).
    Envio com MailKit 4.18.2 (MIT; a 5.0 recém-lançada ficou de fora); sem `Smtp:Host`, nada sai. Compose:
    Mailpit `v1.33.1` (8025), `App__PublicUrl`, `Smtp__*` e o escopo `accounts.cards.read` no reporting. Na
    SPA, seção "Resumo diário por e-mail" nas preferências do sino (endereço, ligado, hora, situação e
    "reenviar link"), e a rota `/confirm-email` guarda o token até o login e confirma com a sessão
    (`email-confirmation`). **Dois defeitos achados pelos testes:** o formato `pt-BR` não existe com
    `InvariantGlobalization` (o resumo quebraria no container; o dinheiro e a data são formatados à mão) e
    trocar o endereço não liberava o resumo do dia para o endereço novo. Cypress `reminders-email.cy.ts`
    (link lido pela API do Mailpit, resumo com o aviso de amanhã; desliga o e-mail do demo e apaga as regras
    no fim, mesmo se falhar).
  - Estimativa: 8 · **Should** · Dependências: NTF-6, NTF-7
- ✅ **NTF-9** Aviso de orçamento. → aviso `Budget` em Atenção ou Estourado.
  - *Dado* um orçamento em Atenção ou Estourado no mês corrente, *quando* olho o painel, *então* vejo o
    aviso com o consumido e o limite, levando a Orçamentos.
  - Feito assim: o `ReminderComposer` calcula a situação dos orçamentos do mês corrente com o mesmo
    `BudgetStatusCalculator` da aba Orçamentos (BDG-4) quando o tipo "orçamentos" está ligado; Estourado vira
    aviso urgente e Atenção, "em breve", com vencimento no fim do mês, o consumido em `amount` e o limite em
    `limit`. A chave leva a situação (`budget:{id}:{yyyy-MM}:warning|exceeded`): dispensar o de Atenção não
    esconde o de Estourado. Na SPA: "estourou: R$ 150,00 de R$ 100,00" ou "85% de R$ 1.000,00 gastos", e o
    clique leva a Orçamentos; o resumo por e-mail ganha a mesma linha. Cypress: quarto caso do
    `reminders.cy.ts` (dispensa o aviso no fim).
  - Estimativa: 2 · **Could** · Dependências: NTF-3

**Fora do escopo (Won't, nesta versão):** push do navegador e app móvel; SMS e WhatsApp; aviso por
lançamento individual ou por parcela; aviso de limite do cartão; pagamento a partir do aviso (o atalho leva
à tela de pagamento que já existe); lembretes personalizados ("me avise dia X de Y"); vários endereços de
e-mail; mudar o que o `identity` emite.

**Priorização (MoSCoW):** Must = NTF-1 a NTF-4 (15 pts): avisos no app de fatura e recorrência, com atalho
para resolver, provados ponta a ponta. Should = NTF-5 a NTF-8 (17 pts): dispensar e preferências tornam o
painel usável no dia a dia; o e-mail entrega o lembrete a quem não abre o app, com o endpoint interno do
`accounts` como pré-requisito. Could = NTF-9 (2 pts). Total: 34 pts.

**Ordem sugerida:** NTF-1 → NTF-2 → NTF-3 → NTF-4 (fecha o Must) → NTF-5 → NTF-6 → NTF-7 → NTF-8 → NTF-9.

## 🟢 EPIC RJT — Reajuste das recorrências  · *Done*
**Problema:** aluguel, condomínio, escola, plano de saúde e contratos de serviço são reajustados uma vez
por ano, quase sempre por um índice (IGP-M, IPCA, INPC) ou por um percentual combinado. A recorrência
(REC) só conhece um valor: o usuário precisa lembrar de editar a regra no mês certo, e a edição muda todos
os períodos em aberto de uma vez, inclusive os anteriores ao reajuste (ADR-0008, decisão 6). A previsão
de caixa e o saldo projetado dos próximos 12 meses ficam com o valor velho depois do aniversário do
contrato, e não há registro de quanto a conta valia antes.
**Objetivo (outcome):** a recorrência passa a ter valor com vigência (o reajuste vale a partir de um
período, sem mudar os anteriores), sabe quando e como reajusta, e no aniversário o app sugere o valor novo
pelo índice oficial para o usuário confirmar. A previsão já conta com o reajuste, marcado como estimado
até ser confirmado.
**Métricas de sucesso:** (1) reajustar o aluguel a partir de março não muda janeiro e fevereiro (lançados
ou em aberto); (2) no mês do aniversário, o valor sugerido bate com o índice acumulado de 12 meses
publicado pelo Banco Central (até 2 casas); (3) a previsão de 12 meses de um aluguel com reajuste em março
mostra o valor estimado de março em diante; (4) a jornada "configurar, reajustar, conferir" passa no E2E do
CI.
**Rastreabilidade:** `docs/PROMPT.md` → previsão e controle de gastos fixos; Won't do épico REC
("reajuste automático do valor (IGP-M, IPCA)"); ideia de produto promovida a pedido do dono do produto
(2026-11-25).

**Achados no código que moldam o épico:**
- **A regra tem um valor só.** `Recurrence.Amount` é o valor estimado de todas as ocorrências; editar a
  regra (REC-2) muda todos os períodos em aberto. Os fechados (lançados ou pulados) não mudam, mas não há
  como dizer "de março em diante".
- **O padrão de vigência já existe nos orçamentos** (ADR-0002, decisão 1): `Budget` guarda limites com
  `EffectiveFrom` e `LimitFor(month)` acha o vigente. Mudar o valor cria vigência nova sem mexer nos meses
  anteriores.
- **O valor viaja no `RecurrenceSaved` v1** (estado completo da regra, com um `amount`). O reporting
  projeta a regra e a previsão usa esse valor para todos os períodos (REC-4). Valores por período exigem
  mudar o contrato do evento, com o `reporting` lendo v1 e v2 lado a lado, como no `TransactionRegistered`
  (ADR-0005, decisão 2).
- **Lançar, "a lançar", importação e avisos usam o valor da regra:** o lançamento pré-preenchido (REC-3),
  a lista "a lançar", o casamento da importação (REC-9, 10% de tolerância sobre o valor estimado) e o aviso
  de saída recorrente (NTF-2). Todos precisam do valor vigente no período, não de um valor só.
- **Nenhum serviço acessa a internet.** Todos os clientes HTTP falam com serviços internos. Buscar índices
  oficiais seria a primeira dependência externa do sistema: rede de saída do container, falha da fonte e
  dado que chega com atraso (o IPCA de um mês sai no meio do mês seguinte).
- **Fonte pública dos índices:** a API de séries temporais do Banco Central (SGS) publica a variação mensal
  do IPCA (série 433), do INPC (188) e do IGP-M (189), sem autenticação.
- **"Hoje" e fuso já existem nos dois serviços** (`TimeZone`, ADR-0008, decisão 8), e o `transactions` já
  tem workers em segundo plano.

**Decisões (aceitas na [ADR-0010](adr/0010-reajuste-das-recorrencias.md), RJT-1; ficou a recomendação de cada uma, com os refinamentos da RJT-1):**
1. **Modelo do valor.** Opções: (a) **valor com vigência**: a regra guarda uma lista de valores, cada um
   válido a partir de um período; o reajuste cria uma vigência nova; (b) continuar editando o valor único.
   *Recomendação: (a)*, no mesmo padrão do limite dos orçamentos. A vigência de um período já fechado não
   muda o lançamento feito (só o estimado dos períodos em aberto).
2. **Como a regra reajusta.** *Recomendação:* a regra ganha um **reajuste anual opcional**: mês de
   aniversário (padrão: o mês do início), índice (`IPCA`, `INPC`, `IGPM`) ou percentual fixo, e "não
   reduzir" (ligado por padrão: índice negativo, como o IGP-M de 2023, mantém o valor). Sem reajuste
   configurado, tudo continua como hoje.
3. **Quem aplica o reajuste.** Opções: (a) o sistema aplica sozinho no aniversário; (b) o sistema
   **sugere** e o usuário confirma (ou digita outro valor). *Recomendação: (b).* Contratos usam defasagens
   diferentes, arredondam e às vezes são renegociados; aplicar sozinho mudaria a previsão com um valor que
   o usuário não reconhece. Confirmar cria a vigência nova.
4. **Fonte dos índices.** Opções: (a) **API do Banco Central (SGS)**, lida por um job diário no
   `transactions`, que guarda a variação mensal de cada índice; (b) o usuário digita o percentual no
   aniversário; (c) tabela mantida à mão no repositório. *Recomendação: (a) com (b) sempre disponível.* O job
   é desligável (`Indexes:Fetch:Enabled`), tolera a fonte fora do ar (tenta no dia seguinte) e nunca
   bloqueia nada: sem o índice do período, a sugestão fica "aguardando o índice" e o usuário pode informar o
   percentual. É a primeira saída para a internet, então a ADR registra o host liberado
   (`api.bcb.gov.br`) e que nenhum dado do usuário vai na chamada.
5. **Qual período do índice.** *Recomendação:* acumulado dos 12 meses que terminam no **mês anterior** ao
   aniversário (o mais comum nos contratos de aluguel); defasagem de 2 meses fica como Could. O acumulado é
   o produto das variações mensais, e o valor novo é arredondado a centavos (meio para cima).
6. **Na previsão.** *Recomendação:* do aniversário em diante, sem reajuste confirmado, a previsão usa o
   valor **estimado** com o último acumulado de 12 meses conhecido (ou o percentual fixo), e marca a parte
   estimada. O `RecurrenceSaved` passa à **v2**, com a lista de vigências e o reajuste configurado (com a
   taxa estimada calculada pelo `transactions`); o `reporting` lê v1 (uma vigência só, sem reajuste) e v2.
   Implantar o `reporting` antes do `transactions`.
7. **Onde o valor vigente vale.** *Recomendação:* em tudo o que hoje usa o valor da regra: lançamento
   pré-preenchido, "a lançar", casamento da importação, aviso de saída recorrente, previsão e saldo
   projetado.
8. **Aviso de reajuste.** *Recomendação (Could):* no mês do aniversário, um aviso novo no sino e no resumo
   por e-mail (NTF): "Reajuste do aluguel: R$ 2.000,00 → R$ 2.084,00 (IPCA 4,2%)", levando à regra para
   confirmar.

- ✅ **RJT-1** ADR: reajuste das recorrências. → [ADR-0010](adr/0010-reajuste-das-recorrencias.md), Aceita.
  - *Dado* as decisões em aberto 1 a 8 deste épico, *quando* o dono do produto revisa as opções,
    *então* a ADR-0010 é registrada como **Aceita** em `docs/adr/` (com o índice atualizado) e o PO
    ajusta as histórias seguintes ao que foi decidido.
  - Refinamentos registrados na ADR além das recomendações: vigências em `recurrence_amounts` por
    **período** (como a identidade da ocorrência), a primeira sempre existe, a nova começa num período da
    agenda, depois do primeiro e ainda não fechado (400 `recurrence.amount_period_invalid`); rotas
    `POST/DELETE /transactions/recurrences/{id}/amounts`; o `amount` do `PUT` da regra passa a corrigir a
    **vigência atual** (não cria vigência). Reajuste aplicado por
    `POST .../adjustments` com `{ year, rate }` ou `{ year, amount }` (409
    `recurrence.adjustment_already_applied`), sobre o valor vigente antes do aniversário, meio para cima.
    A leitura da regra traz `pendingAdjustment` do aniversário até 11 meses depois (com `waitingFor` quando
    falta mês de índice). Índices em `economic_indexes`, `IndexFetchWorker` diário com
    `Indexes:Fetch:Enabled` (padrão desligado; o compose liga) e host em configuração. `RecurrenceSaved` v2
    com `amounts` e `adjustment` (inclui `estimatedRate`), republicado quando sai mês novo do índice; a
    previsão estima só o próximo aniversário (`estimatedRecurring`). Aviso `RecurrenceAdjustment` com chave
    `adjustment:{regra}:{período}`.
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **RJT-2** Valor com vigência na regra. → `recurrence_amounts`, `POST/DELETE .../amounts`, `RecurrenceSaved` v2.
  - *Dado* um aluguel de R$ 2.000 desde janeiro, *quando* informo R$ 2.100 a partir de março, *então*
    janeiro e fevereiro continuam R$ 2.000 (em aberto ou lançados) e março em diante passam a R$ 2.100; *e*
    a vigência não pode começar antes do início da regra nem num período já fechado.
  - *Dado* períodos com valores diferentes, *quando* lanço uma ocorrência, consulto "a lançar" ou importo um
    extrato, *então* o valor pré-preenchido, o da lista e o do casamento são os do período.
  - *Dado* uma vigência nova, *quando* ela é gravada, *então* um `RecurrenceSaved` v2 sai no outbox com a
    lista de vigências.
  - Feito assim: a coluna `amount` da regra continua sendo o valor da **primeira vigência** (sem migração de
    dados) e as seguintes ficam em `recurrence_amounts` (regra + período, migração `RecurrenceAmounts`);
    `AmountFor(período)` devolve a última com início até ele. `POST /transactions/recurrences/{id}/amounts`
    (`{ fromPeriod, amount, version }`) cria ou troca a vigência (período fora da agenda, o primeiro ou já
    fechado = 400 `recurrence.amount_period_invalid`; versão antiga = 409); `DELETE .../amounts/{período}?version=`
    tira uma que ainda não começou (400 `recurrence.amount_already_started`, 404 `recurrence.amount_not_found`).
    O `amount` do `PUT` passa a corrigir a vigência de hoje; mudar início, fim ou frequência que deixe uma
    vigência fora da agenda = 400 `recurrence.amounts_outside_schedule`. O DTO traz `amount` (vigente hoje),
    `amounts` e o valor da próxima ocorrência. Lançar sem valor, "a lançar" e o casamento da importação usam o
    valor do período. `RecurrenceSaved` v2 com `amounts` (e `amount` = vigente no dia do evento).
  - Estimativa: 5 · **Must** · Dependências: RJT-1
- ✅ **RJT-3** Previsão usa o valor vigente.
  - Feito assim: o `ProjectionProcessor` lê o `RecurrenceSaved` v1 e v2 (v1 vira uma vigência só, no período
    do início) e guarda as vigências em `projected_recurrence_amounts` (migration
    `ProjectedRecurrenceAmounts`), trocadas inteiras a cada evento. A previsão (caixa, conta e fatura do
    cartão) e o aviso `RecurrenceDue` usam `AmountFor(período)` de cada ocorrência.
  - *Dado* um aluguel de R$ 2.000 com R$ 2.100 a partir de março, *quando* consulto a previsão de 6 meses
    em janeiro, *então* janeiro e fevereiro têm R$ 2.000 e março em diante R$ 2.100, inclusive no saldo
    projetado e no aviso de saída recorrente.
  - *Dado* um `RecurrenceSaved` v1 antigo no feed, *quando* o reporting o lê, *então* ele vira uma regra
    com uma vigência só.
  - Estimativa: 3 · **Must** · Dependências: RJT-2
- ✅ **RJT-4** Reajuste anual configurado e aplicado à mão. → `adjustment` na regra, `POST .../adjustments`.
  - Feito assim: o corpo da regra (criar e `PUT`) ganha `adjustment` (`{ anniversaryMonth?, kind: Index|Fixed,
    index?: IPCA|INPC|IGPM, rate?, noReduction? }`; nulo = sem reajuste; mês padrão = o do início, na anual
    tem de ser o dela; "não reduzir" padrão ligado), guardado em colunas `adjustment_*` da regra; erros
    `recurrence.adjustment_kind_invalid`, `_index_invalid`, `_month_invalid`, `_rate_invalid` (acima de
    -100%, até 1000%, 4 casas). `POST /transactions/recurrences/{id}/adjustments` (`{ year, rate | amount,
    version }`) cria a vigência no período do aniversário sobre o valor vigente antes dele (meio para cima;
    "não reduzir" vale para o percentual, o valor informado vale como está) e registra o aniversário em
    `recurrence_adjustments` (migration `RecurrenceAdjustments`): de novo = 409
    `recurrence.adjustment_already_applied`; aniversário no início da regra, antes ou depois dela = 400
    `recurrence.adjustment_year_invalid`; um e só um de `rate`/`amount` (400 `_value_required`); regra sem
    reajuste = 400 `_not_configured`. Remover a vigência do aniversário libera aplicá-lo de novo. A leitura
    traz `adjustment` e `pendingAdjustment` (`{ year, period, currentAmount, rate, suggestedAmount }`, do mês
    do aniversário até 11 meses depois; sugestão só no percentual fixo até a RJT-7). `RecurrenceSaved` v2
    leva `adjustment` com `estimatedRate` (o percentual fixo; o do índice vem na RJT-8). Smoke passo 24b
    (sugere, aplica, 409; 48 passos).
  - *Dado* uma regra, *quando* configuro reajuste anual em março por percentual fixo de 5% (ou por índice),
    *então* a regra guarda o aniversário, o índice ou percentual e o "não reduzir".
  - *Dado* o mês do aniversário, *quando* confirmo o reajuste informando o percentual ou o valor novo,
    *então* nasce a vigência a partir daquele período, com o valor arredondado a centavos; confirmar duas
    vezes o mesmo aniversário responde 409.
  - Estimativa: 3 · **Must** · Dependências: RJT-2
- ✅ **RJT-5** Vigências e reajuste na SPA. → `RecurrenceValuesRow`, `recurrence-values.cy.ts`.
  - Feito assim: o formulário da regra ganha "Sem reajuste anual / por percentual / pelo índice", com o mês
    (padrão o do início; travado na anual), o percentual ou o índice (IPCA, INPC, IGP-M) e "não reduzir";
    editar traz o reajuste e o manda de volta no `PUT` (antes a edição o apagava). Na lista, a regra mostra
    o reajuste configurado e, quando há, o link "reajuste de <mês> pendente". O link "valores" abre uma
    linha com as vigências ("desde … (início)", "a partir de …"; as que ainda não começaram podem ser
    removidas), o formulário de valor novo a partir de um mês (ou ano, na anual) e, com reajuste pendente,
    o resumo "R$ X → R$ Y (Z%)" e a confirmação pelo percentual ou pelo valor novo. Lançar a próxima usa o
    valor do período dela. E2E: cadastra com 5% no mês corrente, confirma o pendente (1.000 → 1.050), põe e
    tira um valor futuro e edita mantendo o reajuste.
  - *Dado* a aba Recorrências, *quando* abro uma regra, *então* vejo as vigências (valor e "a partir de"),
    configuro o reajuste anual e, no mês do aniversário, confirmo o reajuste com o percentual ou o valor
    novo.
  - Estimativa: 5 · **Must** · Dependências: RJT-4
- ✅ **RJT-6** Índices oficiais do Banco Central. → `IndexFetchWorker`, `economic_indexes`.
  - Feito assim: `FetchEconomicIndexesJob` pede ao SGS (`BcbSgsIndexSource`, séries 433/188/189,
    `dataInicial`/`dataFinal`; 404 = sem dados) os meses de cada índice desde o último guardado (ou
    `Indexes:Fetch:Since`, padrão 24 meses atrás) até hoje e grava os novos em `economic_indexes`
    (migration `EconomicIndexes`, chave índice + mês). A falha de um índice (rede, HTTP, resposta fora
    do formato) descarta o que ficou pela metade, vai para o log e não impede os outros. O
    `IndexFetchWorker` roda 30 s depois da subida e a cada 24 h, só com `Indexes:Fetch:Enabled`
    (padrão desligado; o compose liga com `INDEXES_FETCH_ENABLED`, host em `INDEXES_FETCH_BASE_URL`).
    Conferido no Docker: 24 meses de cada índice gravados da API real; com host inválido, um aviso por
    índice e o serviço segue de pé. A republicação das regras quando chega mês novo fica com a RJT-8,
    que é quem usa a taxa estimada.
  - *Dado* o job diário ligado, *quando* ele roda, *então* busca na API do SGS as variações mensais do
    IPCA, INPC e IGP-M que ainda não tem e as guarda (mês, índice, variação); *e* com a fonte fora do ar,
    registra no log e tenta no dia seguinte, sem afetar o resto.
  - *Dado* o job desligado, *quando* o sistema roda, *então* nada sai para a internet e o reajuste
    continua manual.
  - Estimativa: 5 · **Should** · Dependências: RJT-1
- ✅ **RJT-7** Reajuste sugerido pelo índice. → `pendingAdjustment` com o acumulado ou `waitingFor`.
  - Feito assim: no reajuste pelo índice, o `pendingAdjustment` usa os 12 meses que terminam no mês
    anterior ao aniversário (`RecurrenceAdjustment.IndexWindow`), lidos de `economic_indexes`
    (`PendingAdjustmentRates` carrega só os índices que as regras usam, dos últimos 24 meses): `rate` =
    `Π(1 + r/100) − 1` em % com 4 casas (aceito de volta pelo `POST .../adjustments`) e `suggestedAmount`
    com o arredondamento e o "não reduzir" do reajuste (acumulado negativo com "não reduzir" = valor
    atual). Faltando algum mês, `rate`/`suggestedAmount` vêm nulos e `waitingFor` traz o mês mais recente
    que falta; o reajuste continua confirmável com o percentual digitado. Na SPA, o resumo mostra
    "R$ X → R$ Y (IPCA Z%)" ou "aguardando o IPCA de <mês>; informe o percentual ou o valor novo", e o
    percentual sugerido vem preenchido. Smoke passo 24c (49 passos); conferido com o IPCA real
    (nov/2025–out/2026 = 4,6284%). E2E em `recurrence-values.cy.ts` confirma pelo índice com percentual
    digitado (o resultado do job varia).
  - *Dado* um aluguel com reajuste por IGP-M em março e as variações de março do ano anterior a fevereiro
    deste ano guardadas, *quando* abro a regra em março, *então* vejo o valor sugerido (acumulado de 12
    meses, 2 casas) e confirmo com um clique; *e* com acumulado negativo e "não reduzir", a sugestão é o
    valor atual.
  - *Dado* que o índice do último mês ainda não saiu, *quando* abro a regra, *então* vejo "aguardando o
    índice de fevereiro" e posso informar o percentual à mão.
  - Estimativa: 3 · **Should** · Dependências: RJT-4, RJT-6
- ✅ **RJT-8** Previsão estima o reajuste futuro. → `estimatedRate` no evento, `estimatedRecurring` na previsão.
  - Feito assim: o `transactions` põe no `RecurrenceSaved` v2 a `estimatedRate` (percentual fixo, ou o
    último acumulado de 12 meses seguidos do índice; sem eles, nula) ao criar, editar, mudar vigência e
    aplicar reajuste; quando o job grava mês novo de um índice, republica no mesmo commit as regras que
    reajustam por ele (escopo pelo `/internal/account-scopes` com token de serviço; regra sem escopo
    conhecido fica de fora; falha = nada gravado, nova tentativa no dia seguinte). O `reporting` guarda
    mês, taxa estimada e "não reduzir" (migration `ProjectedRecurrenceAdjustment`) e estima um reajuste
    só: o do aniversário mais recente sem vigência (pendente) ou, se já aplicado, o próximo; nunca o do
    período do início. Do aniversário em diante, até começar uma vigência posterior, a ocorrência usa o
    valor vigente antes dele reajustado pela taxa (centavos, meio para cima, "não reduzir"), e o fluxo do
    mês traz `estimatedRecurring` (quanto disso é reajuste estimado). A Previsão mostra "inclui R$ X de
    reajuste estimado" no resultado do mês. A jornada da RJT-10 passou a conferir o estimado antes da
    confirmação (R$ 1.050 marcado). Conferido no Docker que a consulta das regras por índice roda no MySQL.
  - *Dado* um aluguel com reajuste por IPCA no mês que vem ainda não confirmado, *quando* consulto a
    previsão de 12 meses, *então* do mês que vem em diante o valor é o estimado pelo último acumulado de 12
    meses conhecido, e a tela mostra a parte estimada.
  - Estimativa: 3 · **Should** · Dependências: RJT-3, RJT-6
- ✅ **RJT-9** Aviso de reajuste pendente. → aviso `RecurrenceAdjustment` no sino e no resumo por e-mail.
  - Feito assim: o `reporting` passa a guardar também o índice do reajuste (migration
    `ProjectedRecurrenceAdjustmentIndex`) e o `ReminderCalculator` avisa o aniversário mais recente (do
    mês dele até 11 meses depois) que ainda não virou vigência, dentro da regra e depois do período do
    início (mesma regra do reajuste pendente da previsão, `RecurrenceAmounts.Pending`), com chave
    `adjustment:{regra}:{período}`, valor atual (`previousAmount`), valor estimado pela taxa do evento
    (`amount`), `rate` e `index`; sem taxa, `rate` nulo. Vale para gasto e receita e segue a preferência
    "recorrências" dos avisos; dispensar funciona como nos outros. O sino mostra "R$ 2.000,00 → R$ 2.084,00
    (IGP-M 4,2%)" ou "aguardando o IPCA: confirme na recorrência", e o atalho abre a linha "valores" da
    regra; o e-mail traz a mesma linha. Confirmar cria a vigência, e o aviso some. A jornada da RJT-10
    confirma o reajuste a partir do aviso.
  - *Dado* uma regra com reajuste no mês corrente ainda não confirmado, *quando* olho o sino (ou recebo o
    resumo por e-mail), *então* vejo "Reajuste de <regra>: R$ X → R$ Y (<índice> Z%)", que leva à regra; *e*
    confirmar tira o aviso.
  - Estimativa: 2 · **Could** · Dependências: RJT-7
- ✅ **RJT-10** E2E Cypress: vigência, reajuste e previsão. → `recurrence-adjustment-journey.cy.ts`.
  - Feito assim: o reajuste só fica pendente (e confirmável) a partir do mês do aniversário (ADR-0010,
    decisão 5), então a jornada usa um aluguel de R$ 1.000 que começou há um ano (dia 1) com reajuste de 5%
    no mês corrente e lê a previsão nos meses seguintes, por diferença (usuário demo compartilhado): sem
    confirmar, o mês que vem tem +1.000; confirmado o pendente pela tela, +1.050; com um valor renegociado
    de R$ 1.100 a partir de daqui a dois meses, o mês que vem fica +1.050 e o seguinte +1.100. "Lançar" a
    próxima vem com R$ 1.050. Fecha o Must do épico.
  - *Dado* um aluguel cadastrado, *quando* configuro reajuste anual por percentual fixo e confirmo o
    reajuste a partir do mês que vem pela SPA, *então* a previsão mostra o valor antigo neste mês e o novo
    nos seguintes, e o lançamento do mês que vem vem pré-preenchido com o valor novo.
  - Estimativa: 3 · **Must** · Dependências: RJT-5, RJT-3

**Fora do escopo (Won't, nesta versão):** reajuste aplicado sem confirmação; índices além de IPCA, INPC e
IGP-M (IGP-DI, IPC-Fipe, IVAR); defasagem do índice diferente de 1 mês (fica Could da ADR); reajuste em
outra periodicidade que não a anual; reajuste de lançamento comum ou de compra parcelada; histórico de
índices na tela; recalcular lançamentos já feitos.

**Priorização (MoSCoW):** Must = RJT-1 a RJT-5 e RJT-10 (21 pts): valor com vigência, reajuste
configurado e confirmado à mão, previsão com o valor certo de cada período, provados ponta a ponta. Should
= RJT-6 a RJT-8 (11 pts): índices oficiais, sugestão pelo índice e a previsão estimando o reajuste que
ainda não veio. Could = RJT-9 (2 pts). Total: 34 pts.

**Ordem sugerida:** RJT-1 → RJT-2 → RJT-3 (com o `reporting` implantado antes do `transactions` que publica
o `RecurrenceSaved` v2) → RJT-4 → RJT-5 → RJT-10 (fecha o Must) → RJT-6 → RJT-7 → RJT-8 → RJT-9.

## 🟢 EPIC RCN — Conciliação da fatura do cartão  · *Done*
**Problema:** a fatura do cartão é calculada pelo app a partir do que foi lançado (INV): compras,
parcelas, estornos e pagamentos, no ciclo do cartão. A fatura do banco quase nunca bate de primeira:
falta uma compra que ninguém lançou, uma compra foi lançada duas vezes ou caiu no ciclo errado, aparece
IOF, juros ou anuidade, e o usuário só percebe quando paga um valor diferente do previsto. Hoje não há
como comparar as duas: o usuário confere linha a linha olhando duas telas, e importar a fatura do banco
para "completar" cria compras novas para parcelas que o app já tinha.
**Objetivo (outcome):** o usuário confere a fatura do app com a do banco, pelo total ou pelo arquivo da
fatura, e vê exatamente o que difere: o que falta lançar, o que está a mais no app e o que tem valor
diferente. Resolve na mesma tela (importa o que falta, corrige ou exclui o que sobra) e marca a fatura
como conferida.
**Métricas de sucesso:** (1) toda linha do arquivo do banco termina num grupo (conferida, só no banco ou
valor diferente) e todo item da fatura do app termina conferido ou "só no app"; (2) parcela de uma compra
já lançada nunca vira compra nova ao importar a fatura do banco; (3) quando a diferença vem só de compras
não lançadas, importar o que falta zera a diferença; (4) a jornada "enviar a fatura do banco, importar o
que falta, marcar conferida" passa no E2E do CI.
**Rastreabilidade:** `docs/PROMPT.md` → controle do cartão de crédito; Won't do épico IMP ("conciliação de
fatura de cartão fica com o épico de fechamento de fatura") e do épico INV ("conciliação automática do
total calculado com o total da fatura importada do banco"); ADR-0005 (consequências: "conciliar os dois
fica fora do épico"); ideia de produto promovida a pedido do dono do produto (2026-11-26).

**Achados no código que moldam o épico:**
- **A fatura calculada vive no `reporting`.** `InvoiceCalculator` monta as faturas de um cartão a partir
  da projeção (compras, parcelas, estornos e pagamentos) com o `CardCycle` lido do `accounts`. Cada
  `InvoiceItem` traz o lançamento (`TransactionId`), a data da compra, o valor, a parcela (`n` de `m`) e a
  descrição. `GET /reports/cards/{id}/invoices` já devolve isso para a tela.
- **O arquivo da fatura vive no `transactions`.** A importação (IMP, ADR-0004) já lê OFX e CSV de cartão
  (`StatementKind.CreditCard`), guarda a prévia em `import_batches`/`import_lines` e confirma com decisões
  por linha (incluir, categoria, meio, pagamento de fatura). Créditos na fatura (pagamento, estorno) já
  saem "fora por padrão", e o pagamento vira `CardPayment` (INV-11).
- **O casamento da importação é só pela chave do arquivo.** Uma linha é "duplicada" quando a chave externa
  (FITID, ou a impressão digital da linha) já está num lançamento da conta. Compra lançada à mão não tem
  chave, então nunca casa com a linha do banco. A linha duplicada também não informa qual lançamento já
  existe.
- **Parcelas não são reconhecidas na importação.** A fatura do banco traz "LOJA PARCELA 03/10" com uma
  chave nova a cada mês. Para a importação, é uma compra nova de 1 parcela, que se soma à parcela 3 que o
  app já projeta para aquela fatura: o total dobra.
- **O total declarado pelo banco é ignorado.** O `OfxReader` lê as transações do `CCSTMTRS`, mas não o
  saldo (`LEDGERBAL`) nem o período (`DTSTART`/`DTEND`). CSV de fatura não tem total.
- **A fatura não tem estado guardado.** O status (aberta, fechada, paga, vencida) é calculado a cada
  consulta; não há onde registrar "conferida".
- **Dependências entre serviços hoje:** o `reporting` lê o `transactions` pelo feed de eventos e chama o
  `accounts` por HTTP com o token do usuário; o `transactions` chama o `accounts`. Nenhum serviço chama o
  `reporting`.

**Decisões (aceitas na [ADR-0011](adr/0011-conciliacao-da-fatura-do-cartao.md), RCN-1; ficou a recomendação de cada uma, com os refinamentos da RCN-1):**
1. **Onde a conciliação é calculada.** Opções: (a) no **`reporting`**, dono da fatura calculada, lendo as
   linhas do lote no `transactions` por HTTP com o token do usuário (mesma direção da dependência de hoje,
   via feed); (b) no `transactions`, dono do arquivo, recalculando a fatura (duplicaria ciclo, parcelas e
   estornos da ADR-0005); (c) na SPA, juntando as duas respostas (regra de negócio no cliente).
   *Recomendação: (a).* O `transactions` continua dono da importação e da confirmação; o `reporting` só
   lê o lote e devolve o resultado.
2. **O lado do banco.** Opções: (a) só o **total** digitado pelo usuário (do PDF ou do app do banco); (b)
   o **arquivo** da fatura (OFX/CSV, já suportados) para o detalhe linha a linha; (c) os dois.
   *Recomendação: (c).* O total digitado é o mínimo e serve para quem não tem arquivo; com o arquivo, o
   total do banco é o `LEDGERBAL` do OFX quando existe, senão a soma das linhas sem os pagamentos.
3. **Como uma linha do banco casa com um item do app**, nesta ordem, cada item casando com uma linha só:
   (i) **mesma chave externa** (a linha já foi importada antes: o `transactions` passa a informar o
   lançamento que ela duplica); (ii) **parcela**: descrição com "n/m" (ou "PARC n/m", "PARCELA n DE m") e
   valor igual ao da parcela `n` de um item com `m` parcelas naquela fatura; (iii) **mesmo valor** (ao
   centavo) e **data da compra até 3 dias** de diferença, a mais próxima primeiro, com a descrição parecida
   como desempate. *Recomendação: essa ordem*, sem casamento por valor aproximado (o "valor diferente" só
   sai de (i) e (ii), em que o par é certo).
4. **O resultado.** *Recomendação:* quatro grupos: **conferido** (casou e o valor bate), **valor
   diferente** (casou por chave ou parcela, valor difere), **só no banco** (falta lançar) e **só no app**
   (lançado, mas não está na fatura do banco: data no ciclo errado, outro cartão, lançamento em dobro).
   Mais os totais: do banco, do app, a diferença e quanto dela cada grupo explica. Pagamentos ficam fora
   da comparação (já são tratados na INV-11).
5. **Como resolver.** *Recomendação:* **só no banco** → importar pelo próprio lote, que já está na prévia:
   a tela deixa incluídas só essas linhas, e as casadas ficam de fora como "já lançadas" (nunca viram
   compra nova, inclusive as parcelas); **só no app** → atalho para editar (LNC-4) ou excluir o lançamento;
   **valor diferente** → atalho para editar. Encargos do banco (IOF, juros, multa, anuidade, tarifa) são
   reconhecidos pela descrição e vêm com a categoria "Encargos do cartão" sugerida.
6. **Qual fatura é o arquivo.** *Recomendação:* o `reporting` sugere o mês de vencimento cujo período
   (início ao fechamento) contém mais datas de compra das linhas que não são parcela, ou o do `DTEND` do OFX
   quando existe; o usuário troca se quiser.
7. **Conferida.** *Recomendação:* o usuário marca a fatura como conferida; o `reporting` guarda cartão,
   mês de vencimento, total do banco, total do app no momento e a data (tabela `invoice_reconciliations`).
   A fatura mostra o selo "conferida" e, se o total do app mudar depois, "alterada depois da conferência".
   Desfazer apaga a marca. Conferir com diferença é permitido (o usuário pode aceitar o IOF sem lançar),
   e o selo mostra a diferença aceita.
8. **Rotas.** *Recomendação:* no `reporting`, `GET /reports/cards/{id}/invoices/{dueMonth}/reconciliation`
   (com `?batchId=` para o arquivo, sem ele só o total guardado) e `PUT`/`DELETE` na mesma rota para marcar
   e desfazer a conferência (`{ bankTotal }`). No `transactions`, o lote de cartão passa a trazer o total e
   o período declarados no OFX, e a linha duplicada, o lançamento que ela repete. Nada novo no gateway (as
   rotas cabem em `/api/reports/**` e `/api/transactions/**`) nem no `identity` (o `reporting` usa o token
   do próprio usuário para ler o lote).

- ✅ **RCN-1** ADR: conciliação da fatura do cartão. → [ADR-0011](adr/0011-conciliacao-da-fatura-do-cartao.md), Aceita.
  - *Dado* as decisões em aberto 1 a 8 deste épico, *quando* o dono do produto revisa as opções, *então* a
    ADR-0011 é registrada como **Aceita** em `docs/adr/` (com o índice atualizado) e o PO ajusta as
    histórias seguintes ao que foi decidido.
  - Refinamentos registrados na ADR além das recomendações: o `reporting` lê o lote em
    `GET /transactions/imports/{id}` repassando o token do usuário (os três serviços aceitam o mesmo token)
    pelo `Services:TransactionsBaseUrl` que já existe; lote de outro dono = 404
    `reconciliation.batch_not_found`, de outro cartão = 400 `reconciliation.batch_card_mismatch`. Com OFX,
    a tela vem com o `LEDGERBAL` como total do banco; sem ele, a soma das linhas sem os pagamentos.
    Parcela casa sem olhar a data da linha (desempate pela descrição e pela data da compra); "descrição
    parecida" = mais palavras em comum depois de normalizar. Estornos lançados entram na comparação; os
    créditos fora por padrão, não. Linha "n/m" sem compra no app entra como compra de 1 parcela; "ANUIDADE
    3/12" é encargo. Campos novos no lote: `statedTotal`, `statementStart`, `statementEnd` e, na linha
    duplicada, `duplicateOf`. `GET /reports/cards/{id}/invoices` passa a trazer a conferência.
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **RCN-2** Conferir pelo total do banco. → `GET/PUT/DELETE /reports/cards/{id}/invoices/{dueMonth}/reconciliation`.
  - Feito assim: `InvoiceReconciliation` no `reporting` (tabela `invoice_reconciliations`, chave dono + cartão +
    mês de vencimento, migration `InvoiceReconciliations`) guarda o total do banco, o total do app na hora e a
    data; a diferença é banco − app. O `GET` devolve o total do app agora (fatura calculada; mês sem compra = 0),
    o total do banco e a diferença contra o total de agora, e a conferência guardada. O `PUT {bankTotal}`
    marca (ou substitui) a conferência; total ausente, com mais de 2 casas ou acima de 1 bilhão = 400
    `reconciliation.bank_total_invalid`. O `DELETE` desfaz (204; sem conferência = 404
    `reconciliation.not_found`). Cartão que o accounts não devolve ao dono = 404 `card.not_found`; mês fora de
    `yyyy-MM` = 400 `invoice.month_invalid`. `GET /reports/cards/{id}/invoices` traz a `reconciliation` de cada
    fatura. Smoke passo 24e (51 passos).
  - *Dado* a fatura de dezembro calculada em R$ 3.180,00, *quando* informo que a do banco é R$ 3.214,50,
    *então* vejo a diferença de R$ 34,50 e posso marcar a fatura como conferida com essa diferença aceita.
  - *Dado* uma fatura conferida, *quando* consulto as faturas do cartão, *então* ela traz a conferência
    (total do banco, total do app na hora, data); *e* desfazer apaga a marca.
  - Estimativa: 3 · **Must** · Dependências: RCN-1
- ✅ **RCN-3** Total e período do arquivo da fatura. → `statedTotal`, `statementStart`/`statementEnd`, `duplicateOf`.
  - Feito assim: o `OfxReader` lê, só na fatura de cartão, o `LEDGERBAL/BALAMT` (sem sinal: o saldo do
    cartão vem negativo na maioria dos bancos) e o `DTSTART`/`DTEND` da lista de lançamentos; valor ou data
    ilegível fica ausente, sem derrubar o arquivo (`StatementTotals`). O lote de cartão guarda os três
    (`stated_total`, `statement_start`, `statement_end`; extrato de conta ignora) e o `ImportBatchDto` os
    devolve. Na prévia de fatura de cartão, a linha duplicada traz `duplicateOf` (o lançamento da conta com a
    mesma chave, por `FindTransactionIdsByExternalIdsAsync`; coluna `duplicate_of` em `import_lines`, migration
    `ImportStatementTotals`). Smoke passo 24d (fatura OFX: total, período e `duplicateOf`; 50 passos).
  - *Dado* um OFX de fatura com `LEDGERBAL` e `DTSTART`/`DTEND`, *quando* importo, *então* o lote guarda
    o total e o período declarados pelo banco (CSV fica sem eles).
  - *Dado* uma linha do arquivo que repete um lançamento já importado, *quando* vejo a prévia, *então* a
    linha duplicada informa qual lançamento ela repete.
  - Estimativa: 3 · **Must** · Dependências: RCN-1
- ✅ **RCN-4** Conciliação linha a linha. → `GET .../reconciliation?batchId=`, `InvoiceMatcher`.
  - Feito assim: com `batchId`, o `reporting` lê o lote em `GET /imports/{id}` do `transactions` (a rota é
    `/imports`, não `/transactions/imports` como a ADR escreveu; no gateway, `/api/imports`) repassando o token
    do usuário (`TransactionsImportBatchReader`); lote de outro dono = 404 `reconciliation.batch_not_found`,
    extrato de conta ou fatura de outro cartão = 400 `reconciliation.batch_card_mismatch`. Entram as linhas
    válidas; crédito com cara de pagamento ("PAGAMENTO", "PAGTO", "PGTO") fica fora, e os outros créditos
    entram como estorno (valor negativo, casando com o estorno lançado no app). O `InvoiceMatcher` (função
    pura) casa na ordem da ADR: chave (`duplicateOf` ou o lançamento que a linha criou); parcela "n/m"
    ("PARCELA 03/10", "PARC 3/10", "PARC.03/12", "3 DE 10"; n ≤ m, m ≥ 2) com o mesmo valor ou, com valor
    diferente, só com descrição parecida; mesmo valor e até 3 dias, o mais perto primeiro. O resultado
    (`detail`) traz o total do arquivo (`statedTotal` ou a soma das linhas), a diferença para o app, a fatura
    sugerida (pelo `statementEnd`; sem ele, a que recebe mais compras que não são parcela), as entradas com
    grupo, regra, linha e item, e quanto cada grupo explica (com o `unexplained`, quando o total declarado
    difere da soma das linhas). Smoke: passo 24d confere a linha casada pela chave no Docker (52 passos).
  - *Dado* o arquivo da fatura de dezembro com 20 linhas e a fatura do app com 19 itens, *quando* concilio,
    *então* cada linha e cada item caem num grupo (conferido, valor diferente, só no banco, só no app), pela
    ordem de casamento da decisão 3, com os totais e a diferença explicada por grupo.
  - *Dado* uma compra em 10 parcelas já lançada, *quando* o arquivo traz "LOJA PARCELA 03/10" com o valor
    da parcela, *então* a linha casa com a parcela 3 da fatura (e não fica "só no banco").
  - *Dado* um arquivo sem fatura escolhida, *quando* concilio, *então* o app sugere a fatura pelas datas
    (ou pelo `DTEND`).
  - Estimativa: 5 · **Must** · Dependências: RCN-3
- ✅ **RCN-5** Resolver a diferença. → parcela já lançada vira duplicada na prévia; importar só o que falta.
  - Feito assim: na prévia de fatura de cartão, a linha "n/m" com o valor da parcela `n` de uma compra em `m`
    vezes já lançada no mesmo cartão (mesma divisão da fatura: arredondada para baixo, sobra na primeira;
    compra de até `n` meses e 45 dias antes da linha, ou até 3 dias depois; cada parcela de uma compra uma vez)
    fica **duplicada**, fora da importação e com `duplicateOf` = a compra (`InstallmentLines`,
    `ListInstallmentPurchasesAsync`). Vale também para quem importa a fatura sem conciliar, e a conciliação a
    casa pela chave. Importar só o que falta usa a confirmação que já existe: a tela manda incluídas só as
    linhas "só no banco" (as outras, `included: false`); os atalhos de editar e excluir ficam na RCN-6. Smoke
    passo 24f (parcela 02/03 duplicada apontando a compra; 53 passos).
  - *Dado* uma conciliação com 2 linhas só no banco, *quando* importo o que falta, *então* só essas 2
    viram lançamento no cartão; as casadas (inclusive as parcelas) ficam de fora como já lançadas, e a
    diferença da fatura cai para o que sobrou.
  - *Dado* um item só no app ou com valor diferente, *quando* uso o atalho, *então* chego à edição (ou à
    exclusão) daquele lançamento.
  - Estimativa: 3 · **Must** · Dependências: RCN-4
- ✅ **RCN-6** Conferir na SPA. → `InvoiceReconciliationPanel`, `invoice-reconciliation.cy.ts`.
  - Feito assim: na fatura (aba Cartões), "Conferir com o banco" abre o painel: total do banco (vem preenchido
    com o do app, ou com o do arquivo) e a diferença enquanto digita, "Marcar como conferida" (`PUT`) e
    "desfazer"; a fatura ganha o selo "Conferida" (com a diferença aceita, se houver). O arquivo é o OFX da
    fatura (CSV continua pela aba Importar, sem o painel): o painel cria o lote no cartão e mostra os totais
    (arquivo, app, diferença), o aviso de "o arquivo parece ser da fatura de …" com atalho, a parte não
    explicada e os grupos (só no banco, só no app, valor diferente, conferidas). "Importar o que falta"
    confirma o lote com só as linhas "só no banco" incluídas (categoria sugerida pelo histórico ou a digitada)
    e recarrega até a projeção casá-las. "Só no app" e "valor diferente" têm "editar" (abre o lançamento em
    edição na aba Lançamentos) e "excluir". E2E: conferir pelo total com R$ 10 de diferença, selo e desfazer.
  - *Dado* a aba Cartões, *quando* abro uma fatura e escolho "Conferir com o banco", *então* informo o total
    ou envio o arquivo, vejo os grupos e os totais, importo o que falta, uso os atalhos e marco como
    conferida; a fatura passa a mostrar o selo "conferida" (com a diferença aceita, se houver).
  - Estimativa: 5 · **Must** · Dependências: RCN-2, RCN-5
- ✅ **RCN-7** Encargos do cartão reconhecidos. → `CardChargeHints` nos dois serviços.
  - Feito assim: descrição com a palavra inteira IOF, JUROS, MULTA, MORA, ANUIDADE, TARIFA ou ENCARGO(S) é
    encargo. Na prévia de fatura de cartão, a saída que é encargo e não tem categoria no histórico vem com
    "Encargos do cartão" (o histórico continua valendo antes); encargo nunca é parcela de compra (nem na
    prévia, nem na conciliação: "ANUIDADE 3/12" fica só no banco). A conciliação marca a entrada (`charge`), e
    o painel mostra "encargo do banco"; a jornada da RCN-9 confere a marca no IOF.
  - *Dado* linhas "IOF COMPRA INTERNACIONAL", "JUROS ROTATIVO", "ANUIDADE 3/12" ou "TARIFA", *quando*
    aparecem só no banco, *então* vêm marcadas como encargo, com a categoria "Encargos do cartão" sugerida
    (e a "ANUIDADE 3/12" não é tomada por parcela de compra).
  - Estimativa: 2 · **Should** · Dependências: RCN-4
- ✅ **RCN-8** Alterada depois da conferência. → `changedSince` na conferência.
  - Feito assim: a conferência devolvida (na lista de faturas e na rota da conferência) traz `changedSince`
    quando o total do app agora difere do total guardado na conferência. A fatura troca o selo "Conferida" por
    "Alterada depois da conferência" (com o total da conferência e o de agora no título), e o painel diz quanto
    era e quanto é; conferir de novo atualiza a marca. E2E `invoice-reconciliation.cy.ts`: uma compra nova no
    ciclo depois da conferência deixa a fatura alterada (150 → 170).
  - *Dado* uma fatura conferida em R$ 3.214,50, *quando* entra um lançamento novo naquele ciclo, *então* a
    fatura mostra "alterada depois da conferência" com o total novo; *e* conferir de novo atualiza a marca.
  - Estimativa: 2 · **Should** · Dependências: RCN-2
- ✅ **RCN-9** E2E Cypress: conferir a fatura com o arquivo do banco. → `invoice-reconciliation-journey.cy.ts`.
  - Feito assim: cartão novo com notebook 300 em 3x, mercado 80 e padaria 25 lançados hoje; o OFX traz a
    parcela 01/03 (100), o supermercado (80), uma farmácia (39,40), um IOF (0,98) e o total declarado
    (220,38). A conciliação mostra 2 conferidas (parcela pela chave da prévia, mercado por valor e data),
    a padaria só no app e farmácia e IOF só no banco (diferença 15,38); "editar" abre a padaria na aba
    Lançamentos. Importar o que falta e excluir a padaria deixam 4 conferidas e diferença 0; marcar como
    conferida usa o total do arquivo e a fatura fica com o selo, sem diferença. Fecha o Must do épico.
  - *Dado* um cartão com uma compra parcelada e duas compras lançadas, *quando* envio o OFX da fatura com a
    parcela, uma das compras, uma compra que faltava e um IOF, *então* a conciliação mostra a parcela e a
    compra conferidas, a outra compra só no app e as duas linhas só no banco; *e* importar o que falta e
    excluir a compra a mais zera a diferença e a fatura fica conferida.
  - Estimativa: 3 · **Must** · Dependências: RCN-6

**Fora do escopo (Won't, nesta versão):** conexão direta com o banco (Open Finance); ler a fatura em PDF;
conciliar o extrato da conta corrente (só a fatura do cartão); ajustar o app sozinho para bater com o banco;
criar a compra parcelada (com as parcelas futuras) a partir de uma linha "n/m" que só existe no banco (ela
entra como compra de 1 parcela do valor da linha); calcular juros, multa ou rotativo; aviso de fatura
fechada ainda não conferida.

**Priorização (MoSCoW):** Must = RCN-1 a RCN-6 e RCN-9 (24 pts): conferir pelo total e pelo arquivo,
resolver na mesma tela e marcar conferida, provado ponta a ponta. Should = RCN-7 e RCN-8 (4 pts): encargos
reconhecidos e o selo de alterada depois da conferência. Total: 28 pts.

**Ordem sugerida:** RCN-1 → RCN-3 → RCN-2 → RCN-4 → RCN-5 → RCN-6 → RCN-9 (fecha o Must) → RCN-7 → RCN-8.

## 🟡 EPIC CYC — Editar o cartão e o ciclo da fatura  · *In Progress*
**Problema:** o cartão não tem edição nenhuma: nome, final, limite e ciclo (dia de fechamento e de
vencimento) são definidos ao criar, e para mudar qualquer um o usuário precisa criar outro cartão. O
outro cartão não traz as compras, as parcelas, os pagamentos nem as conferências do antigo, e a fatura
fica partida em dois. Mudar o vencimento é comum: o banco oferece a troca, o usuário alinha com o dia do
salário, e o limite sobe ou desce algumas vezes por ano.
**Objetivo (outcome):** o usuário corrige o nome, o final e o limite do cartão, e muda o fechamento e o
vencimento a partir de uma fatura, sem mexer nas faturas que já fecharam. As faturas antigas continuam
com as compras, os pagamentos e as conferências de sempre; a fatura da troca mostra o período maior ou
menor que o banco cobra; previsão, avisos, limite e conciliação passam a usar o ciclo novo dali em diante.
**Métricas de sucesso:** (1) depois de mudar o ciclo, nenhuma fatura já fechada muda de total, de itens,
de pagamento ou de conferência; (2) a fatura da troca vai do dia seguinte ao último fechamento antigo até
o primeiro fechamento novo, sem dia de compra sem fatura nem em duas; (3) mudar o limite muda o
disponível na hora; (4) a jornada "mudar o vencimento e conferir as faturas" passa no E2E do CI.
**Rastreabilidade:** `docs/PROMPT.md` → controle do cartão de crédito; Won't do épico INV ("editar o
ciclo do cartão (continua sendo criar outro cartão)"); ADR-0005, decisão 3 ("se o cartão ganhar edição do
ciclo, reabrir esta decisão e a regra 4"); ideia de produto promovida a pedido do dono do produto
(2026-11-26).

**Achados no código que moldam o épico:**
- **O cartão só é criado e desativado.** O `accounts` tem `POST /credit-cards` e `DELETE` (desativa); não
  há `PUT`. Nome, bandeira, final, limite, fechamento e vencimento são fixos.
- **A fatura é recalculada a cada consulta com o ciclo de hoje.** O `reporting` lê o cartão no `accounts`
  na hora (ADR-0005, decisão 3) e o `InvoiceCalculator` monta todas as faturas com um `CardCycle` só.
  Trocar o ciclo sem histórico reposicionaria todas as compras do passado: faturas pagas perderiam itens,
  faturas vencidas ganhariam outros.
- **Pagamentos, conferências e avisos são guardados pelo mês de vencimento** (`card_payments`,
  `invoice_reconciliations`, chave `invoice:{cartão}:{mês}` do NTF). Se a fatura de um mês muda de
  período, o pagamento e a conferência continuam no mesmo mês: o histórico precisa manter os períodos.
- **Quem usa o ciclo:** no `reporting`, faturas, limite disponível, previsão (compras e recorrências no
  cartão), avisos de vencimento (pelo token do usuário e pelo endpoint interno do resumo por e-mail) e a
  fatura sugerida na conciliação (RCN); no `transactions`, o mês de vencimento sugerido para o pagamento de
  fatura importado (só o dia de vencimento); na SPA, a mesma sugestão na tela de importação.
- **O `accounts` não publica eventos.** O ciclo chega a quem precisa pela leitura na hora; não há projeção
  para atualizar.

**Decisões (aceitas na [ADR-0012](adr/0012-editar-cartao-e-ciclo-da-fatura.md), CYC-1; ficou a recomendação de cada uma, com os refinamentos da CYC-1):**
1. **Como guardar a mudança de ciclo.** Opções: (a) **ciclo com vigência**: o cartão guarda uma lista de
   ciclos, cada um valendo a partir de um mês de vencimento; (b) trocar o ciclo e recalcular tudo
   (simples, mas reescreve o passado); (c) guardar as faturas fechadas (cópia dos períodos e dos totais) e
   calcular só as abertas. *Recomendação: (a)*, no mesmo padrão do limite dos orçamentos e do valor das
   recorrências. O primeiro ciclo vale desde sempre; o cartão continua expondo `closingDay`/`dueDay` (o
   ciclo de hoje) para quem só precisa dele.
2. **A partir de quando vale.** *Recomendação:* a partir de uma fatura (mês de vencimento) **ainda aberta**
   ou futura: a atual, se o fechamento novo dela ainda não passou, ou a seguinte. Fatura fechada nunca
   muda (400). Mudar de novo antes de valer substitui a mudança pendente.
3. **A fatura da troca.** *Recomendação:* os períodos continuam encadeados: a fatura que inaugura o ciclo
   novo começa no dia seguinte ao último fechamento do ciclo antigo e termina no primeiro fechamento do
   novo. Ela pode ficar mais curta ou mais longa que um mês; se o fechamento novo cair antes do último
   fechamento antigo (período vazio ou negativo), a troca é recusada para aquela fatura e o app sugere a
   seguinte. Uma compra sempre cai numa fatura só.
4. **Onde mora e como chega.** *Recomendação:* o histórico mora no `accounts` (tabela `card_cycles`) e
   vem junto do cartão (`cycles: [{ fromDueMonth, closingDay, dueDay }]`) nas rotas que o `reporting` já
   chama, com o token do usuário e no endpoint interno do resumo. Mantém a ADR-0005, decisão 3 (leitura na
   hora, sem evento nem projeção), e substitui a regra 4 dela pela do ciclo de cada período.
5. **Os outros campos.** *Recomendação:* `PUT /credit-cards/{id}` corrige nome, bandeira, final e
   limite, sem histórico (o limite novo vale para o disponível na hora). A conta do cartão não muda.
6. **Rotas.** *Recomendação:* `PUT /credit-cards/{id}` (dados) e `PUT /credit-cards/{id}/cycle`
   (`{ closingDay, dueDay, fromDueMonth }`), mais `DELETE /credit-cards/{id}/cycle/{fromDueMonth}` para
   desfazer uma troca que ainda não valeu. Nada novo no gateway (`/api/credit-cards/**` já passa) nem no
   `identity`.

- ✅ **CYC-1** ADR: editar o cartão e o ciclo da fatura. → [ADR-0012](adr/0012-editar-cartao-e-ciclo-da-fatura.md), Aceita.
  - *Dado* as decisões em aberto 1 a 6 deste épico, *quando* o dono do produto revisa as opções, *então* a
    ADR-0012 é registrada como **Aceita** em `docs/adr/` (com o índice atualizado), reabrindo a ADR-0005
    (decisão 3 e regra 4), e o PO ajusta as histórias seguintes ao que foi decidido.
  - Refinamentos registrados na ADR além das recomendações: o primeiro ciclo continua nas colunas do cartão
    (vale desde sempre) e as trocas vão para `card_cycles`; a fatura antiga que fecharia no mesmo dia ou
    depois do primeiro fechamento novo deixa de existir (o mês de vencimento pula, achado ao escrever os
    exemplos da CYC-3), então a da troca nunca fica vazia; a troca vale de um mês em que o primeiro
    fechamento novo não passou e a fatura substituída não fechou, senão 400 `card.cycle_change_invalid` com a
    próxima fatura possível; "hoje" no fuso do app, que o `accounts` passa a ler (`TimeZone`). Pendente
    = vigência cuja fatura ainda não começou (trocar de novo substitui; `DELETE` desfaz). O cartão sai com o
    ciclo de hoje em `closingDay`/`dueDay` e o histórico em `cycles` (o primeiro com `fromDueMonth` nulo),
    também no `/internal/credit-cards`. A regra dos períodos encadeados fica no `reporting` e, para validar,
    uma cópia no `accounts`, com os mesmos testes de exemplo. Cartão desativado não é editado (400
    `card.inactive`).
  - Estimativa: 2 · **Must** · Dependências: —
- ✅ **CYC-2** Editar nome, bandeira, final e limite. → `PUT /credit-cards/{id}`.
  - Feito assim: `PUT /credit-cards/{id}` (`{ label, brand, lastFourDigits, creditLimit }`) corrige os dados com as
    regras e os códigos da criação (`card.label_required`, `card.label_too_long`, `card.last_four_invalid`,
    `card.limit_negative`); a conta e o ciclo não mudam aqui. Cartão de outro dono = 404 `card.not_found`;
    desativado = 400 `card.inactive`. Sem histórico do limite: o `reporting` lê o cartão a cada consulta, então o
    disponível muda na hora. Smoke passo 24h (limite 5.000 → 8.000 sobe o disponível em 3.000; 55 passos).
  - *Dado* um cartão com limite de R$ 5.000, *quando* corrijo o nome e mudo o limite para R$ 8.000, *então*
    a lista e a fatura mostram o nome novo e o disponível sobe R$ 3.000 na hora.
  - *Dado* dados inválidos (final sem 4 dígitos, limite negativo, nome vazio), *quando* salvo, *então* recebo
    400 com as mesmas regras da criação.
  - Estimativa: 3 · **Should** · Dependências: —
- ✅ **CYC-3** Ciclo com vigência no `accounts`. → `card_cycles`, `PUT/DELETE /credit-cards/{id}/cycle`.
  - Feito assim: o ciclo da criação continua nas colunas do cartão e as trocas ficam em `card_cycles` (cartão +
    mês de vencimento de início, migration `CardCycles`). `CardCycleSchedule` (domínio) encadeia os períodos: a
    fatura antiga que fecharia no mesmo dia ou depois do primeiro fechamento novo não existe (o mês pula), e o
    início de cada fatura é o dia seguinte ao fechamento da anterior que existe; a ADR-0012 foi corrigida com
    essa regra, achada ao escrever os exemplos. `PUT /credit-cards/{id}/cycle` (`{ closingDay, dueDay,
    fromDueMonth }`) troca a partir de um mês em que o primeiro fechamento novo não passou e a fatura substituída
    não fechou (senão 400 `card.cycle_change_invalid`, com a próxima fatura possível), substituindo as trocas
    pendentes; `DELETE .../cycle/{yyyy-MM}` desfaz uma pendente (400 `card.cycle_already_started`, 404
    `card.cycle_not_found`); mês inválido = 400 `card.month_invalid`, dias fora de 1 a 28 = 400
    `card.day_invalid`, cartão desativado = 400 `card.inactive`. O cartão sai com o ciclo de hoje em
    `closingDay`/`dueDay` e o histórico em `cycles`, também no `/internal/credit-cards`. O `accounts` passa a ler
    `TimeZone` (compose: `APP_TIME_ZONE`). Smoke passo 24g (troca pendente e desfeita; 54 passos).
  - *Dado* um cartão que fecha dia 10 e vence dia 17, *quando* mudo para fechar dia 25 e vencer dia 5 a
    partir da fatura de março, *então* o cartão guarda os dois ciclos (o antigo desde sempre, o novo desde
    março) e o `GET` devolve `cycles` e o ciclo de hoje em `closingDay`/`dueDay`.
  - *Dado* uma fatura já fechada, *quando* tento mudar o ciclo a partir dela, *então* recebo 400; *e* mudar
    de novo antes de valer substitui a mudança pendente, que também pode ser desfeita.
  - Estimativa: 3 · **Must** · Dependências: CYC-1
- ✅ **CYC-4** Faturas pelo ciclo de cada período. → `CardCycleSchedule` no `reporting`.
  - Feito assim: o `CardInfo` lido do `accounts` traz `cycles` (sem eles, o ciclo de hoje vale desde sempre) e
    `CardInfo.Schedule()` monta a sequência de ciclos, com a mesma regra e os mesmos exemplos de teste do
    `accounts`. O `InvoiceCalculator` usa o ciclo de cada período: compra na fatura que existe e cujo período
    contém a data, e cada parcela na fatura seguinte que existe (pula o mês de vencimento que some). Faturas,
    limite, avisos (pelas faturas carregadas, também no resumo por e-mail), previsão (compras e recorrências no
    cartão) e a fatura sugerida na conciliação passam a usar a sequência. Pagamentos e conferências continuam
    no mês de vencimento deles. Smoke passo 24g: uma compra em 3x tem a 3ª parcela na fatura da troca, que fecha
    no dia 25 do ciclo novo.
  - *Dado* compras antes e depois da troca, *quando* consulto as faturas, *então* as fechadas continuam
    iguais (itens, total, pagamento, conferência), a da troca vai do dia seguinte ao último fechamento
    antigo até o primeiro fechamento novo, e as seguintes seguem o ciclo novo.
  - *Dado* o ciclo novo, *quando* consulto o limite, a previsão (com as recorrências no cartão), os avisos
    de vencimento (no app e no resumo por e-mail) e a fatura sugerida na conciliação, *então* todos usam o
    ciclo de cada período.
  - Estimativa: 5 · **Must** · Dependências: CYC-3
- ✅ **CYC-5** Editar o cartão e o ciclo na SPA. → `CardEditRow`, `card-edit.cy.ts`.
  - Feito assim: "editar" no cartão (aba Cartões) abre uma linha com os dados (nome, bandeira, final, limite) e a
    troca de ciclo (fecha dia, vence dia, a partir da fatura de). A fatura sugerida é a primeira possível e
    acompanha os dias digitados até o usuário escolher o mês (o E2E pegou a sugestão presa ao ciclo antigo). A
    prévia mostra o período e o vencimento da fatura da troca, avisa quando uma fatura deixa de existir (o
    vencimento pula) e quando a troca pendente será substituída; com a fatura já fechada, a prévia diz isso e o
    botão fica desligado. A prévia usa uma terceira cópia da regra, em TypeScript (`cardCycle.ts`), só
    informativa: quem valida é o `accounts`. A troca pendente aparece na coluna Fecha/Vence ("a partir de
    03/2027: 25/5") e na edição, com "desfazer". E2E: corrige nome e limite, recusa a fatura do mês passado,
    troca a partir da sugerida e desfaz.
  - *Dado* a aba Cartões, *quando* edito um cartão, *então* corrijo nome, bandeira, final e limite; *e* ao
    mudar o fechamento e o vencimento escolho a partir de qual fatura vale e vejo a fatura da troca ("a
    fatura de março vai de 26/01 a 25/02 e vence 05/03") antes de confirmar.
  - *Dado* uma troca pendente, *quando* abro o cartão, *então* vejo "a partir da fatura de março: fecha dia
    25, vence dia 5" e posso desfazer.
  - Estimativa: 5 · **Must** · Dependências: CYC-3, CYC-4
- 🔵 **CYC-6** Sugestão do mês da fatura paga pelo ciclo do período.
  - *Dado* um pagamento de fatura no extrato, *quando* o app sugere qual fatura ele paga (importação no
    `transactions` e na SPA), *então* usa o vencimento do ciclo que vale para aquela data.
  - Estimativa: 2 · **Could** · Dependências: CYC-3
- 🔵 **CYC-7** E2E Cypress: mudar o vencimento e conferir as faturas.
  - *Dado* um cartão com compras e uma fatura paga, *quando* mudo o fechamento e o vencimento a partir da
    próxima fatura pela SPA, *então* a fatura paga continua igual, a da troca tem o período e o vencimento
    novos, uma compra nova cai na fatura do ciclo novo e o limite novo aparece no disponível.
  - Estimativa: 3 · **Must** · Dependências: CYC-5

**Fora do escopo (Won't, nesta versão):** mudar a conta do cartão; trocar o cartão por outro mantendo o
histórico (fusão de cartões); calendário de dias úteis e feriados no fechamento e no vencimento (continua
como na ADR-0005); histórico do limite (o limite novo vale na hora); mudar o ciclo de uma fatura já
fechada; aviso de que o ciclo vai mudar.

**Priorização (MoSCoW):** Must = CYC-1, CYC-3, CYC-4, CYC-5 e CYC-7 (18 pts): mudar o ciclo sem
reescrever o passado, com a fatura da troca e todo o app usando o ciclo de cada período, provado ponta a
ponta. Should = CYC-2 (3 pts): editar nome, final e limite. Could = CYC-6 (2 pts). Total: 23 pts.

**Ordem sugerida:** CYC-1 → CYC-3 → CYC-4 → CYC-2 → CYC-5 → CYC-7 (fecha o Must) → CYC-6.

## Ideias de produto (fora do escopo atual — backlog de descoberta)
Não estão no briefing; registrar como épicos candidatos se o PO priorizar.
- (A exportação contábil virou o épico CTB, em 2026-11-22.)
- (Receita e edição de lançamentos viraram o épico LNC, em 2026-11-22.)
- (Lançamentos recorrentes viraram o épico REC, em 2026-11-22.)
- (Lembretes de vencimento viraram o épico NTF, em 2026-11-23.)
- (Reajuste das recorrências virou o épico RJT, em 2026-11-25.)
- (Conciliação da fatura virou o épico RCN, em 2026-11-26.)
- (Editar o cartão e o ciclo da fatura virou o épico CYC, em 2026-11-26.)
