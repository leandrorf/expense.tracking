# expense.tracking

Controle de gastos **pessoal e empresarial (PJ)** — cartões de crédito, Pix, dinheiro e
boletos — em arquitetura de microserviços, com autenticação OAuth2 própria.

- **Frontend:** React 18 + TypeScript (Vite) · *planejado*
- **Backend:** .NET 10 / C# · ASP.NET Core · EF Core 10 · arquitetura hexagonal
- **Banco:** MySQL 8 (um por serviço)
- **Auth:** servidor OAuth2/OIDC próprio (sem bibliotecas de terceiros)
- **Infra:** Docker + Docker Compose
- **Testes:** xUnit (unitário) + WebApplicationFactory (integração)

Veja [`docs/architecture.md`](docs/architecture.md) para o desenho completo e o estado de
cada serviço.

## Estrutura do repositório

```
expense.tracking/
├── Directory.Build.props / Directory.Packages.props   # padrões + versões centralizadas
├── NuGet.config                                        # feed nuget.org
├── docker-compose.yml / .env.example                   # MySQL + identity
├── docs/architecture.md
├── services/
│   └── identity/          # Servidor OAuth2 (hexagonal) — IMPLEMENTADO
│       ├── src/ Domain | Application | Infrastructure | Api
│       └── tests/ UnitTests | IntegrationTests
└── apps/
    └── web/               # SPA React + TS (login OAuth2 + PKCE) — IMPLEMENTADO
```

## Pré-requisitos

- .NET SDK 10
- Docker + Docker Compose
- Node 20+ (para o frontend, quando existir)

## Rodando com Docker (MySQL + Identity)

```bash
cp .env.example .env          # ajuste as senhas
docker compose up --build
```

Sobem dois serviços:

- **Identity** em `http://localhost:5080`
- **SPA (web)** em `http://localhost:5173`

Com o seed habilitado, são criados:

- Cliente SPA público: `expense-tracking-spa` (redirect `http://localhost:5173/callback`)
- Cliente de serviço confidencial: `expense-tracking-service` / `dev-service-secret`
- Usuário demo: `demo@expense.local` / `Password123!`

Abra `http://localhost:5173`, clique em **Entrar**, autentique com o usuário demo e veja
os dados do token em `/connect/userinfo`. Verificações rápidas:

```bash
curl http://localhost:5080/health
curl http://localhost:5080/.well-known/openid-configuration
```

## Desenvolvimento local do Identity (sem Docker)

```bash
cd services/identity
dotnet build
dotnet test                   # 37 testes (unitários + integração)
# Para rodar a API é necessário um MySQL acessível (ver ConnectionStrings:IdentityDb)
dotnet run --project src/Expense.Tracking.Identity.Api
```

### Migrations (EF Core)

```bash
cd services/identity
dotnet ef migrations add <Nome> \
  --project src/Expense.Tracking.Identity.Infrastructure \
  --startup-project src/Expense.Tracking.Identity.Api \
  --context IdentityDbContext \
  --output-dir Persistence/Migrations
```

As migrations são aplicadas automaticamente no startup da API (com retry).

## Desenvolvimento local da SPA (sem Docker)

```bash
cd apps/web
cp .env.example .env     # ajuste a authority/porta se necessário
npm install
npm run dev              # http://localhost:5173
```

Requer o Identity rodando (Docker ou `dotnet run`) em `http://localhost:5080`.

## Próximos passos

1. `accounts` (contas PF/PJ, cartões, meios de pagamento) — primeiro serviço de recurso
2. `gateway` (API Gateway) fronteando os serviços de recurso + validação de token
3. `transactions` (lançamentos de gastos)
4. `reporting` (relatórios/consolidação)
5. Testes E2E de ponta a ponta entre serviços e SPA
