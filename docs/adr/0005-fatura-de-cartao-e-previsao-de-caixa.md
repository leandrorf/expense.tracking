# ADR-0005: Fatura de cartão e previsão de caixa calculadas no reporting, com parcelas e pagamento de fatura (épico INV)

- **Status:** Aceito
- **Data:** 2026-11-21
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico INV (história INV-1), `docs/backlog.md`, ADR-0001 (outbox e feed de eventos),
  ADR-0002 (orçamentos no reporting), ADR-0004 (importação de extrato e fatura)

## Contexto

O cartão de crédito é o meio mais usado, mas o produto só enxerga a compra no dia em que ela
aconteceu. O épico INV mostra, para cada cartão, a fatura atual, as próximas e as fechadas, com
total, fechamento, vencimento e situação. Também registra o pagamento da fatura sem que ele vire
gasto e mostra uma previsão de caixa mês a mês, separada por escopo PF/PJ.

### Achados no código

- **O cartão já tem ciclo:** `CreditCard` (accounts) guarda `ClosingDay` e `DueDay` (1 a 28, para todo
  mês ter o dia) e `CreditLimit`. Não há edição de cartão, só criar e desativar.
- **Não existe fatura.** Nada agrupa as compras de um cartão por ciclo; o lançamento tem
  `OccurredOn` e `CreditCardId`. Resumo e orçamentos contam pela data da compra (competência), e isso
  continua certo para eles: a fatura é a visão de caixa.
- **O evento não leva o cartão.** `TransactionRegistered` (ADR-0001) tem conta, escopo, tipo, meio,
  categoria, valor, moeda e data, mas não o `CreditCardId`. A projeção do `reporting`
  (`ProjectedTransaction`) também não.
- **O `reporting` não fala com o `accounts`.** Desde o épico EVT ele só lê o feed de eventos do
  `transactions`; não conhece ciclo nem limite dos cartões.
- **Não há parcelamento.** Uma compra em 10x vira um lançamento com o valor cheio na data da compra.
- **Não há saldo de conta.** `Account` não tem saldo inicial nem corrente; o produto registra fluxos.
- **Pagamento de fatura vira gasto hoje.** A importação já deixa o crédito da fatura de fora
  (ADR-0004, decisão 6), mas a saída correspondente no extrato da conta ("PAG FATURA") é um débito
  comum e entra como gasto, assim como um pagamento lançado à mão. O mesmo dinheiro conta duas vezes.
- **Lançamento com data futura é aceito** (`Transaction.Create` não limita a data).
- **Exclusão é física e não há edição de lançamento**; a projeção acompanha a exclusão pelo
  `TransactionDeleted`.
- **Gateway:** `/api/reports/**` já vai para o `reporting` e `/api/transactions/**` para o
  `transactions`.

## Opções consideradas

### 1. Onde a fatura e a previsão são calculadas

- **(a) No `reporting`**, a partir da projeção, como os orçamentos (ADR-0002). Fatura e previsão são
  leitura e agregação; as escritas (compra, parcelas, pagamento) ficam no `transactions`. Exige levar
  o cartão e as parcelas no evento e o `reporting` conhecer o ciclo dos cartões.
- **(b) No `transactions`**, que já tem o `CreditCardId` e já chama o `accounts`. Evita mudar o
  evento, mas espalha o lado de leitura em dois serviços: a previsão precisa das faturas e de todos
  os outros lançamentos juntos, e o Resumo e os orçamentos já moram no `reporting`.

### 2. Ciclo do cartão no reporting

- **(a) Ler `GET /credit-cards` no `accounts` na hora da consulta**, com o token do usuário
  encaminhado. Poucos cartões por usuário; o ciclo não muda porque não há edição de cartão.
- **(b) Eventos de cartão do `accounts`** para uma projeção no `reporting`. Um segundo feed (outbox,
  worker, backfill) para um dado que hoje é imutável.

### 3. Parcelamento

- **(a) Fora do escopo:** a compra entra cheia na fatura da compra. Errado para fatura, limite e caixa.
- **(b) Um lançamento com número de parcelas;** a fatura distribui as parcelas nas faturas seguintes.
- **(c) N lançamentos, um por parcela.** Resumo e orçamentos passariam a ver a compra espalhada pelos
  meses (muda a competência), a exclusão precisaria apagar N lançamentos ligados, e cada parcela
  pediria categoria e descrição próprias.

### 4. Pagamento de fatura

- **(a) Lançamento de um tipo novo**, nem gasto nem receita. Resumo, orçamentos, "Sem orçamento",
  sugestões de categoria e a importação teriam de filtrá-lo em todo lugar, e um esquecimento volta a
  duplicar o gasto.
- **(b) Registro próprio**, fora dos lançamentos, com eventos próprios no outbox.

### 5. Previsão de caixa

- **(a) Fluxo mês a mês** (entradas, saídas fora do cartão, faturas e resultado), sem saldo.
- **(b) Saldo projetado**, que exige saldo inicial por conta, um dado que não existe hoje.

## Decisão

**Fatura e previsão de caixa são leitura, calculadas no `reporting` a partir da projeção; compras
parceladas e pagamentos de fatura são escritas no `transactions`, publicadas no outbox.** As regras
abaixo valem para as histórias INV-2 a INV-13.

1. **Lugar** (opção 1a): fatura, limite disponível e previsão no `reporting`. O `transactions`
   guarda a compra (com parcelas) e o pagamento de fatura e publica os eventos. Nenhum serviço novo.
2. **Contrato do evento.** `TransactionRegistered` passa à **versão 2**, com `creditCardId` (nulo
   fora do cartão) e `installments` (1 quando à vista). O `reporting` aceita v1 e v2 lado a lado; v1
   é lido como à vista e sem cartão. Os lançamentos de cartão anteriores à v2 ganham, uma vez só, um
   `TransactionCardDetailsResolved` (cartão e parcelas) gravado por um backfill no `transactions`,
   idempotente, no padrão do `TransactionAccountScopeResolved` (EVT-4). A projeção aplica esse evento
   só em linha que já existe.
3. **Ciclo do cartão no reporting** (opção 2a): o `reporting` lê os cartões do usuário no `accounts`
   (`GET /credit-cards`) na hora da consulta, com o token do usuário encaminhado (como o `transactions`
   já faz). Cartão que o `accounts` não devolve para aquele usuário é **404**. Se o cartão ganhar
   edição do ciclo, reabrir esta decisão (opção 2b) e a regra 4.
4. **Em que fatura cai uma compra.** Compra **até o dia de fechamento, inclusive**, cai na fatura que
   fecha naquele mês; **depois** dele, na do mês seguinte. O vencimento é no mesmo mês do fechamento
   quando `DueDay > ClosingDay`, e no mês seguinte quando não. A fatura é identificada por cartão e
   **mês de vencimento** (`yyyy-MM`), como o banco a chama. Sem calendário de dias úteis: fechamento e
   vencimento no fim de semana ou feriado ficam no dia cadastrado (limitação conhecida; o banco
   costuma adiar o vencimento).
5. **Parcelamento** (opção 3b): o lançamento ganha `installments` (**1 a 24**; acima de 1 só com o meio
   `CreditCard`). A parcela é `valor / n` arredondada a centavos, com a diferença de arredondamento na
   **primeira** parcela (R$ 100,00 em 3x = 33,34 + 33,33 + 33,33). A primeira parcela cai na fatura da
   compra e as seguintes nas faturas dos meses seguintes. **Resumo e orçamentos não mudam:** a compra
   conta inteira no mês da compra (competência). Só fatura, limite e previsão olham as parcelas.
   Juros de parcelamento ficam fora: o valor lançado é o total pago. Excluir a compra tira todas as
   parcelas.
6. **Pagamento de fatura** (opção 4b): tabela `card_payments` no `expense_transactions` (dono, cartão,
   mês de vencimento da fatura, conta de origem, valor, data do pagamento), com os eventos
   `CardInvoicePaid` e `CardInvoicePaymentDeleted` no outbox, gravados no mesmo `SaveChanges`. O
   pagamento **nunca** é lançamento: não aparece no Resumo, nos orçamentos, no "Sem orçamento" nem nas
   sugestões de categoria. Cartão e conta são validados no `accounts` (de outro usuário = 404); a
   conta de origem pode ser qualquer conta ativa do usuário. Pagamento parcial e acima do total são
   aceitos e ficam visíveis na fatura. O pagamento pode ser excluído; não há edição.
7. **Situação da fatura**, calculada e nunca guardada: **Aberta** até o fechamento; **Fechada** entre
   o fechamento e o vencimento; **Paga** quando os pagamentos cobrem o total; **Parcialmente paga**
   quando cobrem parte; **Vencida** depois do vencimento sem cobrir o total. Receita no cartão
   (estorno) abate do total da fatura em que cai. Saldo devedor de uma fatura vencida **não** passa
   para a fatura seguinte (rotativo fica fora).
8. **Previsão de caixa** (opção 5a): fluxo mês a mês, do mês atual até **6 meses** (parâmetro de 1 a
   12), por escopo PF, PJ e total. Por mês: **entradas** (receitas fora do cartão pela data),
   **saídas** (gastos fora do cartão pela data), **faturas** (total das faturas que vencem no mês,
   com quanto já foi pago e quanto falta) e **resultado** (entradas − saídas − faturas). O cartão
   entra **só pela fatura, no mês de vencimento**, nunca pela data da compra. O pagamento de fatura não
   entra de novo (já está nas faturas). O escopo de uma fatura é o da conta do cartão. A previsão só
   conhece o que está lançado (sem recorrência), e a tela diz isso. Saldo projetado fica para a
   INV-13 (Could), com saldo inicial informado pelo usuário.
9. **Limite disponível:** limite do cartão menos o que ainda não foi pago de todas as suas faturas,
   **incluindo parcelas futuras**. Só leitura: lançamento acima do limite não é bloqueado (o produto
   registra, não autoriza).
10. **Pagamento de fatura no extrato importado:** na prévia de um extrato de conta, a linha cujo memo
    indica pagamento de fatura ("PAG FATURA", "PAGAMENTO FATURA", "PGTO CARTAO") vem sugerida como
    **pagamento de fatura** de um cartão da conta (o usuário escolhe quando houver mais de um), e a
    confirmação cria o pagamento em vez de um gasto. Continua sendo sugestão: o usuário pode trocar
    para lançamento comum (mesma regra das pistas da ADR-0004, decisão 5).
11. **Rotas e escopos:** faturas em `GET /reports/cards/{id}/invoices` e previsão em
    `GET /reports/cash-forecast` no `reporting` (escopo `expenses.read`); pagamentos em
    `/transactions/card-payments` no `transactions` (`expenses.write` para registrar e excluir,
    `expenses.read` para ler). As rotas do gateway que já existem cobrem as duas; nenhum escopo novo,
    nenhuma mudança no `identity`.

## Consequências

- **Positivas:**
  - O pagamento de fatura deixa de duplicar gasto, à mão e (com a INV-11) na importação.
  - Resumo e orçamentos não mudam de comportamento: competência continua sendo a data da compra,
    inclusive na compra parcelada.
  - Fatura, limite e previsão ficam no mesmo lado de leitura que o Resumo e os orçamentos, sobre a
    mesma projeção.
  - Nenhum serviço, escopo, rota de gateway ou mudança no `identity` novo.
- **Negativas:**
  - Contrato do evento muda (v2) e precisa de backfill dos lançamentos de cartão anteriores; durante o
    backfill a projeção fica atrasada por um momento (métrica `reporting.projection.lag`).
  - O `reporting` volta a depender do `accounts` por HTTP na consulta de fatura, limite e previsão:
    se o `accounts` cair, essas telas falham (Resumo e orçamentos não).
  - Duas tabelas e dois tipos de evento novos (`card_payments`; `CardInvoicePaid`,
    `CardInvoicePaymentDeleted`) além do `TransactionCardDetailsResolved`.
  - A previsão sem recorrência subestima o futuro (salário e assinaturas só entram se lançados com data
    futura).
- **Neutras / a observar:**
  - Fechamento e vencimento ignoram dias úteis; a fatura calculada pode vencer um ou dois dias antes da
    do banco.
  - O total calculado pode divergir do total da fatura do banco (IOF, juros, compras não lançadas);
    conciliar os dois fica fora do épico.
  - Um pagamento informa o mês da fatura que paga; se o usuário pagar a fatura errada, exclui e
    registra de novo.
