# ADR-0013: Metas de gastos e de economia no reporting, com o resultado da previsão e objetivos medidos por aportes (épico MET)

- **Status:** Aceito
- **Data:** 2026-11-28
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico MET (história MET-1), `docs/backlog.md`, ADR-0002 (orçamentos no reporting,
  limite com vigência e limiar de atenção), ADR-0005 (previsão de caixa), ADR-0008 (recorrências na
  previsão), ADR-0009 (lembretes), ADR-0010 (reajuste estimado)

## Contexto

O app mostra o que entrou e saiu (Resumo), quanto cada categoria pode gastar (orçamentos) e o que vem
pela frente (previsão). Ele não responde "quanto sobra para guardar este mês?" nem "estou no caminho
daquele objetivo?". O usuário quer uma meta do mês inteiro, sem categoria (guardar um valor, guardar um
percentual do que entra ou gastar no máximo um teto), e objetivos com valor e prazo.

### Achados no código

- **Os orçamentos já são metas por categoria.** `Budget` (ADR-0002) tem limite por categoria e escopo, com
  vigências, encerramento a partir de um mês e situação do mês com atenção a partir de 80%. O dado é
  escrito pelo usuário e fica no `expense_reporting`, fora da projeção.
- **O resultado do mês já existe, mas só na previsão.** O `CashForecastCalculator` calcula, por escopo e
  mês, `Result = entradas − saídas fora do cartão − faturas que vencem no mês`. Inclui as recorrências
  ainda não lançadas como previsto (REC-4) e o reajuste como estimado (RJT-8). A previsão começa no mês
  atual.
- **Não há transferência entre contas.** `TransactionType` é `Expense` ou `Income`. Guardar dinheiro numa
  conta de reserva hoje vira um gasto numa conta e uma receita na outra.
- **O saldo das contas é opcional** (INV-13): só entram as contas com saldo informado.
- **O gateway** manda `/api/reports/**` e `/api/budgets/**` para o `reporting`; uma rota nova precisa de
  uma entrada no YARP.

## Opções consideradas

### 1. Onde mora

- **(a) No `reporting`**, em tabelas próprias, como os orçamentos.
- **(b) Serviço novo `goals`:** teria de duplicar a previsão ou chamá-la a cada consulta.
- **(c) No `accounts`:** não tem os lançamentos.

### 2. O que é "guardar" no mês

- **(a) O resultado da previsão** (entradas − saídas fora do cartão − faturas que vencem no mês).
- **(b) Entradas − gastos por competência** (a compra no cartão conta no mês da compra): não bate com o
  caixa nem com a previsão.
- **(c) Variação do saldo das contas:** só funciona para quem informou o saldo de todas as contas.

### 3. Como o objetivo mede o progresso

- **(a) Aportes registrados no objetivo.**
- **(b) Saldo de uma conta vinculada:** sem transferência entre contas, a ida do dinheiro vira gasto e
  distorce o Resumo, os orçamentos e o resultado.
- **(c) Soma dos resultados mensais desde o início:** mistura sobra com o que foi de fato guardado.

## Decisão

**As metas moram no `reporting` (opção 1a). A meta mensal compara o resultado do mês, o mesmo da
previsão (opção 2a), com uma das três formas. Os objetivos medem o progresso pelos aportes registrados
neles (opção 3a). Aporte não é lançamento.** As regras abaixo valem para as histórias MET-2 a MET-11.

1. **Lugar.** Tabelas próprias no `expense_reporting`, fora da projeção (reconstruir a projeção não
   apaga metas): `monthly_goals` (dono, escopo, início, encerramento) com `monthly_goal_terms` (meta, mês
   de início, forma, valor) e `savings_objectives` com `savings_contributions`. Moeda BRL, como os
   orçamentos.
2. **Resultado do mês.** Por escopo (PF ou PJ): entradas − saídas fora do cartão − faturas que vencem no
   mês, pelo mesmo cálculo da previsão (`CashForecastCalculator`), lido da projeção e das faturas do
   reporting.
   - Meses passados contam só o que foi lançado.
   - O mês atual e os futuros somam o lançado e o previsto: recorrências ainda não lançadas, com o
     reajuste estimado, e o total das faturas que vencem no mês, pagas ou não.
   - Para o mesmo mês e escopo, o resultado é igual ao `Result` da previsão.
3. **Meta mensal.** No máximo uma meta aberta por dono e escopo (índice único, como os orçamentos). Cada
   vigência tem uma forma, e a forma pode mudar de uma vigência para outra:
   - **`SaveAmount`**: resultado ≥ valor (R$ 0,01 a R$ 1.000.000.000).
   - **`SavePercent`**: resultado ≥ percentual × entradas do mês (percentual inteiro de 1 a 100).
   - **`SpendCeiling`**: saídas fora do cartão + faturas do mês ≤ valor.

   Como no limite dos orçamentos, uma mudança vale a partir de um mês, em qualquer mês a partir do início, e
   não altera os anteriores. Encerrar vale a partir de um mês. Erros (400):
   - `goal.kind_invalid`, `goal.amount_invalid`, `goal.percent_invalid`, `goal.month_invalid`;
   - `goal.month_before_start`, `goal.closed`, `goal.already_closed`, `goal.scope_invalid`.

   Mexer na meta de outro dono responde 404 `goal.not_found`.
4. **Alvo e "ainda pode gastar".** O alvo do mês é o valor (guardar), o percentual × entradas do mês
   (percentual) ou o teto. "Ainda pode gastar" nunca é negativo:
   - para guardar: entradas − saídas − faturas − alvo;
   - para o teto: teto − saídas − faturas.

   Uma compra no cartão entra no mês de vencimento da fatura dela, como na previsão, e a tela diz isso.
   Exemplo: meta de guardar R$ 1.000, entradas de R$ 5.000 e saídas mais faturas de R$ 3.500 dão resultado
   de R$ 1.500 e "ainda pode gastar" de R$ 500.
5. **Situação.**
   - **Mês atual e futuros:**
     - **fora**, se o resultado fica abaixo do alvo (guardar) ou o gasto passa do teto;
     - **atenção**, se "ainda pode gastar" é menor que 20% do alvo (guardar) ou do teto (o mesmo limiar de
       80% dos orçamentos);
     - **dentro**, nos outros casos.
   - **Meses passados:** **atingida** ou **não atingida**.
   - **Mês sem meta:** sem situação.

   No exemplo da regra 4, um gasto de R$ 350 deixa R$ 150, ou seja, 15% do alvo, e a situação passa a
   **atenção**.
6. **Objetivos e aportes.**
   - **Objetivo:** escopo, nome (1 a 60 caracteres), alvo (> 0) e prazo opcional (mês, igual ou depois do
     mês atual ao criar). Pode ser editado (nome, alvo, prazo), arquivado e reaberto; o objetivo arquivado
     sai da lista padrão (`?includeArchived=true`).
   - **Aporte:** data (até hoje, no fuso do app), valor diferente de zero (negativo = retirada) e observação
     opcional (até 120 caracteres). Um aporte pode ser excluído.
   - **Saldo:** o guardado (soma dos aportes) nunca fica negativo, nem ao registrar nem ao excluir.
   - **Aporte não é lançamento:** não aparece no Resumo, nos orçamentos, na previsão nem no resultado do
     mês.
   - **Erros:**
     - 400: `goal.name_invalid`, `goal.target_invalid`, `goal.deadline_invalid`,
       `goal.contribution_invalid`, `goal.contribution_in_future`, `goal.balance_negative`,
       `goal.archived`;
     - 404: `goal.not_found` e `goal.contribution_not_found`.
7. **Ritmo.**
   - **falta** = max(alvo − guardado, 0).
   - **precisa por mês** = falta ÷ meses do mês atual até o prazo, contando os dois.
   - **ritmo** = aportes líquidos do primeiro dia de dois meses atrás até hoje ÷ 3. Se o objetivo é mais
     novo, divide pelos meses desde a criação, no mínimo 1.
   - **previsão de chegada** = mês atual + ⌈falta ÷ ritmo⌉ meses. Sem previsão se o ritmo for zero ou
     negativo.
   - Valores arredondados em 2 casas, com o meio para longe do zero.

   Situações:
   - **atingido**: guardado ≥ alvo.
   - **prazo vencido**: o prazo passou sem atingir.
   - **atrasado**: ritmo < precisa por mês.
   - **no ritmo**: os outros casos.

   Objetivo sem prazo mostra só ritmo e previsão de chegada, com situação **atingido** ou **no ritmo**.

   Exemplo: alvo de R$ 30.000 até dez/2027 com R$ 6.000 guardados em nov/2026.
   - Falta R$ 24.000 e precisa de R$ 1.714,29 por mês (14 meses).
   - Com R$ 1.000 por mês nos últimos 3 meses, a situação é **atrasado** e a chegada fica em nov/2028.
8. **Meta mensal e objetivos são independentes.** A lista de objetivos traz, por escopo, a soma do
   "precisa por mês" dos objetivos em andamento com prazo. A SPA pode usá-la como valor da meta mensal
   (Could, MET-10), sem amarrar uma à outra.
9. **Rotas** (escopos `expenses.read` para ler e `expenses.write` para escrever, como os orçamentos;
   `scope` = `personal` ou `business`, `month` = `yyyy-MM`):
   - **Meta mensal:**
     - `GET /goals/monthly`: as metas com as vigências;
     - `PUT /goals/monthly/{scope}`: `{ kind, amount?, percent?, fromMonth }`, cria ou muda;
     - `DELETE /goals/monthly/{scope}?fromMonth=`: encerra;
     - `GET /goals/monthly/status?month=`: PF e PJ, com alvo, entradas, saídas, faturas, resultado,
       "ainda pode gastar" e situação; o padrão é o mês atual;
     - `GET /goals/monthly/history?months=`: meses fechados, padrão 6, de 1 a 24.
   - **Objetivos:**
     - `GET /goals/objectives`;
     - `POST /goals/objectives`;
     - `PUT /goals/objectives/{id}`;
     - `POST /goals/objectives/{id}/archive` e `.../unarchive`;
     - `POST /goals/objectives/{id}/contributions`;
     - `DELETE /goals/objectives/{id}/contributions/{contributionId}`.

   No gateway, uma rota nova `/api/goals/**` aponta para o `reporting`, com o mesmo corte do prefixo
   `/api` dos orçamentos. Nada muda no `identity`.
10. **Aviso** (Could, MET-9). Novo `ReminderKind` no cálculo dos lembretes do NTF, no app e no resumo por
    e-mail:
    - meta do mês atual **fora**;
    - objetivo **atrasado** a 3 meses ou menos do prazo.

    O aviso some na próxima leitura, quando a situação melhora.

## Consequências

- **Positivas:**
  - Uma pergunta que o usuário fazia de cabeça ("quanto ainda posso gastar e guardar o que quero?") vira
    um número na tela, igual ao da previsão.
  - Objetivos com prazo e ritmo sem planilha à parte.
  - Sem serviço, evento nem projeção novos; o padrão de vigências e o limiar de atenção já são conhecidos.
- **Negativas:**
  - O `reporting` ganha mais dado escrito pelo usuário (quatro tabelas).
  - Aporte é registro manual: o usuário guarda no banco e registra no app.
  - O resultado de meses passados passa a ser calculado também fora da previsão (mesma regra,
    reaproveitada).
- **Neutras / a observar:**
  - Quando houver transferência entre contas, reabrir a decisão 3 para medir o objetivo pelo saldo de uma
    conta vinculada.
  - A compra no cartão pesa no mês do vencimento da fatura: quem pensa por competência pode estranhar;
    a tela explica.
