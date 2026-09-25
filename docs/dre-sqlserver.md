# DRE a partir do banco do ERP do cliente (SQL Server)

SQL: `src/Dashborad/Services/ModeloDreSqlServer.sql` (somente SELECT; o portal troca `{inicio}`/`{fim}` pelo ano).
Nada é criado ou alterado no banco do cliente.

## De onde vem cada linha

| Linha da DRE | Origem | Regra |
|---|---|---|
| Receita bruta | `Documentos Fiscais` + `Itens dos Documentos Fisc` | Tipos `N` (NF-e), `C` (NFC-e), `V` (venda), `VA` (pré-venda) com `Situacao = 'G'`; itens com código fiscal 1, 3, 28 (CFOP 5102/6102, 5405/6403, 6108). Valor = `Preco Final` (total do item) |
| (-) Deduções | idem + `Titulos do Contas a Pagar` | Devoluções de venda (código fiscal 2, 4 = CFOP 1202/1411) + impostos pagos nos centros de custo 02.06.02 PIS, 02.06.04 ICMS, 02.06.06 ISS, 02.06.07 COFINS, 01.80.01 Simples |
| (-) CMV | itens das vendas | Quantidade × `Custo reposicao medio` (ou `Custo Atual` se zerado); devoluções estornam o custo |
| (-) Despesas operacionais | `Titulos do Contas a Pagar` por `Centro de custo` | 02.01, 02.03, 02.05, 02.06 (demais), 02.09, 08 (pró-labore), 10 (carretas), 01.80.02/05 e 1.80.05 |
| (-) Despesas financeiras | idem | 02.04.03 tarifas, 02.04.83 juros/IOF, 02.04.87 juros de empréstimos, 02.04.100 permuta |

Data dos títulos: `Data competencia`; se vazia, `Data de Emissao`. Títulos de previsão (`Eprevisao`) ficam fora.

## Fica fora da DRE (de propósito)

- 02.02 compras para revenda: o CMV vem do custo do que foi vendido, não do que foi comprado
- 02.01.08 divisão de lucros (distribuição, não despesa)
- 02.04.05 empréstimos (principal) e 02.04.10 consórcios
- 03 investimentos (imobilizado, veículos, imóveis), 05, 09 ajustes de caixa, 11
- 01.03.99 receita complementar areias e 01.01.100 permuta (a confirmar)
- Venda de ativo imobilizado (CFOP 5551), remessas, bonificações, empréstimos de mercadoria, baixas por perda

## Pontos para confirmar com o cliente/contador

1. `Situacao = 'F'` = faturado (virou outro documento); conferido: nenhum V/VA/C com `G` foi convertido.
2. ICMS/PIS/COFINS entram pela data de competência do título de pagamento (em geral o mês seguinte à venda).
3. 01.03.99 "Receita complementar - areias" aparece no contas a pagar (R$ 769 mil em 12 meses): é compra para revenda?
4. Os centros de custo mudam de cliente para cliente. Para outro cliente, revisar os `CASE`/`LIKE` do SQL no portal (DRE → SQL do banco).

## Resultado na base de teste (06/2024 a 05/2025)

Receita média de R$ 2,4 mi/mês, margem bruta de 26% a 33% e lucro líquido perto de 5%.
Maio/2025 está incompleto (a base termina em 29/05).
