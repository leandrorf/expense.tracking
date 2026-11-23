# ADR-0009: Lembretes de vencimento calculados no reporting, no app e por e-mail diário confirmado (épico NTF)

- **Status:** Aceito
- **Data:** 2026-11-23
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico NTF (história NTF-1), `docs/backlog.md`, ADR-0001 (feed de eventos), ADR-0002
  (dado do usuário no reporting), ADR-0003 (identity em repositório próprio), ADR-0005 (fatura de cartão),
  ADR-0008 (lançamentos recorrentes)

## Contexto

O produto sabe o que vai vencer (a fatura de cada cartão com a situação dela, as ocorrências recorrentes e
o que está "a lançar"), mas só mostra isso a quem abre a aba certa. Fatura fechada esquecida vira fatura
vencida; boleto recorrente passa do dia. Os épicos BDG e INV recusaram "notificação ativa" porque não havia
infraestrutura. O épico NTF avisa no app, com atalho para resolver, e, para quem quiser, por um e-mail
diário.

### Achados no código

- **O reporting já tem o que vence:** faturas com situação, vencimento e quanto falta (`InvoiceCalculator`,
  ADR-0005) e recorrências com os períodos fechados (projeção da REC-4, ADR-0008), além de "hoje" no fuso
  configurado (`Reports:TimeZone`) e um worker (`ProjectionWorker`).
- **O ciclo do cartão vem do `accounts` com o token do usuário** (ADR-0005, decisão 3). Um job sem
  usuário não consegue calcular faturas hoje.
- **Há precedente de serviço lendo o `accounts`:** `/internal/**`, que o gateway bloqueia de propósito
  (`/api/internal/**` responde 404 antes do catch-all `/api/**`), com escopo de serviço (`accounts.scopes.read`, EVT-4). Os
  escopos do cliente de serviço ficam na semente do identity deste repositório
  (`infra/identity/appsettings.Production.json`).
- **Nenhum serviço conhece o e-mail do usuário.** A SPA não pede o escopo `email` e o `identity` vive em
  repositório próprio (ADR-0003). Não há SMTP, biblioteca de e-mail nem servidor de e-mail de
  desenvolvimento.

## Opções consideradas

### 1. Canal

- **(a) Avisos no app**, calculados na consulta com o token do usuário.
- **(b) E-mail diário**, enviado por um job.
- **(c) Push do navegador**: service worker, chaves VAPID e permissão do navegador, para pouco ganho sobre o
  e-mail num app que o usuário abre com frequência.

### 2. Lugar

- **(a) No `reporting`**, ao lado das fontes dos avisos (fatura, recorrências, orçamentos).
- **(b) Serviço novo de notificações**, que teria de ler faturas e recorrências de outro serviço.

### 3. Endereço de e-mail

- **(a) Do `identity`** (escopo `email` e serviço consultando o usuário): muda outro repositório.
- **(b) Informado pelo usuário e confirmado por link** de uso único: não depende do `identity`; um erro de
  digitação não manda dados financeiros a terceiros.

## Decisão

**Os avisos são calculados no `reporting` a cada consulta e nunca guardados; a SPA os mostra num sino com
painel. Um resumo diário por e-mail, opt-in e com endereço confirmado, é montado pelo mesmo cálculo num
job do `reporting`, que lê os cartões pelo endpoint interno do `accounts`.** As regras abaixo valem para as
histórias NTF-2 a NTF-9.

1. **Canal** (opção 1a no Must, 1b no Should): avisos no app primeiro; e-mail depois; push fica fora.
2. **Lugar** (opção 2a): `reporting`, sem serviço novo. `GET /reports/reminders` (`expenses.read`), pela
   rota do gateway que já existe. Só a dispensa e as preferências são gravadas.
3. **Tipos de aviso**, cada um com uma **chave estável** (para dispensar) e uma **urgência**
   (`Overdue` > `Today` > `Soon`):
   - `InvoiceDue`: fatura não paga por inteiro (Fechada ou Parcialmente paga) com vencimento de hoje até
     N dias. Chave `invoice:{cartão}:{yyyy-MM}:due`. Traz cartão, vencimento e quanto falta.
   - `InvoiceOverdue`: fatura Vencida sem pagamento total, até 30 dias depois do vencimento. Chave
     `invoice:{cartão}:{yyyy-MM}:overdue`; por ser outra chave, volta mesmo se o "a vencer" foi dispensado.
   - `RecurrenceDue`: ocorrência em aberto de uma regra de **saída** (gasto, inclusive no cartão) com data
     de hoje até N dias. Chave `recurrence:{regra}:{período}`. Receita não gera aviso.
   - `RecurrencesToLaunch`: um aviso só com a quantidade de ocorrências "a lançar" (mesma regra da REC-3:
     em aberto, de 60 dias atrás até ontem). Chave `to-launch:{data agendada mais recente}`, então uma
     pendência nova o faz voltar.
   - `Budget` (Could): orçamento do mês em Atenção ou Estourado. Chave `budget:{id}:{yyyy-MM}:{situação}`.
   
   A resposta vem ordenada por urgência e data, e cada aviso leva o destino do atalho (decisão 7).
4. **Antecedência e preferências:** N = **3 dias** por padrão, de 0 a 15 (fora disso, 400
   `reminders.lead_days_invalid`); cada tipo pode ser desligado. Tabela `reminder_settings` no reporting,
   uma linha por usuário, criada na primeira gravação (sem linha, vale o padrão). Rotas
   `GET/PUT /reports/reminder-settings` (`expenses.write` para gravar).
5. **Dispensar:** `POST /reports/reminders/dismissals` com a chave; tabela `reminder_dismissals` (dono,
   chave, quando). O aviso dispensado some até a chave mudar (decisão 3). Resolver (pagar, lançar, pular)
   tira o aviso sem dispensa. Dispensas com mais de 120 dias são apagadas pelo worker.
6. **E-mail diário** (opção 3b):
   - Preferências ganham `emailEnabled`, `email`, `emailConfirmedAtUtc` e `sendHour` (0 a 23, padrão 8, no
     fuso do app). Trocar o endereço zera a confirmação e gera um token de **uso único**, válido por
     **24 h**, guardado só como hash. O e-mail de confirmação traz um link para a SPA
     (`/confirm-email?token=...`); a SPA, já logada, chama `POST /reports/reminder-settings/email/confirm`.
     Não há rota anônima.
   - Um job do reporting roda de hora em hora: para cada usuário com e-mail confirmado e ativo, na hora
     escolhida e sem envio no dia, calcula os avisos (sem os dispensados) e envia **só se houver chave que
     não estava no último envio**. Guarda as chaves enviadas e a data.
   - **Cartões para o job:** `GET /internal/credit-cards?ownerUserId=...` no `accounts`, com os cartões do
     dono (ativos ou não) e o ciclo, protegido por um escopo de serviço novo, `accounts.cards.read`, dado
     ao cliente `expense-tracking-service` na semente do identity. O bloqueio de `/api/internal/**` no gateway já o cobre.
   - **Envio:** SMTP configurável (`Smtp:Host`, `Port`, `From`, `User`, `Password`, `UseTls`) com MailKit
     (licença MIT). O compose de desenvolvimento ganha o Mailpit, e o E2E confere o e-mail pela API dele.
     Sem `Smtp:Host`, o job não envia e registra no log. O e-mail traz descrição, valor e data, um link para
     o app (que pede login: nada de link mágico) e um link para desligar o resumo nas preferências.
7. **Atalhos:** fatura → aba Cartões com aquela fatura aberta; ocorrência e "a lançar" → aba Recorrências;
   orçamento → aba Orçamentos. A SPA relê os avisos ao abrir o painel e quando a janela volta ao foco.

## Consequências

- **Positivas:**
  - Fatura e boleto recorrente deixam de passar sem que o usuário perceba, sem infraestrutura nova no Must.
  - Os avisos nunca ficam velhos: calculados a cada consulta, somem quando o problema é resolvido.
  - O e-mail não depende de mudar o `identity`.
- **Negativas:**
  - O `reporting` ganha um job de envio, três tabelas (`reminder_settings`, `reminder_dismissals` e o
    registro do último envio dentro das preferências) e uma dependência de SMTP.
  - O `accounts` ganha um endpoint interno e o cliente de serviço, um escopo a mais.
  - O usuário informa o e-mail de aviso à parte do e-mail de login.
- **Neutras / a observar:**
  - O job lê o ciclo dos cartões do `accounts` a cada envio; com muitos usuários, vale agrupar ou guardar
    o ciclo em cache.
  - A antecedência conta dias corridos, sem calendário de dias úteis (como a fatura, ADR-0005).
