# ADR-0007: Edição de lançamentos com evento próprio e concorrência otimista, e receita na SPA (épico LNC)

- **Status:** Aceito
- **Data:** 2026-11-22
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico LNC (história LNC-1), `docs/backlog.md`, ADR-0001 (outbox e feed de eventos),
  ADR-0004 (importação de extrato e fatura), ADR-0005 (fatura de cartão), ADR-0006 (exportação contábil)

## Contexto

O lançamento é o dado mais usado do produto, mas só nasce e morre. A SPA registra apenas gastos, e
corrigir qualquer campo obriga a excluir e lançar de novo, o que perde a origem de um lançamento
importado. O épico LNC traz a receita e as observações para o formulário e a edição de lançamentos,
com Resumo, orçamentos, fatura, previsão e exportação contábil acompanhando a correção.

### Achados no código

- **A API já aceita receita** (`type: "Income"`); a SPA fixa `type: 'Expense'`. Receita no cartão é
  estorno e abate a fatura (ADR-0005, decisão 7), mas o domínio aceita receita parcelada.
- **Observações** (`notes`, até 500) existem na API e na exportação, não na SPA. A lista não mostra
  se o lançamento é receita ou gasto.
- **Não há edição.** `Transaction` tem `UpdatedAtUtc`, mas nenhum método muda o lançamento, não existe
  `PUT`/`PATCH` e não há token de concorrência (só o lote de importação tem).
- **O evento é de criação.** `TransactionRegistered` (v2) leva o estado completo e o escopo da conta;
  a projeção do `reporting` o aplica como *upsert* por id. Exclusão é `TransactionDeleted`. Fatura,
  previsão, Resumo e orçamentos leem a projeção.
- **O escopo PF/PJ é da conta**, copiado no evento. A chave externa da importação é única por dono e
  conta; o lote aponta para os lançamentos que criou, e desfazer a importação os apaga.
- **A sugestão de categoria** da importação (IMP-6) usa o lançamento criado por último com a mesma
  descrição (`CreatedAtUtc`).
- **A exportação contábil** lê o `transactions` direto e avisa quando um mês exportado muda (CTB-6).

## Opções consideradas

### 1. Contrato do evento de edição

- **(a) Evento novo `TransactionUpdated`** com o estado completo depois da edição, aplicado na
  projeção só em linha que já existe.
- **(b) Reemitir `TransactionRegistered`**, que a projeção já aplica como *upsert*. "Registrado" e
  "alterado" viram o mesmo fato no feed, e uma edição lida depois da exclusão recriaria a linha.

### 2. Concorrência

- **(a) A última gravação vence.** Duas abas editando o mesmo lançamento se sobrescrevem em silêncio.
- **(b) Concorrência otimista** com uma versão inteira no lançamento e 409 quando ela mudou.

### 3. Forma da rota

- **(a) `PUT /transactions/{id}`** com o lançamento inteiro, reusando a validação da criação.
- **(b) `PATCH`** só com o que mudou. As regras cruzadas (meio × cartão × parcelas × tipo) ficam
  ambíguas quando só parte dos campos chega.

### 4. Troca de conta

- **(a) Permitida.** Pode trocar o escopo PF/PJ (o evento e a projeção precisariam reescrevê-lo) e
  conflita com a chave externa da importação, que é por conta.
- **(b) Proibida:** quem errou a conta exclui e lança de novo.

## Decisão

**A edição é um `PUT` do lançamento inteiro no `transactions`, com concorrência otimista, publicada
como um evento novo `TransactionUpdated` que a projeção aplica só em linha existente; a SPA ganha
receita, observações e edição.** As regras abaixo valem para as histórias LNC-2 a LNC-7.

1. **Evento** (opção 1a): `TransactionUpdated` (v1) no outbox, no mesmo commit da edição, com
   `transactionId`, `ownerUserId`, `accountId`, `transactionType`, `paymentMethod`, `category`,
   `amount`, `currency`, `occurredOn`, `description`, `creditCardId`, `installments` e `version`. Não
   leva o escopo: a conta não muda (regra 2), então a projeção mantém o escopo que já tem. O
   `reporting` aplica o evento **só se a linha existir** (lançamento excluído não volta) e ignora tipos
   que não conhece, como hoje.
2. **O que muda** (opção 4b): tipo, meio, cartão (da mesma conta), parcelas, descrição, categoria,
   valor, moeda, data e observações, com as mesmas regras da criação (ADR-0005, decisão 5, para
   parcelas). A **conta não muda**: `accountId` diferente do atual = 400
   `transaction.account_immutable`. Cartão é validado no `accounts` como na criação.
3. **Lançamento importado:** editável como os outros. A chave externa e o lote são preservados:
   reimportar o mesmo extrato continua acusando duplicata pela chave, e desfazer a importação apaga o
   lançamento mesmo editado.
4. **Concorrência** (opção 2b): o lançamento ganha `version` (inteiro, começa em 1; os existentes
   recebem 1 na migração), devolvido em toda leitura. A edição manda a `version` que leu; se ela
   mudou, 409 `transaction.concurrency_conflict` e nada muda. A versão é o token de concorrência do EF
   (como o lote de importação), então duas edições simultâneas com a mesma versão também resultam
   numa só gravação e um 409.
5. **Rota** (opção 3a): `PUT /transactions/{id}` com o mesmo corpo da criação (sem efeito na conta,
   que só é conferida) mais `version`; responde 200 com o lançamento atualizado. `expenses.write`;
   lançamento de outro usuário, 404. Pela rota existente do gateway; nenhum escopo novo.
6. **Edição sem mudança:** quando nenhum campo muda, responde 200 com o lançamento como está, sem
   subir a versão e sem evento (não gera trabalho na projeção nem aviso falso de mês alterado).
7. **Receita na SPA:** o formulário ganha o tipo (Gasto/Receita, padrão Gasto) e as observações. A
   lista mostra o tipo pelo sinal e pela cor do valor (+ verde para receita, − vermelho para gasto) e
   as observações abaixo da descrição. **Receita não é parcelada:** `installments > 1` em receita =
   400 `transaction.installments_not_allowed` (o código de hoje para meio que não é cartão). Receita
   no cartão continua sendo estorno.
8. **Sugestão de categoria:** a sugestão da importação (IMP-6) passa a usar o lançamento **alterado**
   por último (`UpdatedAtUtc`, que a criação também preenche) com a mesma descrição: recategorizar
   ensina a próxima importação.
9. **Histórico e lote:** sem trilha de auditoria na tela nesta versão; o feed de eventos guarda a
   sequência de estados. Recategorizar vários lançamentos de uma vez é **Could** (LNC-7), com um
   `TransactionUpdated` por lançamento e a mesma regra de versão por item.

## Consequências

- **Positivas:**
  - Receita entra pela tela, e corrigir um lançamento mantém o id e a origem.
  - Resumo, orçamentos, fatura e previsão acompanham a edição pela projeção, sem lógica nova de
    leitura; a exportação contábil já avisa o mês alterado (CTB-6).
  - Duas abas não se sobrescrevem em silêncio.
  - Nenhum serviço, escopo ou rota de gateway novo.
- **Negativas:**
  - Um tipo de evento a mais no feed (`TransactionUpdated`) e uma coluna nova (`version`) com
    migração em `transactions`.
  - O cliente precisa mandar a `version`; integrações que só criam lançamentos não mudam.
  - Trocar a conta continua exigindo excluir e lançar de novo.
- **Neutras / a observar:**
  - Editar a data para outro mês muda os dois meses no Resumo e na exportação; o aviso da CTB-6
    aparece nos dois, se exportados.
  - Editar um lançamento de cartão muda faturas já fechadas ou pagas: a fatura recalcula, e a situação
    pode voltar a "Parcialmente paga". Isso é o comportamento esperado de uma correção.
