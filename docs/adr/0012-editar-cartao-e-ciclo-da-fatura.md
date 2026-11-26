# ADR-0012: Edição do cartão e ciclo da fatura com vigência, guardado no accounts e lido na hora pelo reporting (épico CYC)

- **Status:** Aceito
- **Data:** 2026-11-26
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico CYC (história CYC-1), `docs/backlog.md`, ADR-0005 (fatura de cartão; reabre a
  decisão 3 e substitui a regra 4), ADR-0002 (limite com vigência), ADR-0010 (valor com vigência),
  ADR-0011 (conciliação da fatura)

## Contexto

O cartão só é criado e desativado: nome, bandeira, final, limite, fechamento e vencimento são fixos. Mudar
qualquer um exige outro cartão, que não leva as compras, as parcelas, os pagamentos nem as conferências
do antigo. A ADR-0005 (decisão 3) deixou escrito: "se o cartão ganhar edição do ciclo, reabrir esta
decisão e a regra 4".

### Achados no código

- **A fatura é recalculada a cada consulta com o ciclo de hoje.** O `reporting` lê o cartão no `accounts`
  na hora e o `InvoiceCalculator` monta todas as faturas com um `CardCycle` só. Trocar o ciclo sem
  histórico reposicionaria todas as compras do passado.
- **Pagamentos, conferências e avisos são guardados pelo mês de vencimento** (`card_payments`,
  `invoice_reconciliations`, chave dos avisos do NTF). O mês de vencimento é a identidade da fatura; os
  períodos precisam continuar os mesmos no passado.
- **Quem usa o ciclo:** no `reporting`, faturas, limite, previsão (compras e recorrências no cartão),
  avisos (pelo token do usuário e pelo endpoint interno do resumo por e-mail) e a fatura sugerida na
  conciliação; no `transactions` e na SPA, o mês sugerido para o pagamento de fatura importado (só o dia de
  vencimento).
- **O `accounts` não calcula faturas nem conhece o fuso do app**, e não publica eventos.

## Opções consideradas

### 1. Como guardar a mudança de ciclo

- **(a) Ciclo com vigência:** lista de ciclos, cada um valendo a partir de um mês de vencimento.
- **(b) Trocar e recalcular tudo:** reescreve o passado (faturas pagas perdem itens).
- **(c) Guardar as faturas fechadas** (períodos e totais) e calcular só as abertas: mais estado, mais
  pontos para ficar desatualizado.

### 2. Onde o histórico mora

- **(a) No `accounts`**, junto do cartão, lido na hora pelo `reporting` (como hoje).
- **(b) Projeção no `reporting`** por evento do `accounts` (o `accounts` ainda não tem outbox).

## Decisão

**O ciclo do cartão passa a ter vigência por mês de vencimento (opção 1a), guardada no `accounts` e lida
na hora pelo `reporting` junto do cartão (opção 2a). Faturas que já fecharam nunca mudam; a fatura da troca
encadeia o último fechamento antigo ao primeiro novo. Nome, bandeira, final e limite ganham edição simples.**
As regras abaixo valem para as histórias CYC-2 a CYC-7.

1. **Ciclos.** O primeiro ciclo é o da criação e vale desde sempre (colunas atuais do cartão); as trocas
   ficam em `card_cycles` (cartão, mês de vencimento de início, dia de fechamento, dia de vencimento). O
   ciclo de uma fatura é o da última vigência com início até o mês de vencimento dela. Dias de 1 a 28, como
   na criação.
2. **Períodos encadeados** (substitui a regra 4 da ADR-0005). O fechamento e o vencimento de cada fatura
   vêm do ciclo dela; o início é o dia seguinte ao fechamento da fatura anterior (pelo ciclo da anterior).
   Uma compra cai na fatura cujo período contém a data. A fatura da troca pode ficar mais curta ou mais
   longa que um mês.
3. **A partir de quando.** A troca vale a partir de um mês de vencimento cuja fatura **ainda não fechou**
   nem pelo ciclo antigo nem pelo novo (o fechamento das duas contas é hoje ou depois), e cujo período fica
   não vazio (fechamento novo depois do fechamento da fatura anterior). Fora disso, 400
   `card.cycle_change_invalid`, com a próxima fatura possível na mensagem. "Hoje" é no fuso do app
   (`TimeZone`, o mesmo do `transactions` e do `reporting`), que o `accounts` passa a ler.
4. **Pendente.** Uma vigência cuja fatura ainda não começou (o início dela é depois de hoje) é pendente:
   trocar de novo substitui as pendentes, e `DELETE` desfaz uma. Uma vigência que já começou não sai.
5. **Leitura.** O cartão sai com `closingDay`/`dueDay` = o ciclo de hoje (quem só precisa do ciclo atual
   continua igual) e `cycles: [{ fromDueMonth, closingDay, dueDay }]` (o primeiro com `fromDueMonth` nulo),
   nas rotas que o `reporting` já chama (`/credit-cards`, `/credit-cards/{id}` com o token do usuário e
   `/internal/credit-cards` do resumo por e-mail). Mantém a ADR-0005, decisão 3: leitura na hora, sem evento
   nem projeção.
6. **A conta do ciclo nos dois serviços.** A regra pura dos períodos encadeados fica no domínio do
   `reporting` (faturas, limite, previsão, avisos, conciliação) e, para validar a troca, uma cópia pequena
   no domínio do `accounts`. As duas têm os mesmos testes de exemplo.
7. **Outros campos.** `PUT /credit-cards/{id}` corrige nome, bandeira, final e limite com as regras da
   criação, sem histórico: o limite novo vale para o disponível na hora. A conta do cartão não muda.
   Cartão desativado não é editado (400 `card.inactive`).
8. **Rotas.** `PUT /credit-cards/{id}` (dados), `PUT /credit-cards/{id}/cycle`
   (`{ closingDay, dueDay, fromDueMonth }`, `fromDueMonth` em `yyyy-MM`) e
   `DELETE /credit-cards/{id}/cycle/{fromDueMonth}`. Nada novo no gateway nem no `identity`.
9. **Sugestão do pagamento importado** (Could). O mês sugerido para o pagamento de fatura usa o vencimento
   do ciclo que vale para a data do pagamento.

## Consequências

- **Positivas:**
  - O usuário muda o vencimento quando o banco muda, sem perder o histórico do cartão.
  - As faturas fechadas, os pagamentos e as conferências continuam exatamente onde estavam.
  - Nenhum evento, projeção ou serviço novo.
- **Negativas:**
  - A regra dos períodos encadeados existe em dois serviços (validação no `accounts`, cálculo no
    `reporting`); os testes de exemplo iguais seguram a divergência.
  - O `accounts` passa a depender do fuso do app.
  - Uma tabela nova (`card_cycles`) e o contrato do cartão ganha `cycles`.
- **Neutras / a observar:**
  - A fatura da troca pode ter mais ou menos de um mês de compras; a previsão e o limite mostram isso como
    está.
  - Mudar o limite não guarda histórico: o disponível de qualquer data é calculado com o limite de hoje.
