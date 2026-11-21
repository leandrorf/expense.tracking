# Expense Tracking — Arquitetura

Controle de gastos **pessoal e empresarial (PJ)** cobrindo cartões de crédito, Pix,
dinheiro e boletos. Microserviços no backend, SPA no frontend, tudo em containers.

## Stack

| Camada     | Tecnologia                                             |
|------------|--------------------------------------------------------|
| Frontend   | React 18 + TypeScript (Vite)                           |
| Backend    | .NET 10 / C# · ASP.NET Core · EF Core 10               |
| Banco      | MySQL 8 (um banco por serviço)                         |
| Auth       | OAuth2 próprio (sem IdentityServer/Keycloak)           |
| Infra      | Docker + Docker Compose                                |
| Testes     | xUnit (unitário) · WebApplicationFactory (integração) · Cypress (EndToEnd) |

> Observação: a stack-alvo pedida foi .NET 9, mas a máquina/CI tem o SDK **.NET 10**
> (LTS). Adotamos .NET 10 por ser a versão instalada e mais recente.

## Decomposição em microserviços (backend)

Cada serviço é autônomo, com seu próprio banco MySQL e **arquitetura hexagonal**.

| Serviço        | Responsabilidade                                                        | Status        |
|----------------|-------------------------------------------------------------------------|---------------|
| `identity`     | Servidor OAuth2/OIDC: usuários, clientes, tokens, JWKS                   | ✅ implementado |
| `accounts`     | Contas (PF/PJ), cartões de crédito, catálogo de meios de pagamento        | ✅ implementado |
| `transactions` | Lançamentos de gastos (crédito, Pix, dinheiro, boleto), categorização    | ✅ implementado |
| `reporting`    | Consolidação de gastos (projeção alimentada pelos eventos do transactions) | ✅ implementado |
| `gateway`      | API Gateway (YARP): roteamento, validação de token, CORS                 | ✅ implementado |

Frontend: `apps/web` (SPA React + TS) — ✅ login OAuth2 + PKCE **e** telas de resumo,
lançamentos, orçamentos, contas e cartões (consome as APIs pelo gateway).

## Arquitetura hexagonal (ports & adapters)

Cada serviço .NET segue a mesma divisão:

```
Domain          Núcleo: entidades, value objects, regras. Sem dependências externas.
Application     Casos de uso + portas (interfaces). Depende só do Domain.
Infrastructure  Adaptadores dirigidos (driven): EF Core/MySQL, cripto, JWT.
Api             Adaptador dirigente (driving): HTTP + composition root (DI).
```

Regra de dependência: tudo aponta para dentro (Api → Application → Domain;
Infrastructure → Application → Domain). O Domain não conhece EF, HTTP nem JWT.

## Identity Server (OAuth2) — implementado do zero

Grants suportados (escolha do projeto):

- **Authorization Code + PKCE** (RFC 7636) — para a SPA React (cliente público).
- **Refresh Token** com **rotação** e detecção de reuso.
- **Client Credentials** — comunicação máquina-a-máquina (clientes confidenciais).

Decisões de segurança:

- Access tokens são **JWT assinados em RS256**; a chave pública é publicada via **JWKS**.
- Senhas: **PBKDF2-HMAC-SHA256** (100k iterações, salt de 128 bits) — só BCL.
- Authorization codes e refresh tokens são **persistidos como hash SHA-256** (nunca em claro).
- Authorization code é de **uso único** e expira em ~2 min.
- Comparações sensíveis usam tempo constante (`FixedTimeEquals`).

Endpoints:

| Método | Rota                                   | Descrição                               |
|--------|----------------------------------------|-----------------------------------------|
| POST   | `/account/register`                    | Cria usuário                            |
| POST   | `/account/login`                       | Login (cookie de sessão)                |
| POST   | `/account/logout`                      | Encerra sessão                          |
| GET    | `/account/me`                          | Dados da sessão atual                   |
| GET    | `/oauth/authorize`                     | Emite authorization code (requer sessão)|
| POST   | `/oauth/token`                         | Troca de tokens (3 grants)              |
| GET    | `/connect/userinfo`                    | Claims do usuário (Bearer)              |
| GET    | `/.well-known/openid-configuration`    | Discovery                               |
| GET    | `/.well-known/jwks.json`               | Chaves públicas (JWKS)                  |
| GET    | `/health`                              | Health check                            |

### Fluxo de login no navegador (Authorization Code + PKCE)

1. SPA gera `code_verifier` + `code_challenge` (S256) e redireciona para `/oauth/authorize`.
2. Sem sessão, o `identity` redireciona para sua **própria tela de login** (`/account/login`).
3. Usuário autentica → cookie de sessão → volta ao `/oauth/authorize` → emite o `code`.
4. SPA recebe o `code` no `/callback`, troca por tokens em `/oauth/token` (com PKCE) via `fetch`.
5. SPA chama `/connect/userinfo` com `Bearer` e renova via refresh token quando expira.

CORS: o `identity` libera a origem da SPA (`Cors:AllowedOrigins`, default `http://localhost:5173`)
para os endpoints chamados por `fetch` (`/oauth/token`, `/connect/userinfo`).

A SPA implementa o PKCE e a troca de tokens **sem bibliotecas de terceiros** (Web Crypto API),
guardando os tokens em `sessionStorage`. As telas (contas, cartões, lançamentos) chamam as
APIs de recurso pelo **gateway** (`VITE_API_BASE_URL`), anexando o `Bearer` e renovando o
token automaticamente em caso de 401.

> Contrato de meios de pagamento (unificado): o catálogo `/payment-methods` (accounts) expõe
> como `code` o **nome do enum** (`CreditCard`, `Pix`, …) — o mesmo identificador que o
> `transactions` aceita/devolve e sobre o qual o `reporting` agrupa. A SPA consome o catálogo
> (não há mais lista fixa) e envia esse `code` ao criar lançamentos. O `name` do catálogo é só
> o rótulo de exibição. Fonte única: `PaymentMethodInfo.Code => Method.ToString()`.

### Simplificações atuais (a evoluir)

- `/oauth/authorize` retorna erros de validação como JSON (não redireciona `error=` ao
  `redirect_uri`); apenas o caso "sem sessão" redireciona para o login.
- Não há tela de consentimento (clientes são first-party / auto-consentidos).
- A chave de assinatura é efêmera se `OAuth:SigningKey:PrivateKeyPem` não for configurada
  (apenas DEV). Em produção, injetar a chave via configuração/secret.
- Tokens no `sessionStorage` (simples para DEV). Avaliar BFF/cookies httpOnly depois.

## Serviço `accounts` (recurso) — implementado

Primeiro serviço de recurso, protegido por **JWT Bearer** validado contra o JWKS do
`identity`. Mesma divisão hexagonal. Banco próprio `expense_accounts` (container MySQL
separado — um banco por serviço).

- **Contas** (`/accounts`): PF/PJ, com validação de CPF/CNPJ coerente ao escopo, moeda ISO.
- **Cartões de crédito** (`/credit-cards`): vinculados a uma conta, com ciclo (fechamento/vencimento) e limite.
- **Meios de pagamento** (`/payment-methods`): catálogo (dinheiro, Pix, boleto, crédito, débito, transferência).

Segurança e isolamento:

- Validação do token via metadados OIDC do `identity` (`MetadataAddress` interno na rede
  Docker; `ValidIssuer` = issuer público). Audience `expense-tracking-api`, algoritmo RS256.
- **Políticas de escopo**: leitura exige `expenses.read`; escrita exige `expenses.write`.
- **Isolamento por dono**: toda consulta é filtrada pelo `sub` do token (um usuário só vê os próprios dados).

**Endpoint interno (EVT-4)**: `GET /internal/account-scopes?ids={id}&ids={id}` (1 a 100 ids)
devolve só `{ accountId, scope }` de contas de **qualquer** dono; ids desconhecidos ficam de fora.
Exige token com o escopo `accounts.scopes.read`, que só o cliente de serviço pode pedir (a SPA
não), e não passa pelo gateway. É a única consulta do `accounts` sem filtro de dono (porta
`IAccountScopeLookup`, separada do repositório escopado).

## Serviço `transactions` (recurso) — implementado

Núcleo do controle de gastos. Mesma divisão hexagonal, banco próprio `expense_transactions`,
protegido por JWT Bearer (escopo + isolamento por dono).

- **Lançamentos** (`/transactions`): valor (Money: positivo, 2 casas, ISO), tipo (gasto/receita),
  meio de pagamento, categoria, data, observações; referencia `accountId` e, quando crédito, `creditCardId`.
- **Regras**: cartão de crédito exige `creditCardId`; demais meios não aceitam cartão.
- **Listagem com filtros**: por conta, categoria e período (`from`/`to`).

**Comunicação entre serviços**: ao criar um lançamento, o `transactions` valida a conta e o
cartão chamando o `accounts` por HTTP, **encaminhando o token do usuário** (porta `IAccountsClient`).
Como o `accounts` filtra pelo dono do token, um 200 confirma existência **e** posse — sem acessar
o banco de outro serviço. Nos testes, a porta é substituída por um fake.

**Eventos de domínio (EVT-1, [ADR-0001](adr/0001-transporte-de-eventos-transactions-reporting.md))**:

- **Outbox transacional** (tabela `outbox_events`): criar um lançamento grava um
  `TransactionRegistered` e excluir grava um `TransactionDeleted`, no **mesmo `SaveChanges`** da
  mudança (porta `IEventOutbox`). Nunca há lançamento sem evento, nem o contrário. A exclusão é
  física, então o evento é o único registro dela.
- **Contrato**: envelope com `sequence`, `eventId`, `eventType`, `version`, `aggregateId`,
  `ownerUserId`, `occurredAtUtc` e `traceParent` (W3C, liga ao trace da request de origem).
  O payload do `TransactionRegistered` traz o necessário para a projeção do reporting, incluindo
  o escopo da conta (`accountScope`: `Personal`/`Business`), lido do `accounts` na mesma
  validação de sempre. O escopo é imutável na conta, então a cópia não fica desatualizada.
- **Log retido**: nada é apagado depois de lido; consumidores reconstroem projeções desde a
  sequência 0.
- **Feed** `GET /events?after={seq}&limit={n}` (máx. 500): exige token com o escopo `events.read`
  (client credentials do `expense-tracking-service`) e **não passa pelo gateway**. Devolve os
  eventos em ordem, `lastSequence` (o cursor da próxima leitura) e `headSequence` (a maior
  sequência gravada, inclusive eventos ainda na margem), com que o leitor mede o próprio atraso.
- **Margem contra lacunas** (`Events:FeedSafetyMarginSeconds`, padrão 5 s): a sequência é
  auto-incremento, então uma transação pode commitar depois de outra com sequência maior. O feed
  só serve o **prefixo contíguo** de eventos mais antigos que a margem e para no primeiro evento
  recente demais, para o leitor nunca pular uma sequência.
- **Carga inicial**: na subida, lançamentos sem `TransactionRegistered` (anteriores ao outbox)
  ganham o evento, em ordem de criação. É idempotente. Esses eventos saem com
  `accountScope = null`, porque o `transactions` não guarda o escopo da conta.
- **Escopo dos legados (EVT-4)**: depois da carga inicial, o `AccountScopeBackfillWorker` pede ao
  `accounts` o escopo das contas desses lançamentos (`GET /internal/account-scopes`, token de
  serviço com `accounts.scopes.read`) e grava um `TransactionAccountScopeResolved`
  (`transactionId`, `ownerUserId`, `accountId`, `accountScope`) por lançamento. O log não é
  reescrito: o evento completa o `TransactionRegistered`, e um rebuild desde a sequência 0 chega
  ao mesmo escopo. Idempotente (ignora quem já tem o evento e os lançamentos excluídos). Em
  falha (accounts ou identity fora do ar), tenta de novo com espera crescente até 5 min e para ao
  concluir. `Events:LegacyScopeBackfill:Enabled=false` desliga.

## Serviço `reporting` — implementado

Consolida gastos a partir de uma **projeção local** dos lançamentos, no banco próprio
`expense_reporting`, alimentada pelo feed de eventos do `transactions`
([ADR-0001](adr/0001-transporte-de-eventos-transactions-reporting.md)).

- `GET /reports/summary?accountId=&from=&to=` → total de gastos, receitas, saldo, contagem,
  e quebras **por categoria**, **por escopo (PF/PJ)** e **por meio de pagamento**.
- **Lê só a projeção (EVT-3)**: filtra pelo dono (`sub` do token), pela conta e pelo período
  (inclusivo, pela data do lançamento). Não chama o `transactions` nem o `accounts` na consulta,
  então responde com os dois fora do ar. Em troca, o resumo pode estar **defasado**: um
  lançamento novo aparece depois da margem do feed (5 s) mais o polling do worker (2 s).
- O escopo PF/PJ vem do próprio evento. Lançamentos anteriores ao outbox ganham o escopo pelo
  `TransactionAccountScopeResolved` (EVT-4); até ele chegar, ou se a conta não existir mais no
  `accounts`, aparecem como `Unknown` ("Desconhecido" na SPA).
- O cálculo (`SummaryCalculator`) é uma função pura no domínio — totalmente testável.

**Projeção por eventos (EVT-2)**:

- **Worker** (`ProjectionWorker`, hosted service): lê o feed em lotes a partir do checkpoint
  (`Projection:BatchSize`, padrão 100). Com lote cheio, lê de novo na hora; com o feed vazio,
  espera `Projection:PollIntervalSeconds` (2 s). Em erro, tenta de novo após 10 s.
  `Projection:Enabled=false` desliga o worker.
- **Token de serviço**: client credentials no identity (`ServiceAuth:*`, escopo `events.read`),
  reaproveitado até perto de expirar.
- **Mesmo commit**: `ProjectionProcessor` aplica o lote e grava projeção
  (`projected_transactions`) e checkpoint (`projection_checkpoints`) num único `SaveChanges`.
  Ou o lote inteiro entra, ou é relido.
- **Idempotência**: cadastro é upsert e exclusão remove se existir. Reler eventos, inclusive com o
  checkpoint zerado para reconstruir a projeção, chega ao mesmo estado. Tipo de evento
  desconhecido é pulado; versão não suportada aborta o lote sem gravar nada.
- **Tracing**: um span por lote, com links para os traces das requests que gravaram os eventos
  (`traceParent` do envelope).
- Eventos legados (`accountScope = null`) ficam com escopo nulo na projeção até o
  `TransactionAccountScopeResolved`, que só completa uma linha existente (não recria um
  lançamento já excluído).
- **Atraso (EVT-3)**: a cada leitura do feed, o worker registra `headSequence − checkpoint` em
  `ProjectionStatus`, exposto como as métricas `reporting.projection.lag` e
  `reporting.projection.checkpoint` (meter `Expense.Tracking.Reporting.Projection`). No
  Prometheus: `reporting_projection_lag` e `reporting_projection_checkpoint`. Antes da primeira
  leitura bem-sucedida nada é medido, para um atraso desconhecido não aparecer como zero.

**Orçamentos (épico BDG, [ADR-0002](adr/0002-orcamentos-no-reporting.md))**:

- Ficam no `reporting`, na tabela `budgets` do `expense_reporting`. São **dado escrito pelo
  usuário**, fora da projeção: reconstruir a projeção (zerar o checkpoint) não pode apagá-los.
- `GET /budgets?month=yyyy-MM` (`expenses.read` ou `expenses.write`): sem `month`, lista todos os
  orçamentos do dono (inclusive encerrados) com o valor da vigência mais recente; com `month`, só
  os que valem naquele mês, com o valor daquele mês. Cada item traz `limits` (vigências) e
  `closedFromMonth`.
- `POST /budgets` (`expenses.write`) cria `{category, scope, amount, startMonth: "yyyy-MM",
  currency?, warningThreshold?}` (moeda padrão BRL, limiar padrão 80) e responde 201.
- **Limiar de atenção (BDG-10)**: `PUT /budgets/{id}/warning-threshold {warningThreshold}`
  (inteiro de 1 a 100, coluna `budgets.warning_threshold`). Não tem vigência: vale para todos os
  meses do orçamento, inclusive os passados, porque o status é calculado na leitura.
- **Vigência (BDG-3)**: `PUT /budgets/{id}/amount {amount, fromMonth}` muda o valor a partir de um
  mês; os meses anteriores ficam como estavam e vigências posteriores a `fromMonth` são
  substituídas. Tabela `budget_limits` (uma linha por vigência).
- **Encerramento (BDG-3)**: `POST /budgets/{id}/close {fromMonth}`: o orçamento deixa de valer a
  partir desse mês (tem de ser depois do início) e continua nos anteriores. Depois disso a mesma
  categoria aceita um orçamento novo que comece no mês do encerramento ou depois.
- Erros no formato `ProblemDetails` com `title` = código estável (`budget.category_invalid`,
  `budget.amount_invalid`, `budget.scope_invalid`, `budget.month_invalid`,
  `budget.currency_invalid`, `budget.month_before_start`, `budget.warning_threshold_invalid`; 409: `budget.duplicate`,
  `budget.closed`, `budget.already_closed`; 404: `budget.not_found`, inclusive para orçamento
  de outro usuário).
- **Sugestão de categorias (BDG-7)**: `GET /budgets/categories` (`expenses.read` ou
  `expenses.write`) → `["Alimentação", ...]`: categorias dos lançamentos do dono na projeção e dos
  seus orçamentos, uma por chave normalizada (`CategoryKey`), pela grafia mais usada, em ordem da
  chave. A SPA usa como `<datalist>` nos campos de categoria (orçamento e lançamento), sem
  impedir categoria nova.
- **Acompanhamento do mês (BDG-4)**: `GET /budgets/status?month=yyyy-MM` (`expenses.read` ou
  `expenses.write`; `month` obrigatório) → `{month, budgets: [{budgetId, category, scope, currency,
  limit, spent, remaining, percent, status}], unbudgeted: [{scope, currency, total, categories:
  [{category, total}]}]}` para cada orçamento que vale no mês. `unbudgeted` (BDG-8) traz, por
  escopo e moeda, os gastos que não caem em nenhum orçamento vigente pela mesma regra do consumido,
  com as categorias (variantes de digitação juntas, exibidas pela grafia mais usada). O consumido
  sai da **projeção** (mesma fonte e defasagem do Resumo; responde com o `transactions` fora): soma
  dos gastos do dono no mês, com o mesmo escopo, moeda e categoria normalizada, em qualquer conta e
  meio de pagamento, pela data do lançamento (compra no cartão conta no mês da compra). Receitas e
  escopo desconhecido não contam. `remaining` fica negativo quando estoura. `status`: `OnTrack`
  (abaixo do limiar de atenção do orçamento, padrão 80%), `Warning` (do limiar a 100%),
  `Exceeded` (> 100%), calculado por `BudgetStatusCalculator` (função pura no domínio). Cada item
  traz o `warningThreshold` usado.
- **Categoria normalizada** (`CategoryKey`): sem diferença de maiúsculas, acentos e espaços
  extras, calculada no domínio (os serviços rodam com `InvariantGlobalization`, então o mapeamento
  de acentos é explícito, sem `string.Normalize`).
- **Unicidade**: dois orçamentos da mesma categoria e escopo não podem valer no mesmo mês. A
  aplicação confere a sobreposição; o índice único `ux_budgets_owner_scope_open_category` (sobre
  `open_category_key`, nula quando encerrado) garante no banco um só orçamento aberto, mesmo com
  requisições concorrentes.

## Gateway (YARP) — implementado

Ponto único de entrada para os **serviços de recurso**, usando o reverse proxy YARP.

- `/api/transactions/**` → `transactions`; `/api/reports/**` e `/api/budgets/**` → `reporting`;
  `/api/**` (demais) → `accounts`. Remove o prefixo `/api`: ex. `GET /api/accounts` →
  `accounts` `/accounts`, `GET /api/reports/summary` → `reporting` `/reports/summary`.
- **Validação do token na borda** (mesma config JWT dos serviços) via policy `authenticated`:
  sem token válido, o gateway responde 401 e nem encaminha.
- **CORS centralizado** para a origem da SPA.
- Escopo e isolamento por dono continuam sendo aplicados no serviço de destino (defesa em profundidade).
- **Endpoints internos bloqueados (EVT-4)**: `/api/internal/**` responde 404 na borda. Sem isso,
  o catch-all `/api/**` exporia o `/internal/**` do `accounts`, que é só para serviços.

Topologia: o `identity` permanece acessível diretamente (é o emissor dos tokens — o `iss`
precisa bater com o issuer configurado). A SPA autentica no `identity` e chama as APIs de
recurso através do gateway. Novos serviços (ex.: `transactions`) entram como novas rotas/clusters.

## Observabilidade

Cada serviço configura a observabilidade em `Api/Setup/ObservabilitySetup.cs` (uma cópia por
serviço, sem biblioteca compartilhada).

- **Logs estruturados (OBS-1):** JSON no console (`AddJsonConsole`, com scopes). Todo request
  resolve um `X-Correlation-Id` (header recebido → trace id → GUID), ecoa o valor na resposta e o
  injeta num log scope. O gateway reescreve o header antes do YARP; `transactions` e `reporting`
  o repassam nos typed clients (`CorrelationIdForwardingHandler`).
- **Tracing distribuído (OBS-2):** OpenTelemetry com spans de entrada (ASP.NET Core) e de saída
  (HttpClient). O contexto viaja no header W3C `traceparent`: o YARP o propaga do gateway para o
  serviço de destino e os typed clients, de `transactions`/`reporting` para `accounts`. Assim um
  lançamento pela SPA vira um único trace `gateway → transactions → accounts`.
  - Nome do serviço no trace: `expense-tracking-<serviço>`.
  - Exportação OTLP só quando `OTEL_EXPORTER_OTLP_ENDPOINT` está definido. Sem ele (testes, CI,
    `dotnet run`), o tracing segue ativo e o trace id continua alimentando o correlation id.
  - `/health` fica fora dos traces, para o polling do Docker e do CI não gerar ruído.
  - No Docker Compose, os spans passam pelo OpenTelemetry Collector e chegam ao **Jaeger**
    (`jaegertracing/jaeger`), com a UI em `http://localhost:16686`.
- **Métricas (OBS-3):** o mesmo pipeline do OpenTelemetry coleta `http.server.request.duration`
  (requests recebidos), `http.client.request.duration` (chamadas entre serviços) e o runtime do
  .NET (meter nativo `System.Runtime`: GC, memória, threads). O `reporting` publica também o
  atraso da projeção (`reporting.projection.lag`, EVT-3). Exportação OTLP com a mesma regra
  dos traces; no Compose, a cada 15 s (`OTEL_METRIC_EXPORT_INTERVAL`).
- **OpenTelemetry Collector (OBS-3):** ponto único de entrada da telemetria
  (`infra/observability/otel-collector.yaml`). Os serviços só conhecem o collector
  (`OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`); ele repassa os traces ao Jaeger e
  expõe as métricas em `:8889` para o **Prometheus** (`infra/observability/prometheus.yml`, UI em
  `http://localhost:9090`). No Prometheus, o label `job` é o nome do serviço
  (ex.: `expense-tracking-accounts`). Os serviços não dependem do collector para subir.
- **Health padronizado (OBS-3):** health checks do ASP.NET Core, públicos e com resposta JSON
  (só nome, status e duração de cada check; nenhum detalhe de exceção).
  - `/health/live`: o processo responde. Não roda checks.
  - `/health/ready`: as dependências do próprio serviço estão ok (checks com a tag `ready`). Hoje,
    o banco em `accounts`, `identity`, `transactions` e `reporting` (`AddDbContextCheck`). Responde
    503 quando algum falha. `gateway` não tem banco, e nenhum serviço checa outros serviços, para
    uma queda não se propagar em cascata.
  - `/health`: alias de `/health/live`, mantido por compatibilidade.
- Quando o chamador não envia `X-Correlation-Id`, o correlation id dos logs é o próprio trace id,
  o que liga uma linha de log ao trace no Jaeger.

## Padrões do monorepo

- **Central Package Management** (`Directory.Packages.props`) fixa versões de pacotes.
- **`Directory.Build.props`** define TFM, nullable e implicit usings globalmente.
- **`NuGet.config`** restringe a restauração ao feed público nuget.org.
- Prefixo de namespaces: `Expense.Tracking.<Serviço>.<Camada>`.
