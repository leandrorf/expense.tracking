# ADR-0011: Conciliação da fatura do cartão calculada no reporting, com o arquivo do banco lido do lote de importação (épico RCN)

- **Status:** Aceito
- **Data:** 2026-11-26
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico RCN (história RCN-1), `docs/backlog.md`, ADR-0004 (importação de extrato e
  fatura), ADR-0005 (fatura de cartão e previsão), ADR-0007 (edição de lançamentos)

## Contexto

A fatura do cartão é calculada pelo app a partir do que foi lançado (ADR-0005): compras, parcelas,
estornos e pagamentos, no ciclo do cartão. A fatura do banco raramente bate de primeira: falta uma compra,
outra foi lançada duas vezes ou caiu no ciclo errado, aparecem IOF, juros ou anuidade. A ADR-0005 deixou
"conciliar os dois" fora do épico INV, e a ADR-0004 tirou a conciliação da importação. O épico RCN dá ao
usuário a comparação, o caminho para resolver cada diferença e a marca de fatura conferida.

### Achados no código

- **A fatura calculada vive no `reporting`** (`InvoiceCalculator`, `CardCycle` do `accounts`). Cada item
  traz o lançamento, a data da compra, o valor, a parcela (`n` de `m`) e a descrição. O status da fatura é
  calculado a cada consulta; não há estado guardado.
- **O arquivo da fatura vive no `transactions`** (ADR-0004): OFX e CSV de cartão viram um lote com prévia
  (`import_batches`, `import_lines`), confirmado com decisões por linha. Créditos (pagamento, estorno) saem
  "fora por padrão", e o pagamento vira pagamento de fatura (INV-11).
- **A importação só casa pela chave do arquivo** (FITID ou impressão digital): compra lançada à mão nunca
  casa, e a linha duplicada não diz qual lançamento repete.
- **Parcelas não são reconhecidas:** "LOJA PARCELA 03/10" chega com chave nova a cada mês e vira compra
  nova, somando-se à parcela que o app já projeta.
- **O total do banco é ignorado:** o leitor de OFX não lê `LEDGERBAL` nem `DTSTART`/`DTEND`.
- **Tokens:** os três serviços aceitam o mesmo token do usuário (audiência `expense-tracking-api`, escopos
  `expenses.read`/`expenses.write`), e o `reporting` já o repassa ao `accounts`. Nenhum serviço chama o
  `reporting`.

## Opções consideradas

### 1. Onde a conciliação é calculada

- **(a) No `reporting`**, dono da fatura calculada, lendo as linhas do lote no `transactions` por HTTP com
  o token do usuário.
- **(b) No `transactions`**, dono do arquivo, recalculando a fatura (duplicaria ciclo, parcelas e estornos).
- **(c) Na SPA**, juntando as duas respostas (regra de negócio no cliente, sem teste de servidor).

### 2. O lado do banco

- **(a) Só o total** digitado pelo usuário.
- **(b) Só o arquivo** da fatura.
- **(c) Os dois:** o total sempre; o arquivo, quando houver, para o detalhe.

### 3. Casamento de linhas

- **(a) Por regras em ordem de certeza** (chave, parcela, valor e data).
- **(b) Por pontuação aproximada** (valor parecido, descrição parecida): acha mais pares, erra mais e é
  difícil de explicar na tela.

## Decisão

**A conciliação é calculada no `reporting` (opção 1a), com o total do banco digitado ou lido do arquivo e
o detalhe linha a linha a partir do lote de importação do `transactions` (opção 2c), casando por regras
em ordem de certeza (opção 3a). O que falta é importado pelo próprio lote; a fatura pode ser marcada como
conferida, com a diferença aceita.** As regras abaixo valem para as histórias RCN-2 a RCN-9.

1. **Onde.** O `reporting` calcula; o `transactions` continua dono da importação e da confirmação. Para o
   detalhe, o `reporting` lê o lote em `GET /transactions/imports/{id}` repassando o token do usuário
   (`Services:TransactionsBaseUrl`, o mesmo host do feed). O lote precisa ser do usuário e de fatura do
   mesmo cartão (404 `reconciliation.batch_not_found`; 400 `reconciliation.batch_card_mismatch`). Nenhum
   serviço passa a chamar o `reporting`.
2. **Lado do banco.** O total do banco é o informado pelo usuário; com arquivo OFX, o lote guarda o
   `LEDGERBAL` (total declarado) e o `DTSTART`/`DTEND` (período), e a tela vem com esse total preenchido.
   Sem total declarado (CSV), vale a soma das linhas, sem os pagamentos.
3. **Casamento**, nesta ordem, cada linha e cada item casando uma vez só:
   1. **mesma chave**: a linha repete um lançamento já importado (o `transactions` passa a informar qual) e
      esse lançamento está na fatura;
   2. **parcela**: descrição com "n/m", "PARC n/m" ou "PARCELA n DE m" e valor igual ao da parcela `n` de
      um item com `m` parcelas na fatura (a data da linha não entra; desempate pela descrição parecida e,
      depois, pela data da compra);
   3. **valor e data**: mesmo valor ao centavo e data da compra até **3 dias** de diferença, a mais próxima
      primeiro, com a descrição parecida como desempate.

   Sem casamento por valor aproximado: "valor diferente" só sai das regras 1 e 2, em que o par é certo.
   "Descrição parecida" = mais palavras em comum depois de normalizar (minúsculas, sem acento, sem
   números).
4. **Resultado.** Quatro grupos: **conferido** (casou, valor igual), **valor diferente** (casou pela
   regra 1 ou 2, valor difere), **só no banco** e **só no app**. Totais: banco, app, diferença e quanto
   dela cada grupo explica. Pagamentos e créditos que a importação deixa fora por padrão não entram na
   comparação; estornos lançados no app entram (abatem a fatura dos dois lados).
5. **Resolver.** **Só no banco** → importar pelo próprio lote: a tela inclui só essas linhas e deixa as
   casadas de fora (nunca viram compra nova, parcelas inclusive). Uma linha "n/m" sem compra no app entra
   como compra de 1 parcela do valor da linha. **Só no app** e **valor diferente** → atalho para editar
   (ADR-0007) ou excluir o lançamento. **Encargos** (IOF, juros, multa, mora, anuidade, tarifa) são
   reconhecidos pela descrição e vêm com a categoria "Encargos do cartão" sugerida; "ANUIDADE 3/12" é
   encargo, não parcela de compra.
6. **Qual fatura é o arquivo.** O `reporting` sugere o mês de vencimento pelo `DTEND` do OFX (o
   fechamento) e, sem ele, pelo período que contém mais datas de compra das linhas que não são parcela. O
   usuário troca.
7. **Conferida.** Tabela `invoice_reconciliations` no `reporting` (dono, cartão, mês de vencimento, total
   do banco, total do app no momento, data). Conferir com diferença é permitido e fica registrado. A
   fatura traz a conferência; se o total do app mudar depois, ela aparece como **alterada depois da
   conferência** (RCN-8). Conferir de novo substitui a marca; desfazer a apaga.
8. **Rotas.**
   - `reporting`: `GET /reports/cards/{id}/invoices/{dueMonth}/reconciliation` (`?batchId=` para o
     detalhe; sem ele, só os totais e a conferência guardada), `PUT` na mesma rota com `{ bankTotal }`
     para marcar como conferida e `DELETE` para desfazer. `GET /reports/cards/{id}/invoices` passa a trazer
     a conferência de cada fatura. `dueMonth` em `yyyy-MM`.
   - `transactions`: o lote de cartão traz `statedTotal`, `statementStart` e `statementEnd` (OFX), e a
     linha duplicada traz `duplicateOf` (o lançamento que ela repete).
   - Nada novo no gateway (`/api/reports/**` e `/api/transactions/**` já passam) nem no `identity`.

## Consequências

- **Positivas:**
  - O usuário vê a diferença da fatura e a causa, linha a linha, e resolve sem sair da tela.
  - Importar a fatura do banco deixa de duplicar parcelas.
  - O cálculo da fatura continua num lugar só (ADR-0005).
- **Negativas:**
  - O `reporting` passa a chamar o `transactions` por HTTP (até aqui só lia o feed): se o `transactions`
    cair, a conciliação com arquivo falha (a por total, não).
  - Uma tabela nova no `reporting` e campos novos no lote de importação.
  - As regras de parcela e de encargo dependem de como cada banco escreve a descrição; bancos com outro
    formato caem em "valor e data" ou em "só no banco".
- **Neutras / a observar:**
  - Logo depois de importar o que falta, a projeção leva um instante: a conciliação recalculada pode ainda
    não ver os lançamentos novos.
  - A janela de 3 dias pode casar duas compras iguais de dias próximos com a linha errada; o total não muda,
    só o par.
