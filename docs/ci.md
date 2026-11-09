# Integração Contínua (CI)

Pipeline de CI do expense.tracking no GitHub Actions. Épico **CI** do backlog
(`docs/backlog.md`).

## Workflows

### `ci.yml` — build + testes (CI-1)
- **Gatilho:** todo `push` e todo `pull_request`.
- **O que faz:** para cada serviço (accounts, gateway, identity, reporting,
  transactions), roda `dotnet restore` → `dotnet build -c Release` →
  `dotnet test` na respectiva solução `.slnx`. Qualquer teste reprovado falha o job.
- **Matriz:** um job por serviço (`fail-fast: false`), então uma falha num
  serviço não esconde o resultado dos demais.
- **Sem banco:** os testes de integração usam EF InMemory + fakes
  (`*ApiFactory`), não dependem de MySQL nem de outros serviços no ar.
- **Artefatos:** resultados `.trx` publicados por serviço.

### `e2e.yml` — Cypress end-to-end (CI-2)
- **Gatilho:** `pull_request` para `main` e `workflow_dispatch` (manual).
- **O que faz:** `docker compose up -d --build` sobe o stack completo
  (MySQLs + identity + accounts + transactions + reporting + gateway + web),
  aguarda os health (`identity /.well-known/openid-configuration`,
  `gateway /health`, `web` em `:5173`) e roda `cypress run` headless contra a SPA.
- **Credenciais:** o seed do identity cria o usuário demo
  (`demo@expense.local` / `Password123!`), que é o default dos specs.
- **Em falha:** publica screenshots do Cypress e despeja `docker compose logs`.
- **Sempre:** derruba o stack (`docker compose down -v`).

## Branch protection (CI-3)

Objetivo: **PR sem CI verde não faz merge** em `main`.

> Pré-requisito: os workflows precisam ter rodado ao menos uma vez num PR para
> que o GitHub conheça os nomes dos checks. Os contextos exigidos são os nomes
> dos jobs: `build & test (accounts)`, `build & test (gateway)`,
> `build & test (identity)`, `build & test (reporting)`,
> `build & test (transactions)` e `Cypress E2E`.

### Via UI
`Settings → Branches → Add branch ruleset` (ou *Branch protection rule*) para `main`:
- ☑ Require a pull request before merging
- ☑ Require status checks to pass before merging → **Require branches to be up to date** + marque os 6 checks acima
- ☑ Do not allow bypassing the above settings (opcional, recomendado)

### Via `gh` CLI
Troque `OWNER/REPO` e rode (precisa de permissão de admin no repo):

```bash
gh api -X PUT repos/OWNER/REPO/branches/main/protection \
  -H "Accept: application/vnd.github+json" \
  --input - <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": [
      "build & test (accounts)",
      "build & test (gateway)",
      "build & test (identity)",
      "build & test (reporting)",
      "build & test (transactions)",
      "Cypress E2E"
    ]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": { "required_approving_review_count": 1 },
  "restrictions": null
}
JSON
```

> Observação: a `main` deste repositório ainda está vazia — o projeto vive na
> branch de feature. Definir o alvo de PR/merge para `main` é um passo à parte,
> anterior a ligar a branch protection de fato.
