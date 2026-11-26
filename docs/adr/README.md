# Architecture Decision Records

Decisões arquiteturais do expense.tracking, no formato de Michael Nygard. Cada ADR registra o
contexto, as opções avaliadas, a decisão e as consequências. ADRs aceitos não são reescritos: uma
mudança de decisão vira um novo ADR que substitui o anterior.

| ADR | Título | Status | Data |
|---|---|---|---|
| [0001](0001-transporte-de-eventos-transactions-reporting.md) | Transporte de eventos entre transactions e reporting (épico EVT) | Aceito | 2026-11-19 |
| [0002](0002-orcamentos-no-reporting.md) | Orçamentos por categoria ficam no reporting (épico BDG) | Aceito | 2026-11-20 |
| [0003](0003-identity-em-repositorio-proprio.md) | Identity em repositório próprio, consumido como imagem versionada (épico IDX) | Aceito | 2026-11-21 |
| [0004](0004-importacao-csv-ofx-no-transactions.md) | Importação de extrato e fatura (CSV/OFX) no transactions, com lote persistido (épico IMP) | Aceito | 2026-11-21 |
| [0005](0005-fatura-de-cartao-e-previsao-de-caixa.md) | Fatura de cartão e previsão de caixa no reporting, com parcelas e pagamento de fatura (épico INV) | Aceito | 2026-11-21 |
| [0006](0006-exportacao-contabil-pj-no-transactions.md) | Exportação contábil PJ gerada no transactions, como pacote ZIP de CSVs (épico CTB) | Aceito | 2026-11-22 |
| [0007](0007-edicao-de-lancamentos-e-receita-na-spa.md) | Edição de lançamentos com evento próprio e concorrência otimista, e receita na SPA (épico LNC) | Aceito | 2026-11-22 |
| [0008](0008-lancamentos-recorrentes.md) | Lançamentos recorrentes como regras que só projetam, com ocorrências lançadas ou puladas pelo usuário (épico REC) | Aceito | 2026-11-23 |
| [0009](0009-lembretes-de-vencimento.md) | Lembretes de vencimento calculados no reporting, no app e por e-mail diário confirmado (épico NTF) | Aceito | 2026-11-23 |
| [0010](0010-reajuste-das-recorrencias.md) | Reajuste das recorrências com valor por vigência, sugerido pelo índice oficial e confirmado pelo usuário (épico RJT) | Aceito | 2026-11-25 |
| [0011](0011-conciliacao-da-fatura-do-cartao.md) | Conciliação da fatura do cartão calculada no reporting, com o arquivo do banco lido do lote de importação (épico RCN) | Aceito | 2026-11-26 |
