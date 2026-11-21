# ADR-0003: Identity em repositório próprio, consumido como imagem versionada (épico IDX)

- **Status:** Proposto
- **Data:** 2026-11-21 (proposta)
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico IDX (história IDX-1), `docs/backlog.md`

## Contexto

O expense.tracking é de microserviços em tempo de execução: `identity`, `accounts`,
`transactions`, `reporting` e `gateway` rodam em processos e containers próprios, cada um com o seu
banco. Mas todos nascem, compilam, testam e são versionados no mesmo repositório. O dono do produto
quer que o projeto siga microserviços também no versionamento, começando pelo `identity` (o
servidor OAuth2 próprio), que é o serviço do qual todos os outros dependem e o que menos muda junto
com eles.

Separar o repositório só traz ganho se o `identity` puder evoluir e ser publicado sem o monorepo, e
se o monorepo puder mudar sem exigir release do `identity`. Hoje nenhuma das duas coisas é verdade.

### Achados no código

- **Sem referência de código entre serviços.** Nenhum projeto do `identity` referencia projeto fora
  de `services/identity`, e nenhum serviço referencia o `identity`. Os outros serviços dependem só
  do **contrato OIDC**: discovery (`/.well-known/openid-configuration`), JWKS, `/oauth/authorize`,
  `/oauth/token` e as claims `sub` e `scope`.
- **Build compartilhado na raiz.** `Directory.Build.props` (net10.0, nullable,
  `InvariantGlobalization`), `Directory.Packages.props` (versões centrais) e `NuGet.config` ficam na
  raiz. O `Dockerfile` do `identity` usa a raiz do monorepo como contexto para enxergá-los.
- **Configuração dos consumidores fixa no código.** O `IdentitySeeder` cria o cliente da SPA
  (`expense-tracking-spa`, redirect `http://localhost:5173/callback`, escopos `expenses.*`), o
  cliente `expense-tracking-service` com os escopos `events.read` (EVT-1) e `accounts.scopes.read`
  (EVT-4) e segredo `dev-service-secret`, e o usuário `demo@expense.local` (o seed só roda com
  `Seed:Enabled`, ligado no compose). O CORS já lê `Cors:AllowedOrigins`, mas tem a origem da SPA
  (`http://localhost:5173`) como padrão no código e no `appsettings.json`, e a tela de login mostra
  as credenciais do usuário demo. Os escopos do EVT só existiram porque o código do `identity`
  foi alterado junto.
- **Tudo no compose do monorepo.** O `docker-compose.yml` faz o build do `identity`; os outros 4
  serviços e a SPA apontam para `http://identity:8080` (JWKS interno, token de serviço) e para o
  issuer público `http://localhost:5080`.
- **CI e gate.** A matriz do `ci.yml` tem `identity`; o ruleset `main gate (CI-3)` exige o check
  `build & test (identity)`; o `e2e.yml` sobe o stack com `--build`, e o login do Cypress passa pela
  tela do `identity`.
- **Histórico pequeno:** 8 commits tocam `services/identity`.

## Opções consideradas

### Opção A — Repositório próprio, imagem Docker publicada no GHCR

O `identity` vai para `leandrorf/expense-identity` com CI próprio e publica
`ghcr.io/leandrorf/expense-identity:X.Y.Z` a cada tag. O compose do monorepo usa a imagem com versão
fixada.

- **Prós:** o monorepo nem compila o `identity`; versões explícitas e reversíveis (trocar a tag);
  é o mesmo jeito de consumir qualquer serviço de terceiros; o CI do monorepo fica mais curto.
- **Contras:** dois repositórios para manter (CI, rulesets, versões de pacote que podem divergir);
  testar uma mudança que atravessa os dois exige publicar ou fazer build local; um registry entra
  na cadeia.

### Opção B — Repositório próprio como submódulo git

O monorepo referencia o repositório do `identity` como submódulo e continua fazendo o build dele.

- **Prós:** fácil de ver e alterar o código dos dois juntos.
- **Contras:** mantém o acoplamento de build que se quer remover (o CI do monorepo compila o
  `identity`); submódulos são frágeis no dia a dia (commit apontado esquecido, clone sem
  `--recursive`). **Descartada.**

### Opção C — Repositório próprio, o CI do monorepo clona e faz o build

- **Contras:** mesma dependência de build da opção B, sem versão explícita (o CI pegaria o `main` do
  outro repositório, sem reprodutibilidade). **Descartada.**

### Opção D — Manter no monorepo

- **Prós:** nenhum trabalho; mudanças atravessadas num PR só.
- **Contras:** não atende ao pedido do dono do produto. **Descartada.**

## Decisão

**Vamos usar a Opção A: o `identity` vai para o repositório `leandrorf/expense-identity` e o
monorepo passa a consumi-lo pela imagem publicada no GHCR, com versão fixada.**

Regras aceitas junto com esta decisão:

1. **Versão:** SemVer por tag git (`vX.Y.Z`), gerando a imagem com a mesma tag. O monorepo fixa a
   versão exata, nunca `latest`, e troca de versão por PR, que roda o E2E. Mudança incompatível no
   contrato OIDC (rotas, claims, formato de token, escopos existentes) é versão **major**.
2. **Visibilidade:** repositório e imagem **públicos**, como o monorepo, para o compose e o CI
   baixarem a imagem sem credencial.
3. **Histórico:** o histórico de `services/identity` vai junto (`git filter-repo`). A pasta só sai do
   monorepo quando o compose já estiver usando a imagem publicada.
4. **Configuração dos clientes é do consumidor.** Clientes OAuth (id, tipo, redirect, escopos,
   grants, PKCE, segredo), usuário demo, origens de CORS e o texto de demo da tela de login passam a
   vir de configuração (`Seed:Clients`, `Seed:Users`, `Cors:AllowedOrigins`, sem padrão escondido no
   código nem no `appsettings.json`). O seed continua idempotente e reconcilia escopos de clientes
   existentes. Sem configuração, o `identity` sobe sem cliente, sem usuário e sem origem de CORS. O compose do monorepo declara os clientes dele. Isso é feito **antes** da
   extração, ainda no monorepo (IDX-2), para separar mudança de comportamento de mudança de lugar.
   Segredos (segredo do cliente de serviço, senha do demo) vêm de variável de ambiente; os valores
   de desenvolvimento ficam só nos defaults do compose, como hoje.
5. **Build autônomo:** o repositório novo tem cópias próprias de `Directory.Build.props`,
   `Directory.Packages.props` (só os pacotes que usa) e `NuGet.config`, e um `Dockerfile` com a raiz
   dele como contexto.
6. **CI e gate:** o repositório novo tem build e testes no CI e o `main` protegido por ruleset no
   padrão do CI-3. No monorepo, a matriz deixa de ter `identity` e o ruleset deixa de exigir
   `build & test (identity)` **no mesmo PR** que remove a pasta, para o gate não esperar um check
   que não existe mais.
7. **Desenvolvimento local:** o compose usa a imagem por padrão; um arquivo opcional
   (`docker-compose.identity-local.yml`) faz o build a partir de um clone local do repositório do
   `identity`, indicado por variável, para testar mudanças nos dois juntos.
8. **Proteção do contrato:** o repositório do `identity` roda no CI um teste de contrato OIDC contra
   o container (discovery, JWKS, Authorization Code + PKCE, refresh com rotação, client credentials,
   claims `sub`/`scope`). O E2E do monorepo continua sendo a prova ponta a ponta com a versão fixada.
9. **Atualização:** o Dependabot do monorepo (ecossistema `docker-compose`) abre PR quando sair
   versão nova da imagem.

**Quando reavaliar:** se as mudanças passarem a atravessar os dois repositórios com frequência
(sinal de fronteira errada), ou antes de extrair outro serviço. Extrair os demais serviços fica fora
deste épico e, se vier, entra numa nova ADR que use a experiência desta.

## Consequências

- **Positivas:**
  - O `identity` evolui, testa e publica sozinho; o CI do monorepo deixa de compilá-lo.
  - Cliente ou escopo novo num serviço do monorepo vira mudança de configuração do próprio monorepo,
    sem release do `identity`.
  - O contrato OIDC fica explícito e testado nos dois lados.
- **Negativas:**
  - Dois repositórios, dois CIs e dois rulesets para manter; versões de pacote podem divergir.
  - Mudança que atravessa os dois vira dois PRs (publicar o `identity` e depois subir a versão no
    monorepo).
  - O GHCR entra na cadeia: se a imagem não estiver disponível, o stack e o E2E não sobem.
- **Neutras / a observar:**
  - A chave de assinatura dos tokens continua sendo gerada a cada subida quando não há
    `PrivateKeyPem` (gotcha já conhecido de 401 até o JWKS atualizar). Não muda com a separação, mas
    fica mais visível quando a imagem é trocada; tratar em história própria se incomodar.
  - O `demo@expense.local` e o cliente da SPA continuam existindo, mas como configuração de
    desenvolvimento do monorepo, não como parte do produto `identity`.
