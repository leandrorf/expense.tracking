# ADR-0002: Orçamentos por categoria ficam no reporting (épico BDG)

- **Status:** Aceito
- **Data:** 2026-11-20 (proposta e aceite)
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico BDG (história BDG-1), `docs/backlog.md`,
  [ADR-0001](0001-transporte-de-eventos-transactions-reporting.md)

## Contexto

O épico BDG permite ao usuário definir um limite mensal de gasto por categoria, separado para PF e
PJ, e acompanhar no mês quanto consumiu, quanto resta e o que estourou. Para isso o sistema precisa
de duas coisas:

1. **Guardar o orçamento**, que é um dado escrito pelo usuário (categoria, escopo, valor, moeda,
   vigência).
2. **Calcular o consumido**, somando os gastos do mesmo dono, categoria, escopo e mês.

Os gastos, com escopo PF/PJ e categoria, já existem numa forma pronta para consulta: a projeção
`projected_transactions` do `reporting` (banco `expense_reporting`), alimentada pelo feed de eventos
do `transactions` (ADR-0001). Hoje o `reporting` só lê: não tem nenhum dado escrito pelo usuário.

### Achados no código

- **Categoria é texto livre** (`Transaction.Category`, `Trim()` e no máximo 60 caracteres). Não há
  catálogo. O `SummaryCalculator` agrupa por comparação ordinal, então "Alimentação", "alimentação"
  e "Alimentacao" viram linhas diferentes; um filtro no MySQL (collation `utf8mb4_0900_ai_ci`)
  trataria as três como iguais.
- **Escopo PF/PJ** já está em cada linha da projeção; `Unknown` só sobra para conta inexistente.
- **Moeda:** `Money` aceita qualquer código ISO 4217; o Resumo assume uma moeda só.
- O `reporting` autoriza por `expenses.read` ou `expenses.write`, e o gateway já roteia
  `/api/reports/**` para ele.

## Opções consideradas

### Opção A — Tabelas de orçamento no `reporting`

O orçamento fica em tabelas próprias no `expense_reporting`, ao lado da projeção. O consumido é uma
consulta local à projeção.

- **Prós:** sem chamada entre serviços na leitura; sem novo consumidor do feed; sem banco, container
  ou check de CI novos; o status fica de pé com o `transactions` fora (servindo a projeção).
- **Contras:** o `reporting` deixa de ser só modelo de leitura e passa a ter dado escrito pelo
  usuário (backup, migração e validação de escrita passam a importar nele); mistura dois contextos
  num serviço.

### Opção B — Serviço novo `budgets`

Serviço próprio com banco `expense_budgets`.

- **Prós:** contexto delimitado limpo; o `reporting` segue só leitura.
- **Contras:** para calcular o consumido, ou chama o `reporting` na hora da leitura (acoplamento
  síncrono e dupla latência), ou vira o **segundo consumidor** do feed, que é o gatilho listado na
  ADR-0001 para reabrir o transporte de eventos. Também acrescenta serviço, banco, imagem, rota e um
  required check no CI.

### Opção C — No `accounts`

- **Contras:** o `accounts` não tem os lançamentos; cairia nos mesmos problemas da opção B, sem o
  ganho de um contexto limpo. **Descartada.**

## Decisão

**Vamos usar a Opção A: os orçamentos ficam no `reporting`,** em tabelas próprias no
`expense_reporting`, dentro de um módulo separado (`Budgets`) nas camadas de domínio, aplicação e
infraestrutura do serviço.

Regras do modelo, aceitas junto com esta decisão:

1. **Período:** mês civil. O orçamento é um limite **recorrente** com vigência: começa num mês;
   alterar o valor cria nova vigência a partir de um mês escolhido, sem mudar os meses anteriores;
   encerrar define o último mês em que vale.
2. **Unicidade:** um orçamento ativo por dono + escopo + categoria normalizada.
3. **Correspondência de categoria:** chave normalizada no domínio, aplicada ao orçamento e aos
   lançamentos: trim, espaços internos colapsados, sem diferença de maiúsculas nem de acentos. Não
   depende da collation do banco. Catálogo gerenciado fica fora do MVP.
4. **Consumido:** soma dos lançamentos do tipo gasto da projeção, do mesmo dono, escopo e moeda,
   com data de ocorrência no mês (compra no cartão conta na data da compra), em todas as contas e
   meios de pagamento do escopo. Receitas e escopo `Unknown` não contam.
5. **Status:** calculado na leitura, por função pura no domínio: Dentro (< 80%), Atenção (≥ 80%),
   Estourado (> 100%). Sem notificação ativa.
6. **Moeda:** o orçamento tem moeda (padrão BRL) e só soma lançamentos da mesma moeda.
7. **API e segurança:** rotas em `/budgets` no `reporting`, expostas pelo gateway em
   `/api/budgets/**`. Escrita exige `expenses.write`; leitura aceita `expenses.read` ou
   `expenses.write`. Recurso de outro dono responde 404.

**Quando preferir a Opção B:** se o orçamento ganhar regras próprias pesadas (catálogo de
categorias, notificações, metas de economia), ou se aparecer outro motivo para um segundo
consumidor do feed; nesse caso a mudança vem numa nova ADR, revisando também a ADR-0001.

## Consequências

- **Positivas:**
  - O consumido sai de uma consulta local, consistente com o Resumo (mesma projeção, mesma
    defasagem).
  - Nenhuma infraestrutura nova; a ADR-0001 continua válida sem mudança.
- **Negativas:**
  - O `reporting` passa a guardar dado do usuário: a reconstrução da projeção (zerar o checkpoint)
    **não pode** apagar as tabelas de orçamento.
  - A regra de normalização de categoria precisa ser a mesma no orçamento e no consumo, com teste
    dedicado.
- **Neutras / a observar:**
  - A consulta de consumo filtra por chave normalizada; se o volume crescer, guardar a chave
    normalizada na projeção (coluna indexada) em vez de normalizar na leitura.
  - Categoria livre divergente pode ficar fora do consumido; a BDG-7 (sugestões) e a BDG-8 (gastos
    sem orçamento) reduzem e expõem esse risco.
