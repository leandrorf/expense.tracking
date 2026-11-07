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
| `accounts`     | Contas (PF/PJ), cartões de crédito, meios de pagamento                   | ⏳ planejado   |
| `transactions` | Lançamentos de gastos (crédito, Pix, dinheiro, boleto), categorização    | ⏳ planejado   |
| `reporting`    | Consolidação e relatórios (read models)                                  | ⏳ planejado   |
| `gateway`      | API Gateway (roteamento, validação de token, CORS)                       | ⏳ planejado   |

Frontend: `apps/web` (SPA React) — ⏳ planejado.

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

### Simplificações atuais (a evoluir)

- `/oauth/authorize` retorna erros como JSON em vez de redirecionar com `error=` ao
  `redirect_uri`. Será refinado ao integrar a SPA (fluxo de navegador).
- Não há tela de consentimento (clientes são first-party / auto-consentidos).
- A chave de assinatura é efêmera se `OAuth:SigningKey:PrivateKeyPem` não for configurada
  (apenas DEV). Em produção, injetar a chave via configuração/secret.

## Padrões do monorepo

- **Central Package Management** (`Directory.Packages.props`) fixa versões de pacotes.
- **`Directory.Build.props`** define TFM, nullable e implicit usings globalmente.
- **`NuGet.config`** restringe a restauração ao feed público nuget.org.
- Prefixo de namespaces: `Expense.Tracking.<Serviço>.<Camada>`.
