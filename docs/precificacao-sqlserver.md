# Precificação a partir do banco do ERP do cliente (SQL Server)

SQL: `src/Dashborad/Services/ModeloPrecificacaoSqlServer.sql` (somente SELECT, parâmetro `{filial}`).

## Tabelas

| Tabela | Chave | O que traz |
|---|---|---|
| `Produtos` | `Codigo do Produto` | Descrição, `Desativado` |
| `Produtos_dados` | `Filial` + produto | Custos (`Custo Atual`, `Custo Reposicao`, `Custo reposicao medio`, `Custo liquido`, `Custo total`, `Custo Contabil`), estoque, `Codigo grupo form preco` |
| `Precos` | `Filial` + `Numero da Lista` + produto | `Preco1` (preço de venda), `Lucro Desejado`, `Lucro desejado sobre`, `Preco sugerido` |
| `Grupos formacao preco` / `Grupo form preco itens` | grupo | Impostos sobre a venda (ICMS, PIS/COFINS, custo operacional) em % |
| `Lista de Precos` | lista | Nesta base só existe a lista 1 (LISTA PADRAO) |

A consulta original `Produtos JOIN Produtos_dados JOIN Precos` precisa filtrar **filial** e **lista**. Sem isso, cada produto se repete por filial × lista.

## Regra de formação de preço do ERP (conferida em `Precos sugeridos`)

Custo usado: **Custo Atual**.

- Lucro desejado sobre o **Preço de Venda** (margem): `Preço = Custo ÷ (1 − (impostos do grupo + lucro) / 100)`
- Lucro desejado sobre o **Custo** (markup): `Preço = Custo × (1 + lucro / 100) ÷ (1 − impostos / 100)`

O ERP ainda arredonda o preço final (ex.: 453,03 → 455,00).
Nesta base, 41.164 preços usam margem e 36 usam markup.

## Tipos de custo e impostos

- As flags dos grupos (`Consid cred icms cad produto`, `Nao consid cred ult ent`, `Desconta icms base pis cofins`, `Trib entr da ult nf entr`)
  definem se o crédito de ICMS da entrada é descontado. **Neste cliente estão todas desligadas.** Em outro cliente, conferir e,
  se necessário, trocar `Custo Atual` por `Custo liquido` no SQL (DRE → Precificação → SQL do banco).
- Quantidade/mês = vendas líquidas de devoluções nos últimos 90 dias (até a última venda) ÷ 3.

## No portal

- **Regra ERP**: preço que o ERP do cliente sugeriria com o grupo e o lucro cadastrados; "Dif." mostra quanto o preço atual está acima ou abaixo.
- **Sugerido c/ fixas**: preço que cobre impostos do grupo + comissão + despesas fixas% (da DRE) + margem desejada.
- **Margem líquida**: margem real do preço atual depois de impostos, comissão e despesas fixas.
