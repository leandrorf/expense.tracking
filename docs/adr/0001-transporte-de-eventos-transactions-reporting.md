# ADR-0001: Transporte de eventos entre transactions e reporting (épico EVT)

- **Status:** Proposto
- **Data:** 2026-11-19
- **Decisores:** dono do produto/repositório (decisão pendente); análise preparada pelo time de desenvolvimento
- **Relacionados:** épico EVT (Jira SCRUM-14; histórias SCRUM-21, SCRUM-22, SCRUM-23), `docs/backlog.md`

## Contexto

Hoje o `reporting` é um agregador sem banco. A cada `GET /reports/summary`, ele chama o
`transactions` (lançamentos) e o `accounts` (mapa conta → escopo PF/PJ) por HTTP, com o token do
usuário, e calcula os totais em memória com o `SummaryCalculator`. Se um dos dois estiver fora,
o resumo falha.

O épico EVT troca isso por uma **projeção alimentada por eventos**:

- **EVT-1:** o `transactions` grava um evento no *outbox* na mesma transação de banco do lançamento.
- **EVT-2:** o `reporting` consome os eventos e mantém uma projeção, sem duplicar valores quando o
  mesmo evento chega mais de uma vez.
- **EVT-3:** `/reports/summary` passa a ler a projeção, mantendo o contrato atual.

Esta ADR decide **só o transporte**: como um evento gravado no outbox do `transactions` chega ao
`reporting`. O outbox e a projeção idempotente existem em qualquer opção.

### Forças e restrições

- **Escala e operação.** Projeto pessoal/PJ de um dono só, volume baixo (dezenas de lançamentos por
  dia). Stack local e de CI em Docker Compose; o E2E do CI sobe o stack inteiro a cada PR.
- **Padrões já em uso.** Comunicação entre serviços por HTTP com OAuth2 próprio; o `identity` já
  emite tokens por *client credentials* (cliente de serviço `expense-tracking-service` no seed).
  Tracing W3C e métricas via OpenTelemetry (OBS-2/OBS-3) cobrem HTTP sem código extra.
- **Licenças.** O projeto evita bibliotecas que passaram a ser pagas para uso comercial (já evitou
  MediatR e FluentAssertions). O MassTransit passou a ter licença comercial a partir da v9; a
  alternativa direta é o `RabbitMQ.Client` (Apache-2.0/MPL-2.0).
- **Testes.** Os testes de integração do CI rodam sem containers (EF InMemory + fakes). Uma
  dependência de infraestrutura nova só seria exercitada no E2E, ou exigiria Testcontainers.
- **Reconstrução da projeção.** Projeções erram e mudam de forma. Poder reconstruir a projeção do
  zero, reprocessando o histórico, é um requisito prático.

### Requisitos que valem para qualquer transporte

Levantados no código atual; afetam o desenho dos eventos, não a escolha entre as opções.

1. **Dois eventos, não um.** O backlog cita só `ExpenseRegistered`, mas existe exclusão de
   lançamento, e ela é **física** (a linha é removida). A projeção precisa de
   `TransactionRegistered` e `TransactionDeleted`, gravados no outbox no mesmo `SaveChanges` da
   mudança. O nome é `Transaction*` porque o tipo pode ser despesa ou receita.
2. **Escopo PF/PJ no evento.** O resumo agrupa por escopo, que é dado do `accounts`. O escopo de uma
   conta **não muda** depois de criada (a conta só é renomeada ou desativada). Por isso o
   `transactions` pode capturar o escopo quando já valida a conta no `accounts` e gravá-lo no evento,
   sem o `reporting` precisar de eventos de conta.
3. **Banco próprio no reporting.** A projeção precisa de armazenamento: um MySQL
   `expense_reporting`, como nos demais serviços.
4. **Projeção por lançamento.** O resumo filtra por dono, conta e período. A projeção guarda uma
   linha por lançamento e reaproveita o `SummaryCalculator`.
5. **Carga inicial.** Lançamentos anteriores ao outbox não geram eventos. A migração do EVT-1 precisa
   gravar um `TransactionRegistered` para cada lançamento existente.
6. **Rastreabilidade.** O evento guarda o `traceparent` do request que o originou, para ligar o
   processamento ao trace original no Jaeger.

## Opções consideradas

### Opção A — Broker de mensagens (RabbitMQ)

O `transactions` grava no outbox; um *relay* em segundo plano lê o outbox, publica cada evento numa
exchange do RabbitMQ e marca o evento como publicado. O `reporting` consome de uma fila durável, com
*ack* manual, uma tabela *inbox* (ids de eventos já processados) para idempotência e uma fila de
mensagens mortas (DLQ) para mensagens que falham sempre.

- **Prós:**
  - Padrão de mercado para eventos entre serviços; *push*, com latência de milissegundos.
  - Desacopla a disponibilidade: o broker guarda as mensagens se o `reporting` estiver fora, e o
    `transactions` não expõe nenhuma API nova.
  - Novos consumidores entram criando uma fila ligada à exchange, sem mexer no produtor.
- **Contras:**
  - **Infraestrutura nova** no compose e no E2E do CI (container, credenciais, readiness), mais
    uma peça para operar.
  - **Mais código de mensageria:** topologia (exchange/fila/DLQ), reconexão, ack/nack, relay com
    retentativa. Sem MassTransit (licença), isso fica sobre o `RabbitMQ.Client` direto, ou numa
    alternativa como o Wolverine, que é um framework opinativo.
  - **Ordem não garantida em retentativas:** um *requeue* pode entregar uma exclusão antes do
    cadastro correspondente; a projeção precisa tolerar isso (ex.: guardar a exclusão como marcador).
  - **Reconstrução não é natural:** a fila apaga o que foi consumido. Reprocessar o histórico exige
    um mecanismo à parte (ex.: o relay republicar do outbox retido).
  - **Tracing exige trabalho:** o contexto W3C precisa ir nos headers da mensagem. O
    `RabbitMQ.Client` 7 tem suporte a OpenTelemetry, mas a integração precisa ser validada num spike.
  - Testes do adaptador do broker ficam no E2E ou exigem Testcontainers.

### Opção B — Outbox exposto como feed HTTP (polling)

O outbox do `transactions` é um **log de eventos** retido, com uma sequência crescente. Um endpoint
interno `GET /events?after={seq}&limit={n}` devolve os eventos em ordem. O `reporting` roda um
*worker* em segundo plano que, a cada poucos segundos, lê a partir do último *checkpoint* e grava
**na mesma transação** a projeção e o novo checkpoint. O acesso usa *client credentials* com um
escopo dedicado (ex.: `events.read`) e não passa pelo gateway.

- **Prós:**
  - **Nenhuma infraestrutura nova:** só HTTP, OAuth2 e MySQL, que o projeto já usa e testa.
  - **Idempotência simples:** checkpoint e projeção no mesmo commit dão efeito *exactly-once*; o
    id do evento como chave única cobre o resto.
  - **Ordem natural** pela sequência (ver o risco de lacunas abaixo).
  - **Reconstrução trivial:** zerar o checkpoint reprocessa o log desde o início. A carga inicial
    (requisito 5) entra pelo mesmo caminho.
  - **Tracing e métricas já funcionam** nas chamadas de polling (OBS-2/OBS-3), sem código extra.
  - Testável sem containers: o feed é um endpoint e o worker usa um cliente HTTP, como hoje.
  - Mantém o resumo de pé com o `transactions` fora: o `reporting` serve a projeção, só que
    defasada até o `transactions` voltar.
- **Contras:**
  - **Latência de polling:** segundos (o intervalo do worker), não milissegundos. Aceitável para
    relatórios.
  - **Risco de lacunas na sequência:** com auto-incremento, uma transação que pegou a sequência 10
    pode commitar depois da 11. Um leitor que já avançou para 11 perde o 10. Mitigação conhecida:
    só servir eventos mais antigos que uma margem de segurança (ex.: 5 s) ou não avançar o
    checkpoint por cima de uma lacuna até ela expirar.
  - **Acoplamento de API:** o `reporting` depende de um contrato HTTP do `transactions` (versionado
    como os demais), e o `transactions` precisa estar no ar para a projeção avançar.
  - O log cresce sem limpeza. No volume do projeto isso é desprezível, mas exige uma política de
    retenção se o volume mudar.
  - Cada consumidor novo faz o próprio polling; com muitos consumidores, um broker passa a valer a pena.

### Opção C — Captura de mudanças no banco (CDC com Debezium + Kafka)

O Debezium lê o binlog do MySQL do `transactions` (ou a tabela outbox) e publica no Kafka.

- **Prós:** sem código de publicação no `transactions`; escala para volumes muito maiores.
- **Contras:** três ou mais containers pesados (Kafka, Kafka Connect/Debezium, JVM) no compose e no
  CI; operação e diagnóstico complexos; sem o outbox, os eventos espelham tabelas, não o domínio.
  **Descartada** pela desproporção entre o custo e o volume do projeto.

### Comparação

| Critério | A — Broker (RabbitMQ) | B — Feed HTTP do outbox |
|---|---|---|
| Infraestrutura nova | RabbitMQ no compose e no CI | Nenhuma |
| Latência até a projeção | Milissegundos | Segundos (intervalo de polling) |
| Garantia de entrega | Pelo menos uma vez + inbox | Pelo menos uma vez + checkpoint transacional |
| Ordem dos eventos | Por fila; quebra em retentativas | Pela sequência; exige tratar lacunas |
| Reconstrução da projeção | Mecanismo à parte | Zerar o checkpoint |
| `transactions` fora do ar | Projeção avança com o que já foi publicado | Projeção para até ele voltar; resumo segue servindo |
| Tracing ponta a ponta | Propagar contexto nos headers (spike) | Já coberto; guardar o `traceparent` no evento |
| Novos consumidores | Uma fila nova, sem mexer no produtor | Cada um faz o próprio polling |
| Testes no CI sem containers | Adaptador só no E2E ou Testcontainers | Sim |
| Código novo | Relay, topologia, inbox, DLQ, reconexão | Endpoint de feed, worker, checkpoint |

## Decisão

**Recomendação (aguardando o decisor): Opção B — outbox exposto como feed HTTP.**

Racional:

- O ganho principal do broker (latência de milissegundos e muitos consumidores) não aparece no
  projeto hoje: há um consumidor e o volume é baixo. Os custos dele aparecem em toda execução do CI
  e em toda sessão local.
- A opção B reaproveita o que o projeto já domina e já observa (HTTP, OAuth2 com client credentials,
  OpenTelemetry), e deixa a idempotência e a reconstrução da projeção mais simples do que no broker.
- A escolha é **reversível**: o outbox e o formato dos eventos são os mesmos nas duas opções. Migrar
  para um broker depois troca só o adaptador de leitura do `reporting` (uma porta, como
  `IEventSource`) e acrescenta um relay no `transactions`.

**Quando preferir a Opção A:** se houver mais de um consumidor de eventos à vista, se a projeção
precisar refletir um lançamento em menos de um segundo, ou se praticar mensageria com broker for um
objetivo do projeto por si só.

## Consequências

Para a opção recomendada (B):

- **Positivas:**
  - O resumo deixa de depender do `transactions` e do `accounts` no momento da consulta.
  - Nenhum container novo; CI e ambiente local continuam com o mesmo custo.
  - Reprocessar o histórico vira operação de rotina (zerar o checkpoint), útil para corrigir ou
    mudar a projeção.
- **Negativas:**
  - Defasagem de alguns segundos entre o lançamento e o resumo; a SPA precisa conviver com isso
    (ex.: o E2E que confere o Resumo logo após lançar precisa esperar a projeção).
  - Lógica de margem contra lacunas na sequência, que precisa de teste dedicado.
  - Novo contrato HTTP interno (`/events`) e novo escopo OAuth (`events.read`) para manter.
  - Novo banco (`expense_reporting`) no compose e na migração.
- **Neutras / a observar:**
  - Crescimento do log de eventos (definir retenção se o volume mudar).
  - Métrica de atraso da projeção (diferença entre a última sequência do feed e o checkpoint),
    exposta pelo pipeline da OBS-3, para alertar se o worker parar.
  - Se aparecer um segundo consumidor ou um requisito de baixa latência, reabrir esta decisão com
    uma nova ADR que substitua esta.

## Notas

- Padrões de referência: *Transactional Outbox*, *Polling Publisher* e *Event Feed* (consumo por
  cursor), descritos em microservices.io.
- Itens a validar no início da implementação, em qualquer opção: o formato do evento (`version`,
  `eventId`, `occurredAt`, `ownerUserId`, `traceparent`, payload) e a carga inicial dos lançamentos
  existentes.
- Se a Opção A for escolhida: fazer um spike curto com `RabbitMQ.Client` 7 cobrindo publicação com
  confirmação, consumo com ack manual, DLQ e propagação do contexto de tracing.
