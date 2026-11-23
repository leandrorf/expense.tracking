# ADR-0008: Lançamentos recorrentes como regras que só projetam, com ocorrências lançadas ou puladas pelo usuário (épico REC)

- **Status:** Aceito
- **Data:** 2026-11-23
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico REC (história REC-1), `docs/backlog.md`, ADR-0001 (outbox e feed de eventos),
  ADR-0002 (orçamentos no reporting), ADR-0004 (importação de extrato e fatura), ADR-0005 (fatura de
  cartão e previsão de caixa), ADR-0006 (exportação contábil), ADR-0007 (edição de lançamentos)

## Contexto

Salário, pró-labore, aluguel, condomínio, escola, internet e assinaturas se repetem todo mês, mas o
produto só conhece o que já foi lançado. A previsão de caixa (INV-8) e o saldo projetado (INV-13)
subestimam o futuro, e o único jeito de "prever" hoje é lançar cada mês à mão com data futura, o que
coloca no Resumo, nos orçamentos e na exportação contábil valores que ainda não aconteceram. O épico
REC deixa o usuário cadastrar uma vez o que se repete, faz a previsão contar com isso e mantém o resto
do produto vendo só o que foi lançado.

### Achados no código

- **A previsão só conhece o lançado.** `CashForecastCalculator` soma lançamentos fora do cartão pela
  data e o cartão pela fatura, no mês de vencimento; `ProjectedBalanceCalculator` parte do saldo
  informado. Os dois leem a projeção do `reporting`, que nasce dos eventos do `transactions`.
- **Todo lançamento conta em tudo.** Resumo, orçamentos, fatura, limite e a exportação contábil (que lê
  o `transactions` direto) contam qualquer lançamento que exista, inclusive com data futura.
- **A importação deduplica só pela chave externa** (`ExternalId` por dono e conta, ADR-0004). Um
  lançamento criado pelo sistema não tem chave e duplicaria com o extrato importado.
- **O `reporting` pula tipo de evento desconhecido e o checkpoint avança** (ADR-0001; visto na LNC-4):
  evento novo exige implantar o `reporting` antes ou junto do `transactions`.
- **"Hoje" só tem fuso no `reporting`** (`Reports:TimeZone`, padrão `America/Sao_Paulo`).
- **O escopo PF/PJ é da conta**, consultado no `accounts` na criação e copiado no evento.
- **Parcelamento é outra coisa** (ADR-0005, decisão 5): uma compra, N parcelas nas faturas.

## Opções consideradas

### 1. Modelo

- **(a) Regra que só projeta:** a recorrência alimenta a previsão e nunca vira lançamento sozinha; o
  usuário lança cada ocorrência (com o valor real) ou a pula.
- **(b) Lançamento automático:** um worker cria o lançamento no dia. Duplica com o extrato importado e
  grava como real um valor que muda (luz, telefone).
- **(c) Lançamento previsto:** o lançamento nasce com status "previsto" e é "efetivado" depois. Resumo,
  orçamentos, fatura, limite, exportação e todos os eventos passariam a filtrar o status, o mesmo
  risco que a ADR-0005 evitou ao não fazer do pagamento de fatura um lançamento.

### 2. Onde mora e como chega ao reporting

- **(a) No `transactions`, com eventos próprios no outbox;** o `reporting` projeta as regras.
- **(b) No `transactions`, e o `reporting` lê por HTTP** na consulta da previsão: volta a acoplar a
  leitura ao `transactions`, o que o épico EVT desfez.
- **(c) No `reporting`:** lançar a ocorrência teria de atravessar serviços para criar o lançamento.

### 3. Identidade da ocorrência

- **(a) Regra + data agendada.** Mudar o dia da regra depois de lançar abriria uma "nova" ocorrência no
  mesmo mês, contada de novo na previsão.
- **(b) Regra + período** (`yyyy-MM` na mensal, `yyyy` na anual). Uma ocorrência por período, seja
  qual for o dia em que foi lançada.

## Decisão

**A recorrência é uma regra no `transactions` que só projeta: entra na previsão até o usuário lançar ou
pular cada ocorrência, nunca vira lançamento sozinha e chega ao `reporting` por eventos próprios no
outbox.** As regras abaixo valem para as histórias REC-2 a REC-10.

1. **Modelo** (opção 1a). Lançar uma ocorrência cria um **lançamento comum**, o mesmo
   `TransactionRegistered` de sempre, que Resumo, orçamentos, fatura e exportação veem como qualquer
   outro. A regra em si não aparece em lugar nenhum além da previsão e das telas de recorrência.
2. **Lugar e eventos** (opção 2a). Tabelas `recurrences` e `recurrence_occurrences` no
   `expense_transactions`. Eventos no outbox, no mesmo commit da mudança:
   - `RecurrenceSaved` (v1): estado completo da regra (`recurrenceId`, `ownerUserId`, `accountId`,
     `accountScope`, `transactionType`, `paymentMethod`, `creditCardId`, `description`, `category`,
     `amount`, `currency`, `frequency`, `startsOn`, `endsOn`, `recurrenceVersion`), na criação e em
     cada edição. Leva o escopo como o `TransactionRegistered` (a conta não muda).
   - `RecurrenceDeleted` (v1): `recurrenceId`, `ownerUserId`.
   - `RecurrenceOccurrenceClosed` (v1): `recurrenceId`, `ownerUserId`, `period`, `status`
     (`Launched` ou `Skipped`) e `transactionId` (só no lançado).
   - `RecurrenceOccurrenceReopened` (v1): `recurrenceId`, `ownerUserId`, `period`.
   
   O `TransactionRegistered` **não muda**: o vínculo da ocorrência com o lançamento viaja no evento da
   ocorrência. **Implantação:** o `reporting` que entende esses tipos sobe antes do `transactions` que
   os publica; se a ordem se inverter, zerar o checkpoint (reconstrução, EVT-2) recupera.
3. **Frequência e agenda.** **Mensal** (Must) e **anual** (Should). A data de início (`startsOn`) é a
   âncora: a mensal repete no dia de `startsOn` todo mês, a anual no dia e mês de `startsOn` todo ano.
   Mês (ou fevereiro) sem aquele dia usa o **último dia** do mês. O fim (`endsOn`) é opcional e
   inclusivo. Semanal, quinzenal e "N ocorrências" ficam fora.
4. **Identidade da ocorrência** (opção 3b): regra + **período** (`yyyy-MM` ou `yyyy`). Cada período tem
   no máximo uma ocorrência; `recurrence_occurrences` guarda só as **fechadas** (lançada ou pulada),
   com chave única (regra, período). As **em aberto** são calculadas da agenda, nunca gravadas.
5. **O que a regra guarda.** Os campos do lançamento (conta, tipo, meio, cartão da conta, descrição,
   categoria, valor estimado, moeda, observações) mais frequência, início e fim opcional. Valem as
   validações e os códigos de erro do lançamento (`transaction.*`); **parcelas não existem** na regra.
   Cartão e conta são conferidos no `accounts` na criação, como no lançamento. A **conta não muda**
   (400 `recurrence.account_immutable`, como a ADR-0007).
6. **Editar, encerrar e excluir.** `PUT` da regra inteira com `version` (concorrência otimista, 409
   `recurrence.concurrency_conflict`, como a ADR-0007); edição sem mudança = 200 sem evento. Editar
   muda só os **períodos em aberto**: o que foi lançado ou pulado fica como está. Encerrar = definir
   `endsOn`. Excluir a regra remove as ocorrências em aberto da previsão (`RecurrenceDeleted`) e
   **mantém** os lançamentos feitos a partir dela; o vínculo deles passa a apontar para uma regra que
   não existe mais, sem efeito em nenhuma tela.
7. **Lançar, pular e reabrir.**
   - **Lançar** um período em aberto (inclusive futuro: conta paga adiantado) cria o lançamento com os
     dados da regra, aceitando no pedido outro valor, data e observações; o lançamento grava o vínculo
     (`recurrence_id`, `recurrence_period`) e a ocorrência fecha como `Launched`. Período que não é da
     agenda (antes do início, depois do fim) = 400 `recurrence.period_invalid`; já fechado = 409
     `recurrence.occurrence_closed`.
   - **Pular** fecha o período como `Skipped`. **Desfazer o pulo** reabre.
   - **Excluir o lançamento** de uma ocorrência (pela lista ou desfazendo a importação) **reabre** o
     período no mesmo commit. Editar o lançamento não mexe na ocorrência.
8. **"A lançar".** Períodos em aberto cuja data agendada **já passou** (antes de hoje) e está nos
   **últimos 60 dias** formam a lista "A lançar", das mais antigas para as mais novas. Eles **não**
   entram na previsão: ou aconteceram e foram lançados sem vínculo, ou não aconteceram, e contar seria
   chute. "Hoje" usa o mesmo fuso do `reporting`: o `transactions` passa a ler a configuração
   `TimeZone` (padrão `America/Sao_Paulo`), e o compose passa o mesmo valor aos dois serviços.
9. **Na previsão.** Para cada regra, cada período em aberto com data agendada **de hoje em diante**,
   dentro da janela e até o fim da regra, entra como um lançamento futuro do **valor estimado**:
   fora do cartão, no mês da data; **no cartão, na fatura do mês de vencimento** pela regra de corte da
   ADR-0005 (decisão 4), à vista; receita no cartão abate a fatura (estorno), como a decisão 7 da
   ADR-0005. Entra também no saldo projetado (INV-13): fora do cartão na conta da regra, no cartão pela
   conta do cartão. Cada escopo do mês ganha `recurringIncome`, `recurringExpense` e
   `recurringInvoices`, que são a **parte** recorrente dos totais `income`, `expense` e `invoices` (os
   totais e o resultado já incluem a recorrência). A **fatura exibida e o limite disponível não mudam**.
10. **Fora da previsão.** Ocorrência em aberto não entra no Resumo, nos orçamentos, no "Sem
    orçamento", nas sugestões de categoria, na fatura, no limite nem na exportação contábil.
11. **Importação** (Should). Na prévia, uma linha sem duplicata, da mesma conta e do mesmo tipo de uma
    ocorrência em aberto (incluindo as "a lançar"), com valor até **10%** diferente do estimado e data
    até **5 dias** antes ou depois da data agendada, vem sugerida como "lançamento da recorrência
    <descrição>". Havendo mais de uma candidata, a de data mais próxima; cada ocorrência é sugerida a
    uma linha só. Confirmar cria o lançamento com a chave externa **e** o vínculo, e fecha a
    ocorrência. Continua sendo sugestão: o usuário troca para lançamento comum (ADR-0004, decisão 5).
12. **Rotas e escopos.** No `transactions`, pela rota existente do gateway (`/api/transactions/**`):
    `GET/POST /transactions/recurrences`, `GET/PUT/DELETE /transactions/recurrences/{id}`,
    `GET /transactions/recurrences/due`, `POST /transactions/recurrences/{id}/occurrences/{period}/launch`,
    `POST .../{period}/skip` e `DELETE .../{period}/skip` (desfazer o pulo). `expenses.read` para ler,
    `expenses.write` para o resto; regra de outro usuário = 404. Nenhum escopo novo.

## Consequências

- **Positivas:**
  - A previsão e o saldo projetado passam a enxergar salário, aluguel e assinaturas sem lançar nada
    antes da hora.
  - Resumo, orçamentos, fatura, limite e exportação continuam verdadeiros: só o lançado conta.
  - Lançar uma ocorrência é um lançamento comum; nada novo para quem lê lançamentos.
  - Nenhum serviço, escopo ou rota de gateway novo.
- **Negativas:**
  - Duas tabelas novas no `transactions`, duas colunas no lançamento (`recurrence_id`,
    `recurrence_period`) e quatro tipos de evento novos, com projeção nova no `reporting`.
  - Ordem de implantação: `reporting` antes do `transactions`.
  - Até a REC-9, quem importa extrato precisa pular à mão a ocorrência que veio pelo extrato, senão a
    previsão conta as duas enquanto a data não passa.
  - O valor estimado é uma estimativa: conta de luz e cartão variam, e a previsão herda o erro.
- **Neutras / a observar:**
  - Ocorrência que passou sem lançar nem pular sai da previsão e vai para "A lançar"; se o usuário
    nunca a resolver, ela some da lista depois de 60 dias.
  - Recorrência no cartão de um cartão desativado continua projetando na fatura dele até ser editada
    ou excluída.
  - O fuso passa a ser configurado em dois serviços; o compose usa um valor só para os dois.
