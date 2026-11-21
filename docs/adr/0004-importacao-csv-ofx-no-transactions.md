# ADR-0004: Importação de extrato e fatura (CSV/OFX) no transactions, com lote persistido (épico IMP)

- **Status:** Aceito
- **Data:** 2026-11-21
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico IMP (história IMP-1), `docs/backlog.md`, ADR-0001 (outbox e feed de eventos),
  ADR-0002 (orçamentos no reporting)

## Contexto

Todo lançamento é digitado à mão, um por um. O épico IMP deixa o usuário importar o extrato da conta
ou a fatura do cartão que o banco já entrega (OFX ou CSV), conferir uma prévia e confirmar. Os
lançamentos importados precisam contar no Resumo e nos orçamentos como qualquer outro, com o escopo
PF/PJ da conta, e reimportar o mesmo arquivo não pode duplicar nada.

### Achados no código

- **Não há edição de lançamento**, só criar e excluir. Uma categoria errada na importação só se
  corrige excluindo e lançando de novo, e a categoria pesa no consumido dos orçamentos (ADR-0002).
- **Categoria e descrição são obrigatórias** (`Transaction.Create`: descrição até 200, categoria até
  60 caracteres). Extrato e fatura não trazem categoria; o memo do banco vira a descrição.
- **Valor sempre positivo** (`Money` recusa ≤ 0); o sentido vem do `TransactionType`
  (`Expense`/`Income`).
- **Meios de pagamento:** `Cash`, `Pix`, `Boleto`, `CreditCard`, `DebitCard`, `BankTransfer`.
  `CreditCard` exige `creditCardId` e os outros meios não aceitam cartão. O OFX não diz se um débito
  foi Pix, boleto ou TED; quando muito, o memo dá pistas.
- **Sem identificador externo nem deduplicação.** O lançamento não guarda a origem.
- **Cada criação valida conta e cartão no `accounts` por HTTP** (`IAccountsClient`). Reaproveitar o
  caso de uso atual linha a linha faria centenas de chamadas numa importação.
- **Cada lançamento gera um `TransactionRegistered` no outbox** (ADR-0001), então os importados
  chegam à projeção do `reporting` sem mudança no feed.
- **Nenhum serviço recebe upload** (nenhum endpoint `multipart`). Limite de tamanho, validação de
  formato e cuidado com dado sensível (extrato bancário) entram pela primeira vez.
- **Gateway:** `/api/transactions/**` vai para o `transactions` (ordem 1) e `/api/**` é o catch-all do
  `accounts` (ordem 2).
- **Sem bibliotecas pagas** (decisão do projeto). As bibliotecas OFX para .NET estão abandonadas;
  o `CsvHelper` tem licença dupla MS-PL/Apache 2.0.

## Opções consideradas

### 1. Onde mora a importação

- **(a) Módulo no `transactions`.** Quem cria lançamentos e escreve no outbox já é ele; a
  confirmação grava lançamentos e eventos na mesma transação de banco.
- **(b) Serviço novo `imports`.** Teria de chamar a API do `transactions` linha a linha (sem
  atomicidade, centenas de chamadas) ou dividir o banco com ele (quebra o "um banco por serviço").
  Mais um container, CI e rota sem ganho de autonomia, porque o resultado da importação é dado do
  `transactions`.

### 2. Onde fica a prévia

- **(a) Lote persistido no servidor** (`import_batches`/`import_lines`, status *Pendente →
  Confirmado/Descartado*).
- **(b) Sem estado:** o servidor só lê o arquivo e a SPA devolve as linhas na confirmação. Mais
  simples, mas a confirmação confia no que o navegador manda (a deduplicação e a validação teriam de
  ser refeitas sobre dado do cliente), não há como tornar a confirmação idempotente e não existe um
  id para desfazer a importação depois.

### 3. Leitura dos arquivos

- **OFX:** biblioteca de terceiros (abandonadas, sem suporte a .NET recente, risco de XXE herdado) ou
  **leitor próprio**, pequeno, para o subconjunto que bancos brasileiros emitem.
- **CSV:** leitor próprio (aspas, separador, quebra de linha dentro de campo são fonte conhecida de
  bugs) ou **`CsvHelper`** (licença livre, maduro).

## Decisão

**A importação é um módulo do `transactions`, com lote de importação persistido, leitor de OFX
próprio e `CsvHelper` para CSV.** As regras abaixo valem para as histórias IMP-2 a IMP-11.

1. **Lugar:** módulo de importação dentro do `transactions` (opção 1a). A leitura dos arquivos fica
   em classes puras, sem I/O nem EF, para poder ser extraída se um dia virar serviço. Reavaliar como
   serviço próprio só se a importação ganhar fontes que não criam lançamento (ex.: Open Finance com
   conciliação).
2. **Prévia no servidor** (opção 2a): tabelas `import_batches` e `import_lines` no
   `expense_transactions`, com dono, conta, cartão (se fatura), formato, tipo do arquivo
   (extrato/fatura) e status *Pendente → Confirmado* ou *Descartado*. A confirmação é idempotente: o
   mesmo lote confirmado de novo devolve o mesmo resultado sem criar nada. Lotes pendentes expiram em
   **7 dias** e são apagados por limpeza periódica. O id do lote é guardado em cada lançamento criado
   por ele, para desfazer a importação (IMP-9).
3. **Deduplicação por chave externa por conta.** No OFX a chave é o `FITID` do banco. No CSV é uma
   impressão digital (hash) de data, valor com sinal e descrição normalizada, mais a ordem da linha
   entre as idênticas do mesmo dia no arquivo (dois cafés iguais no mesmo dia são dois gastos). O
   lançamento ganha a coluna `external_id` (nula para os digitados) com índice único
   (dono, conta, `external_id`), que também resolve a corrida entre duas confirmações. Na prévia, a
   linha já importada aparece como **duplicada** e fica de fora por padrão. Um lançamento excluído
   libera a chave e pode ser importado de novo.
4. **Categoria obrigatória antes de confirmar.** Não existe "A classificar" automático, porque não dá
   para editar depois. A prévia vem preenchida pelo histórico (mesma descrição normalizada → categoria
   do lançamento mais recente, IMP-6), com as sugestões da BDG-7 e "aplicar às selecionadas". Linha
   incluída sem categoria bloqueia a confirmação inteira, e a resposta aponta quais linhas faltam.
5. **Meio de pagamento.** Fatura de cartão (OFX `CREDITCARDMSGSRSV1` ou CSV marcado como fatura no
   envio) → `CreditCard`, com o cartão escolhido para o lote. Extrato de conta → o usuário escolhe um
   meio padrão para o lote (ex.: Pix) e pode trocar por linha; `CreditCard` não é aceito em extrato.
   Pistas no memo ("PIX", "BOLETO", "TED", "SAQUE") viram **sugestão** na prévia, nunca decisão
   automática na confirmação.
6. **Sentido do valor.** Débito vira gasto, crédito vira receita, sempre com valor positivo. Crédito
   no extrato de conta entra como **receita, incluída** por padrão. Crédito na fatura do cartão
   (pagamento da fatura, estorno) fica **fora por padrão**: o pagamento da fatura não é gasto nem
   receita, e contá-lo duplicaria os gastos do cartão. O usuário pode incluir um estorno como receita
   na prévia.
7. **Leitura dos arquivos.**
   - **OFX:** leitor próprio para OFX 1.x (SGML, tags sem fechamento, cabeçalho `OFXHEADER`) e 2.x
     (XML). No XML, `DtdProcessing = Prohibit` e `XmlResolver = null`: arquivo com `<!DOCTYPE>` ou
     entidade é **recusado** com erro de formato, sem resolver nada (sem XXE). Codificação pelo
     cabeçalho (`CHARSET:1252` ou `encoding` do XML), padrão Windows-1252 no 1.x.
   - **CSV:** `CsvHelper`, com mapeamento de colunas (data, descrição, e valor único ou débito e
     crédito separados). Separador `;` ou `,`; decimal com vírgula ou ponto; datas `dd/MM/yyyy` e
     `yyyy-MM-dd`; UTF-8 (com ou sem BOM) ou Windows-1252. Sem mapeamento informado, o leitor detecta
     separador e cabeçalho e **propõe** um mapeamento, que o usuário confirma na SPA (IMP-8).
   - Linha com data ou valor inválido sai marcada como **inválida** (com o motivo), sem derrubar o
     arquivo. Arquivo que não é do formato, truncado ou malformado é recusado inteiro.
8. **Limites e dado sensível.**
   - Arquivo até **5 MB** e **5.000 linhas**; só `.ofx`, `.qfx` e `.csv`, conferidos pelo conteúdo e
     não só pela extensão. Limite de corpo aplicado no Kestrel e no gateway, antes de ler o arquivo
     para a memória.
   - O arquivo original **não é guardado**: só as linhas lidas, no lote, que seguem a regra de dono
     (lote de outro usuário = 404, como lançamento, conta e cartão).
   - Conteúdo do arquivo e das linhas **nunca** vai para log, trace, métrica ou mensagem de erro.
     Erros usam códigos estáveis (`import.file_too_large`, `import.too_many_lines`,
     `import.format_invalid`) e, quando muito, o número da linha. Métricas só com contagens (linhas
     lidas, importadas, duplicadas, inválidas).
   - Escopos OAuth: reaproveita `expenses.write` para enviar, confirmar e desfazer e `expenses.read`
     para ler o lote (importar é lançar). Nenhum escopo novo, nenhuma mudança no `identity`.
   - Conta e cartão são validados no `accounts` **uma vez por lote**, no envio e de novo na
     confirmação (a conta pode ter sido arquivada entre os dois), e não linha a linha.
9. **Rota:** `/imports` no `transactions`, exposto pelo gateway como `/api/imports/**` com ordem 1,
   antes do catch-all `/api/**` do `accounts`.

## Consequências

- **Positivas:**
  - Lançamentos e eventos do outbox de uma importação são gravados numa transação só; o `reporting`,
    o Resumo e os orçamentos recebem os importados sem mudança no feed.
  - Reimportar o mesmo arquivo não duplica nada, garantido pelo banco (índice único), não só pela
    prévia.
  - Confirmação idempotente e importação desfazível pelo id do lote.
  - Nenhum serviço, escopo ou mudança no `identity` novo.
- **Negativas:**
  - O `transactions` passa a receber upload: superfície nova de ataque (tamanho, XXE, CSV malformado)
    e dado sensível transitando. Exige a revisão de segurança antes do merge da IMP-4.
  - Duas tabelas, uma coluna nova, limpeza periódica de lotes expirados e um leitor de OFX próprio
    para manter.
  - A impressão digital do CSV depende da descrição normalizada: se o banco mudar o texto de um
    lançamento entre dois arquivos, a linha não é reconhecida como duplicada.
  - Uma importação grande gera muitos eventos de uma vez e aumenta por um momento o atraso da
    projeção (métrica `reporting.projection.lag`).
- **Neutras / a observar:**
  - O CSV de fatura é marcado como fatura pelo usuário no envio; não há como inferir isso do arquivo
    com segurança.
  - A deduplicação é por conta: o mesmo gasto importado em duas contas diferentes entra duas vezes.
  - Conversão de moeda fica fora: o lote usa a moeda do arquivo (padrão BRL).
