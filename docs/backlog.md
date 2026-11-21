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

## 🟡 EPIC IMP — Importação de extrato e fatura (CSV/OFX)  · *In Progress*
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
- 🔵 **IMP-6** Sugerir categoria e meio de pagamento pelo histórico.
  - *Dado* lançamentos anteriores com a descrição "UBER *TRIP" na categoria "Transporte", *quando*
    importo uma linha com "Uber *Trip", *então* a categoria sugerida é "Transporte" (descrição
    normalizada, a categoria mais recente vence).
  - *Dado* um memo com "PIX" ou "BOLETO" num extrato de conta, *quando* a prévia é montada, *então*
    o meio sugerido é Pix ou Boleto; sem pista, vale o meio padrão do lote.
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
- 🔵 **IMP-11** Guardar o mapeamento de CSV por banco.
  - *Dado* um mapeamento confirmado para um CSV, *quando* salvo com um nome ("Banco X — fatura"),
    *então* nas próximas importações ele aparece para escolher, sem remapear.
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

## Ideias de produto (fora do escopo atual — backlog de descoberta)
Não estão no briefing; registrar como épicos candidatos se o PO priorizar.
- Fechamento de fatura de cartão e previsão de caixa.
- Exportação contábil (relatório PJ para o contador).
