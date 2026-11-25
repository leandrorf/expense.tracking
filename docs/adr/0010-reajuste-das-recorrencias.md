# ADR-0010: Reajuste das recorrências com valor por vigência, sugerido pelo índice oficial e confirmado pelo usuário (épico RJT)

- **Status:** Aceito
- **Data:** 2026-11-25
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico RJT (história RJT-1), `docs/backlog.md`, ADR-0001 (feed de eventos), ADR-0002
  (limite com vigência nos orçamentos), ADR-0005 (contrato de evento versionado), ADR-0008 (lançamentos
  recorrentes), ADR-0009 (avisos)

## Contexto

Aluguel, condomínio, escola, plano de saúde e contratos de serviço reajustam uma vez por ano, quase sempre
por um índice (IGP-M, IPCA, INPC) ou por um percentual combinado. A recorrência (ADR-0008) só conhece um
valor: editar a regra muda todos os períodos em aberto, inclusive os anteriores ao reajuste, e a previsão
fica com o valor velho depois do aniversário do contrato. O épico RJT dá à recorrência valor por vigência,
um reajuste anual configurado e a sugestão do valor novo pelo índice oficial.

### Achados no código

- **A regra tem um valor só** (`Recurrence.Amount`), usado no lançamento pré-preenchido, no "a lançar", no
  casamento da importação (10% de tolerância), no aviso de saída recorrente e na previsão.
- **O padrão de vigência já existe nos orçamentos** (ADR-0002, decisão 1): limites com `EffectiveFrom` e
  `LimitFor(month)`.
- **O valor viaja no `RecurrenceSaved` v1** (estado completo), projetado pelo `reporting` (REC-4).
- **Nenhum serviço acessa a internet.** A API de séries temporais do Banco Central (SGS) publica, sem
  autenticação, a variação mensal do IPCA (série 433), do INPC (188) e do IGP-M (189); o IPCA de um mês sai
  por volta do dia 10 do mês seguinte, e o IGP-M no fim do próprio mês.
- **A ocorrência é identificada pelo período** (`yyyy-MM` ou `yyyy`, ADR-0008, decisão 4).

## Opções consideradas

### 1. Modelo do valor

- **(a) Valor com vigência**, como o limite dos orçamentos.
- **(b) Valor único editável**, como hoje: o reajuste muda também os meses anteriores ainda em aberto.

### 2. Quem aplica o reajuste

- **(a) O sistema, sozinho, no aniversário.**
- **(b) O sistema sugere; o usuário confirma** (ou informa outro percentual ou valor).

### 3. Fonte dos índices

- **(a) API do Banco Central (SGS)**, lida por um job: primeira saída do sistema para a internet.
- **(b) O usuário digita o percentual** no aniversário.
- **(c) Tabela mantida à mão no repositório:** desatualiza e exige release a cada mês.

## Decisão

**A recorrência passa a ter valor por vigência e um reajuste anual opcional. No aniversário, o
`transactions` sugere o valor novo pelo índice oficial (lido por um job desligável da API do Banco
Central) ou pelo percentual fixo, e o usuário confirma; a confirmação cria a vigência. A previsão usa o
valor vigente de cada período e estima o reajuste ainda não confirmado.** As regras abaixo valem para as
histórias RJT-2 a RJT-10.

1. **Vigências** (opção 1a). Tabela `recurrence_amounts` (regra, período de início, valor). A primeira
   vigência é a do período do início da regra e sempre existe; o valor de um período é o da última vigência
   com início até ele. Uma vigência nova começa num período **da agenda**, **depois** do primeiro e **ainda
   não fechado** (400 `recurrence.amount_period_invalid`); começar num período que já tem vigência troca o
   valor dela. Os lançamentos já feitos nunca mudam.
2. **Rotas das vigências.** `POST /transactions/recurrences/{id}/amounts` (`{ fromPeriod, amount }`) cria ou
   troca uma vigência; `DELETE .../amounts/{fromPeriod}` remove uma vigência que ainda não começou (a
   primeira nunca sai). O `PUT` da regra continua com o mesmo corpo: o `amount` dele passa a corrigir o
   valor da **vigência atual** (a que vale hoje), sem criar vigência; os outros campos seguem como na
   ADR-0008. Todas com a `version` da regra (409 como na edição).
3. **Reajuste anual configurado** (opcional): mês de aniversário (1 a 12; padrão o mês do início; na regra
   anual, é o próprio mês dela), tipo (`Index` com `IPCA`, `INPC` ou `IGPM`; ou `Fixed` com o percentual) e
   "não reduzir" (ligado por padrão). Vai no corpo da regra (`adjustment`, nulo = sem reajuste).
4. **Aplicar** (opção 2b): `POST /transactions/recurrences/{id}/adjustments` com `{ year, rate }` (percentual)
   ou `{ year, amount }` (valor novo). Cria a vigência a partir do período do aniversário daquele ano,
   sobre o valor vigente no período anterior; arredondamento a centavos com meio para cima; com "não
   reduzir", nunca abaixo do valor anterior. Aniversário já aplicado = 409
   `recurrence.adjustment_already_applied`; ano antes do início ou depois do fim da regra = 400.
5. **Sugestão.** A leitura da regra traz `pendingAdjustment` do mês do aniversário até 11 meses depois,
   enquanto aquele aniversário não for aplicado: período, valor atual, e `rate`/`suggestedAmount` quando dá
   para calcular, ou `waitingFor` (o mês de índice que falta). Índice: acumulado dos **12 meses que
   terminam no mês anterior ao aniversário**, produto das variações mensais
   (`Π(1 + r/100) − 1`); falta qualquer mês = aguardando. Percentual fixo: sempre calculável.
6. **Índices** (opção 3a, com 3b sempre disponível). Tabela `economic_indexes` (índice, mês, variação
   mensal em %) no `transactions`. `IndexFetchWorker` roda uma vez por dia, quando `Indexes:Fetch:Enabled`
   (padrão desligado; o compose liga): pede ao SGS (`api.bcb.gov.br`) os meses que faltam desde
   `Indexes:Fetch:Since` e grava os novos. Fonte fora do ar ou resposta inválida: registra no log e tenta no
   dia seguinte; nada mais é bloqueado. **Nenhum dado de usuário vai na chamada.** É a única saída do sistema
   para a internet; o host fica em configuração (`Indexes:Fetch:BaseUrl`).
7. **Contrato do evento.** `RecurrenceSaved` passa à **v2**: os campos da v1, com `amount` = valor vigente
   hoje, mais `amounts` (`[{ fromPeriod, amount }]`) e `adjustment` (aniversário, tipo, índice ou
   percentual, "não reduzir" e `estimatedRate`, a taxa que a previsão usa para o reajuste ainda não
   confirmado). O `reporting` lê v1 (uma vigência só, sem reajuste) e v2. Criar ou trocar vigência, aplicar
   reajuste e mudar o reajuste publicam um `RecurrenceSaved` v2. Quando o job grava um mês novo de um
   índice, o `transactions` republica as regras que reajustam por ele (a `estimatedRate` muda). Implantar o
   `reporting` antes do `transactions`.
8. **Previsão** (`reporting`). O valor de cada ocorrência é o da vigência do período. Para o aniversário
   seguinte ainda sem vigência, do período do aniversário em diante usa o valor vigente reajustado pela
   `estimatedRate` (o último acumulado de 12 meses conhecido do índice, ou o percentual fixo; respeita "não
   reduzir"), e marca a parte estimada (`estimatedRecurring` no fluxo do mês). Só um reajuste estimado à
   frente: o aniversário do ano seguinte não é estimado de novo.
9. **Onde o valor vigente vale.** No `transactions`: lançamento pré-preenchido, "a lançar" e casamento da
   importação. No `reporting`: previsão, saldo projetado e aviso de saída recorrente (NTF).
10. **Aviso** (Could). Tipo novo `RecurrenceAdjustment` no sino e no resumo por e-mail, com chave
    `adjustment:{regra}:{período}`, enquanto a sugestão estiver pendente; confirmar tira o aviso.

## Consequências

- **Positivas:**
  - Reajustar não muda os meses anteriores; a previsão do ano fica com o valor certo de cada mês.
  - O usuário não precisa achar o índice nem fazer a conta: confirma um valor já calculado.
  - Sem o job, tudo funciona com o percentual digitado.
- **Negativas:**
  - Primeira dependência de internet: rede de saída no ambiente, fonte que pode mudar de formato.
  - `RecurrenceSaved` v2, duas tabelas novas (`recurrence_amounts`, `economic_indexes`) e republicação de
    regras quando sai índice novo.
  - O `PUT` muda de sentido para o valor (vigência atual, não "todos os períodos em aberto").
- **Neutras / a observar:**
  - A taxa estimada da previsão anda a cada mês de índice novo; o valor sugerido no aniversário pode
    diferir do estimado meses antes.
  - Contratos com defasagem de 2 meses ou índices fora da lista ficam com o percentual digitado.
