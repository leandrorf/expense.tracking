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

## 🔵 EPIC BDG — Orçamentos por categoria e escopo (PF/PJ)  · *To Do*
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
- 🔵 **BDG-4** Acompanhamento do mês: limite, consumido, restante e status (API).
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
- 🔵 **BDG-5** Aba Orçamentos na SPA: cadastro, alteração e encerramento.
  - *Dado* o usuário logado, *quando* abre a aba **Orçamentos**, *então* vê os orçamentos separados em
    PF e PJ e um formulário com categoria, escopo, valor e mês de vigência.
  - *Dado* o formulário preenchido, *quando* salva, *então* o orçamento aparece na lista sem recarregar
    a página; *dado* um conflito ou erro de validação, *então* a mensagem da API aparece junto ao
    formulário.
  - *Dado* um orçamento na lista, *quando* altera o valor ou encerra, *então* a lista reflete a
    mudança.
  - Atributos `data-cy` em todos os elementos usados pelo E2E; acessível por teclado.
  - Estimativa: 5 · **Must** · Dependências: BDG-2, BDG-3
- 🔵 **BDG-6** Acompanhamento visual do mês na aba Orçamentos.
  - *Dado* orçamentos PF e PJ no mês corrente, *quando* abro a aba, *então* cada um mostra limite,
    consumido, restante, percentual e uma barra de progresso, agrupados por escopo.
  - *Dado* um orçamento **Atenção** ou **Estourado**, *quando* a lista renderiza, *então* o status
    aparece em texto e em destaque visual (não só por cor).
  - *Dado* a aba aberta, *quando* navego para o mês anterior ou o seguinte, *então* os valores e os
    limites são os daquele mês.
  - Estimativa: 3 · **Must** · Dependências: BDG-4, BDG-5
- 🔵 **BDG-7** Sugerir as categorias já usadas (orçamento e lançamento).
  - *Dado* lançamentos PF e PJ com categorias "Alimentação" e "Combustível", *quando* digito a
    categoria no formulário de orçamento ou de lançamento, *então* as categorias já usadas pelo
    usuário aparecem como sugestão, sem repetir variações de maiúscula/acento.
  - *Dado* outro usuário, *quando* consulta as sugestões, *então* não vê as categorias deste.
  - Continua permitindo categoria nova (texto livre); a lista sai da projeção.
  - Estimativa: 3 · **Should** · Dependências: BDG-1 (independe das demais)
- 🔵 **BDG-8** Gastos sem orçamento no mês.
  - *Dado* gastos PJ em "Viagens" sem orçamento PJ para essa categoria, *quando* consulto o
    acompanhamento do mês, *então* vejo o total "Sem orçamento" por escopo, com as categorias que o
    compõem.
  - Estimativa: 2 · **Should** · Dependências: BDG-4 (UI junto da BDG-6)
- 🔵 **BDG-9** E2E Cypress: jornadas de orçamento PF e PJ.
  - *Dado* um orçamento "Alimentação" PF, *quando* lanço um gasto PF no **Pix** que ultrapassa o
    limite, *então* a aba Orçamentos mostra o consumido e o status **Estourado**.
  - *Dado* um orçamento PJ, *quando* lanço um gasto PJ no **cartão de crédito**, *então* o consumido do
    orçamento PJ sobe e o do PF da mesma categoria não muda.
  - Espera a projeção como o `cy.waitForSummary` (sem `cy.wait` fixo); roda no job `Cypress E2E`.
  - Estimativa: 5 · **Must** · Dependências: BDG-6
- 🔵 **BDG-10** Limiar de "Atenção" configurável por orçamento.
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

## Ideias de produto (fora do escopo atual — backlog de descoberta)
Não estão no briefing; registrar como épicos candidatos se o PO priorizar.
- Importação de extrato/fatura (CSV/OFX).
- Fechamento de fatura de cartão e previsão de caixa.
- Exportação contábil (relatório PJ para o contador).
