# ADR-0006: Exportação contábil PJ gerada no transactions, como pacote ZIP de CSVs (épico CTB)

- **Status:** Aceito
- **Data:** 2026-11-22
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico CTB (história CTB-1), `docs/backlog.md`, ADR-0001 (outbox e feed de eventos),
  ADR-0004 (importação de extrato e fatura), ADR-0005 (fatura de cartão e pagamento de fatura)

## Contexto

Todo mês, o usuário PJ manda ao contador o movimento da empresa: o que entrou, o que saiu, em que
categoria e por qual conta. Hoje o produto só mostra isso na tela (Lançamentos e Resumo), sem um
arquivo para enviar. O épico CTB entrega o movimento PJ de um mês num arquivo que o contador abre
direto no Excel ou importa no sistema dele. O que é pessoal (PF) nunca vai para o contador.

### Achados no código

- **O `transactions` tem o lançamento completo:** descrição, categoria, tipo, valor, moeda, data,
  meio, cartão, parcelas, observações e a origem (`ExternalId` e lote, quando importado). A listagem
  por conta e período já existe (`ITransactionRepository.ListAsync`).
- **O lançamento não guarda o escopo.** PF/PJ é da **conta** (`Account.Scope` no `accounts`). O
  `transactions` o busca no `accounts` (`IAccountsClient`) com o token do usuário.
- **A projeção do `reporting` não serve de fonte completa.** Os lançamentos fora do cartão
  registrados antes do evento v2 (INV-2) não têm descrição na projeção (o backfill só completou as
  compras no cartão). Ela também não tem observações nem a origem, e atrasa alguns segundos em
  relação ao que acabou de ser lançado ou importado.
- **A conta PJ tem CNPJ** (`Account.TaxId`, obrigatório quando o escopo é `Business`), nome e moeda.
  `GET /accounts` e `GET /credit-cards` devolvem também as contas e os cartões inativos.
- **O Resumo assume uma moeda só** (`SummaryCalculator` usa a primeira que encontra). Uma conta PJ
  pode ter outra moeda que não o real.
- **Categoria é texto livre**, sem plano de contas.
- **Pagamento de fatura não é lançamento** (ADR-0005, decisão 6): fica em `card_payments`, também no
  `transactions`.
- **Exclusão é física e não há edição.** Um mês já enviado pode mudar depois, e nada avisa.
- **Não há geração de arquivo no backend** nem armazenamento de arquivos. Bibliotecas de planilha e
  PDF têm licença a checar; o projeto evita libs que viraram pagas para uso comercial.
- **Texto vindo de extrato importado vai parar no arquivo.** Uma descrição que começa com `=`, `+`,
  `-` ou `@` vira fórmula quando o CSV é aberto no Excel (CSV injection, CWE-1236).

## Opções consideradas

### 1. Onde o arquivo é gerado

- **(a) No `transactions`**, fonte da verdade, com o lançamento completo e sem atraso. Precisa buscar
  no `accounts` as contas e os cartões do usuário (nome, escopo, CNPJ, moeda).
- **(b) No `reporting`**, a partir da projeção, como o Resumo. Sai sem descrição dos lançamentos
  antigos, sem observações e sem origem, e com o atraso da projeção.

### 2. Formato

- **(a) CSV no padrão do Excel pt-BR**, sem dependência nova.
- **(b) XLSX**, com biblioteca (ClosedXML é MIT).
- **(c) PDF**, com biblioteca (QuestPDF tem licença comunitária com limite de receita). Serve para
  ler, não para lançar.

### 3. Conteúdo

- **(a) Só os lançamentos**, num CSV.
- **(b) Um pacote ZIP do mês** (`System.IO.Compression`, nativo) com lançamentos, pagamentos de
  fatura e resumo por categoria.

### 4. Plano de contas

- **(a) Fora do escopo.**
- **(b) O usuário associa cada categoria PJ a um código e nome de conta** do plano do contador.
- **(c) Importar o plano inteiro do contador.** Cada sistema contábil tem um layout.

### 5. Mês já enviado que muda depois

- **(a) Nada avisa.**
- **(b) Registro de cada exportação** (totais e impressão digital, nunca o arquivo) e aviso quando o
  mês mudou.
- **(c) Fechamento contábil** que bloqueia excluir lançamentos de mês exportado.

## Decisão

**A exportação contábil é gerada no `transactions`, sob demanda, como um ZIP com três CSVs no padrão
do Excel pt-BR, só com contas PJ.** As regras abaixo valem para as histórias CTB-2 a CTB-7.

1. **Lugar** (opção 1a): no `transactions`, a partir das tabelas de lançamentos e de pagamentos de
   fatura. As contas e os cartões do usuário vêm do `accounts` (`GET /accounts` e
   `GET /credit-cards`, inativos incluídos), com o token do usuário encaminhado. Nenhum serviço novo;
   nada é gravado na geração (até a CTB-6).
2. **Formato** (opção 2a): CSV com `;` como separador, vírgula decimal **sem separador de milhar**
   (`-1234,56`, que o Excel pt-BR e os sistemas contábeis leem como número), data `dd/MM/aaaa`,
   UTF-8 **com BOM**, linhas terminadas em CRLF e cabeçalho na primeira linha. Campo com `;`, aspas ou
   quebra de linha vai entre aspas, com as aspas dobradas (RFC 4180). Campo de texto que começa com
   `=`, `+`, `-`, `@`, tabulação ou retorno de carro ganha um `'` na frente, para o Excel não
   executar fórmula. XLSX fica para depois, se o contador pedir abas; PDF fica fora.
3. **Conteúdo** (opção 3b): um ZIP com três arquivos:
   - **`lancamentos.csv`**: data, descrição, categoria, conta contábil (vazia até a CTB-5), tipo
     (Receita/Despesa), valor com sinal (saída negativa), moeda, meio, cartão (apelido e final), parcelas, conta,
     CNPJ, observações e origem ("digitado"/"importado"), em ordem de data e de criação;
   - **`pagamentos-fatura.csv`**: data do pagamento, cartão, fatura paga (`mm/aaaa`), conta de origem,
     CNPJ da conta de origem, valor e moeda;
   - **`resumo.csv`**: por moeda e categoria (com a conta contábil, quando houver), entradas, saídas e
     saldo, e uma linha de total por moeda.
4. **Período e datas.** Um **mês** (`month=aaaa-mm`); a tela sugere o anterior. Lançamentos entram
   pela **data do lançamento** (competência), como o Resumo; a compra parcelada entra **uma vez, com
   o valor inteiro**, na data da compra, com o número de parcelas na coluna. Pagamentos de fatura
   entram pela **data do pagamento**. O intervalo livre de datas fica fora desta versão: o contador
   trabalha por mês, e um ano são doze downloads.
5. **Escopo.** Só contas **PJ** (`Business`) do usuário, ativas e inativas: todas por padrão, ou uma
   (`accountId`). Conta PF nunca entra, nem por filtro. Um pagamento de fatura entra quando o
   **cartão é de conta PJ ou a conta de origem é PJ** (as duas pontas interessam ao contador). Sem
   conta PJ, 400 `accounting_export.no_business_account`; `accountId` de conta PF, 400
   `accounting_export.account_not_business`; de outro usuário ou inexistente, 404
   `account.not_found`; mês inválido, 400 `accounting_export.month_invalid`.
6. **Moeda.** Cada lançamento leva a moeda; o resumo soma **por moeda**, nunca misturando moedas. Com
   tudo em real, os totais do resumo batem com o Resumo PJ da SPA no mesmo mês.
7. **Plano de contas** (opção 4b), como **Should** (CTB-5): o usuário associa uma categoria a um
   código e nome de conta do plano do contador, guardado no `transactions` por usuário. A coluna
   "conta contábil" sai preenchida no CSV de lançamentos e no resumo. Categoria sem associação sai com
   a coluna vazia, sem bloquear a exportação. Importar o plano inteiro (4c) é Won't.
8. **Mês alterado depois da exportação** (opção 5b), como **Could** (CTB-6): cada exportação grava
   mês, contas, quantidade de linhas, totais e um hash SHA-256 do conteúdo, **nunca o arquivo**. Ao
   abrir a tela, o mesmo mês exportado antes e diferente agora mostra o aviso, com a diferença de
   linhas e de totais. Nada é bloqueado (5c fica fora).
9. **Rota, escopo OAuth e privacidade.** `GET /transactions/accounting-export?month=aaaa-mm[&accountId=]`
   com `expenses.read`, devolvendo `application/zip` com `Content-Disposition: attachment` e nome
   `contabil-<CNPJ só dígitos>-<aaaa-mm>.zip` quando todas as contas exportadas têm o mesmo CNPJ, e
   `contabil-<aaaa-mm>.zip` quando há mais de um. Uma prévia (quantidade e totais, sem o arquivo) para
   a tela da CTB-4 pode ser uma rota irmã, decidida na história. Pela rota existente do gateway
   (`/api/transactions/**`); nenhum escopo novo, nenhuma mudança no `identity`. O conteúdo do arquivo
   nunca vai para log ou trace (mesma regra da ADR-0004, decisão 8); o log registra só mês, quantidade
   de contas e de linhas.

## Consequências

- **Positivas:**
  - O contador recebe o mês PJ completo (com descrição, observações e origem) e conferível contra o
    Resumo PJ, num arquivo só.
  - O que acabou de ser lançado ou importado já sai no arquivo, sem esperar a projeção.
  - Nenhuma biblioteca, serviço, escopo, rota de gateway ou mudança no `identity` nova.
  - O PF nunca sai, nem por filtro.
- **Negativas:**
  - O `transactions` ganha um lado de leitura e relatório (resumo por categoria) que repete uma
    regra do `reporting`; se a regra do Resumo mudar, as duas precisam mudar juntas, e o E2E (CTB-7)
    compara as duas.
  - A exportação depende do `accounts` por HTTP: se ele cair, ela falha.
  - CSV não tem abas nem formatação; quem quiser planilha "bonita" ainda formata no Excel.
- **Neutras / a observar:**
  - Um mês já exportado pode mudar sem aviso até a CTB-6.
  - O resumo agrupa pela categoria como foi digitada; categorias parecidas ("Software" e "software")
    saem separadas, como no Resumo.
  - Despesa pessoal paga pela conta PJ (pró-labore, distribuição de lucros) sai como despesa comum;
    classificá-la fica fora do épico.
