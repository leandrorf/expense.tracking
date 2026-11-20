# Prompt-fonte do projeto — expense.tracking

> **Documento vivo.** Este é o briefing original do projeto, mantido versionado para ser
> re-alimentado sempre que houver mudança de escopo, stack ou prioridade. Ao mudar algo
> estrutural, **atualize este arquivo** (seção _Histórico de mudanças_ no fim) e propague para
> a memória do projeto, o `README.md`, o `docs/architecture.md` e o backlog no Jira.

---

## Briefing original (verbatim)

Vamos criar um projeto com a finalidade de controle de gastos para conta empresa e pessoal.
Como sou PJ tenho conta e gastos da minha empresa e gastos pessoais. Preciso controlar gastos
nos cartões de créditos, pagamentos com pix, dinheiro, transferências e boletos tanto para a
empresa quanto pessoal. O projeto pode usar nome com o prefixo que comece como
`expense.tracking`. Para o front quero usar React com TypeScript, backend API em .NET Core C#
com Entity Framework usando pattern **arquitetura hexagonal** e banco de dados MySQL. O projeto
deve ser em **microserviço** para front e para backend. Adotar estrutura de **testes unitários
e EndToEnd**. As aplicações e base de dados serão utilizados em **container Docker**. A
autenticação deve utilizar o padrão **OAuth2 sem bibliotecas de terceiros** — criar nossa
própria API usando todo padrão de mercado. Criar **agents** para orquestrarem a execução usando
**PO, PMO, Scrum Master, Dev e QA** (e o que for necessário). Planejamento no **Jira**
(projeto SCRUM). Salvar o prompt para re-alimentação em caso de mudanças.

---

## Requisitos destilados

### Domínio
- Controle de gastos **pessoal (PF)** e **empresarial (PJ)** — o mesmo usuário é PJ e tem os dois escopos.
- Meios de pagamento: **cartão de crédito, Pix, dinheiro, transferência, boleto**.
- Escopo do gasto (PF/PJ) é dimensão de primeira classe em lançamentos e relatórios.

### Stack (ver decisões não óbvias na memória do projeto)
- **Frontend:** React 18 + TypeScript (Vite).
- **Backend:** .NET 10 / C# · ASP.NET Core · EF Core 10 · **arquitetura hexagonal** (Domain / Application / Infrastructure / Api).
- **Banco:** MySQL 8 — **um banco por serviço**. Provider Oracle `MySql.EntityFrameworkCore` (não Pomelo).
- **Auth:** servidor **OAuth2/OIDC próprio**, sem IdentityServer/Keycloak. Grants: Authorization Code + PKCE, Refresh (com rotação), Client Credentials. JWT RS256 + JWKS.
- **Arquitetura:** microserviços (backend) + SPA (frontend), atrás de um **API Gateway** (YARP).
- **Infra:** Docker + Docker Compose.
- **Testes:** xUnit (unitário) + WebApplicationFactory (integração) + **Cypress (EndToEnd)**.
- **Restrições de licença:** evitar libs pagas — **sem FluentAssertions v8, sem MediatR**.

### Serviços
| Serviço | Papel | Banco |
|---|---|---|
| `identity` | Servidor OAuth2/OIDC próprio | `expense_identity` |
| `accounts` | Contas PF/PJ, cartões, catálogo de meios de pagamento | `expense_accounts` |
| `transactions` | Lançamentos de gastos (valida conta/cartão no accounts) | `expense_transactions` |
| `reporting` | Consolidação de gastos (projeção alimentada pelos eventos do `transactions`) | `expense_reporting` |
| `gateway` | Entrada única das APIs de recurso sob `/api/**` | — |
| `apps/web` | SPA React + TS | — |

---

## Estado atual (snapshot)

> Atualize este bloco quando o estado mudar. Fonte da verdade detalhada: memória do projeto + `docs/architecture.md`.

- Backend: **completo** (`identity`, `accounts`, `transactions`, `reporting`, `gateway`).
- Frontend `apps/web`: **completo** (login OAuth + abas Resumo / Lançamentos / Contas / Cartões).
- Testes E2E (Cypress): **completos** (jornadas PF/Pix e PJ/cartão).
- Contrato de meios de pagamento: **unificado** (code canônico = nome do enum).
- **CI**: GitHub Actions com build + unitários + integração por serviço e Cypress E2E; o `main` exige os 6 checks verdes.
- **Observabilidade**: logs JSON com correlation id, tracing OpenTelemetry → Jaeger, métricas via OpenTelemetry Collector → Prometheus, health `/health/live` e `/health/ready`.
- **Reporting por eventos** ([ADR-0001](adr/0001-transporte-de-eventos-transactions-reporting.md)): outbox do `transactions` exposto como feed HTTP; o `reporting` mantém uma projeção em banco próprio e o Resumo lê só dela (métrica de atraso no Prometheus). Lançamentos anteriores ao outbox ganharam o escopo PF/PJ por evento.

### Próximos passos conhecidos
Todos os épicos do backlog estão concluídos. Candidatos a novos épicos (ainda não refinados pelo PO,
ver `docs/backlog.md`): orçamentos/metas por categoria e escopo, importação de extrato (CSV/OFX),
fechamento de fatura de cartão e previsão de caixa, exportação contábil PJ.

---

## Orquestração por agents

Agents de processo em `.claude/agents/` (PO, PMO, Scrum Master, Dev, QA). Eles consomem **este
documento** como fonte de requisito e delegam execução técnica aos agents especialistas
(`dotnet-backend-architect`, `senior-react-developer`, `cypress-qa-analyst`, etc.).

## Planejamento

- Jira: projeto **SCRUM** em `https://leandrorodriguesfernandes.atlassian.net`.

---

## Histórico de mudanças

| Data | Mudança | Origem |
|---|---|---|
| 2026-11-09 | Criação do documento-fonte a partir do briefing original. | prompt inicial |
| 2026-11-19 | Épicos CI (pipeline + gate do `main`) e OBS (logs, tracing, métricas/health) concluídos. | backlog CI/OBS |
| 2026-11-19 | `reporting` deixa de ser agregador sem banco: passa a manter projeção por eventos em `expense_reporting` (ADR-0001, épico EVT). Novos escopos de serviço `events.read` e `accounts.scopes.read`. | épico EVT |
| 2026-11-20 | Documento e agents de processo (`.claude/agents/`) passam a ser versionados no repositório. | pedido do usuário |
