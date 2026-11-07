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
| Testes     | xUnit (unitário) · WebApplicationFactory (integração)  |

> Observação: a stack-alvo pedida foi .NET 9, mas a máquina/CI tem o SDK **.NET 10**
> (LTS). Adotamos .NET 10 por ser a versão instalada e mais recente.

## Decomposição em microserviços (backend)

Cada serviço é autônomo, com seu próprio banco MySQL e **arquitetura hexagonal**.

| Serviço        | Responsabilidade                                                        | Status        |
|----------------|-------------------------------------------------------------------------|---------------|
| `identity`     | Servidor OAuth2/OIDC: usuários, clientes, tokens, JWKS                   | ✅ implementado |
| `accounts`     | Contas (PF/PJ), cartões de crédito, catálogo de meios de pagamento        | ✅ implementado |
| `transactions` | Lançamentos de gastos (crédito, Pix, dinheiro, boleto), categorização    | ⏳ planejado   |
| `reporting`    | Consolidação e relatórios (read models)                                  | ⏳ planejado   |
| `gateway`      | API Gateway (roteamento, validação de token, CORS)                       | ⏳ planejado   |

Frontend: `apps/web` (SPA React + TS) — ✅ implementado (login OAuth2 + PKCE).

> O `gateway` entra quando houver serviços de recurso (accounts/transactions):
> hoje a SPA fala direto com o `identity` para autenticar.

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
guardando os tokens em `sessionStorage`.

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

## Padrões do monorepo

- **Central Package Management** (`Directory.Packages.props`) fixa versões de pacotes.
- **`Directory.Build.props`** define TFM, nullable e implicit usings globalmente.
- **`NuGet.config`** restringe a restauração ao feed público nuget.org.
- Prefixo de namespaces: `Expense.Tracking.<Serviço>.<Camada>`.
