# Arquivos de exemplo da importação (IMP)

Arquivos **sintéticos**, escritos à mão para os testes: bancos, contas, cartões, valores e
estabelecimentos são inventados. Nunca coloque aqui um extrato ou fatura real, nem "anonimizado".

| Arquivo | Formato | Conteúdo |
|---|---|---|
| `extrato-conta-ofx1.ofx` | OFX 1.02 (SGML), Windows-1252 | Extrato de conta corrente: débitos (padaria, Pix, boleto com vírgula decimal e `NAME` sem `MEMO`), crédito de salário e um `FITID` repetido. |
| `fatura-cartao-ofx2.ofx` | OFX 2.11 (XML), UTF-8 | Fatura de cartão: compras como débito, pagamento da fatura como crédito. |
| `extrato-conta.csv` | CSV `;`, vírgula decimal, Windows-1252 | Extrato com cabeçalho e coluna de saldo: dois gastos idênticos no mesmo dia, descrição com `;` entre aspas, linha de saldo sem valor e uma data inválida. |
| `fatura-cartao.csv` | CSV `,`, ponto decimal, UTF-8 com BOM | Fatura no estilo de banco digital: compras **positivas** e pagamento negativo (precisa de "inverter sinal"). |
| `extrato-debito-credito.csv` | CSV com `sep=;` do Excel, UTF-8 | Débito e crédito em colunas separadas. |
