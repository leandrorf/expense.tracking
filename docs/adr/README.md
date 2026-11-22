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
