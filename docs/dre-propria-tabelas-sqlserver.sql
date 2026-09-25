-- DRE própria de um cliente (cliente piloto) direto nas tabelas do ERP (sem a viewFaturamentoLV, que se desconfigura nas atualizações do ERP).
-- Mesmas regras da viewFaturamentoLV para receita/devolução, usada como referência do que se considera e do que se descarta:
--   CONSIDERA: itens de documentos dos tipos Cupom Fiscal, Nota Fiscal, Nota Fiscal Fatura e Conhecimento de Frete
--              ([Tipos de Documentos Fisca].Tipodocfiscal), emitidos a partir de 2018.
--   DESCARTA:  condições de faturamento marcadas "Nao considera faturamento" (e documentos sem condição cadastrada);
--              documentos com Valor Total <= 0, ou compostos só de frete ou só de despesas acessórias.
--   DEVOLUÇÃO: CFOP cuja descrição ([Descricao dos Codigos Fis].Devolucao) é 'Devolução de Venda' → sinal -1 (vai para Deduções).
--   VALOR:     (Preço com IPI + ST + frete do item + despesas acessórias do item) com o desconto do documento rateado:
--              × Valor Total / (Valor Total + Valor do desconto). Frete/despesas do item só se o documento tiver frete/despesas.
-- Descartado da view (só servia para descrições): Pessoas, Estruturas de vendas/estoque, Locais de pagamento, Depto compras, DIASEMANA.
-- CMV: nossa regra (Quantidade × Custo reposição médio; sem ele, Custo Atual), com o sinal da devolução.
-- Despesas: iguais ao modelo padrão do portal.
-- {inicio} e {fim} são trocados pelo ano escolhido. Somente leitura.

SELECT DATEADD(month, DATEDIFF(month, 0, x.Data), 0) AS Competencia, x.Grupo, x.Conta, SUM(x.Valor) AS Valor
FROM (
    -- RECEITA e CMV: uma passada nos itens, duas linhas por item (CROSS APPLY)
    SELECT v.Data, l.Grupo, l.Conta, l.Valor
    FROM (
        SELECT ISNULL(DF.[Data competencia], DF.[Data de Emissao]) AS Data,
               I.[Codigo do tipo de documen] AS Tipo,
               CASE WHEN DCF.Devolucao = 'Devolução de Venda' THEN -1 ELSE 1 END AS Sinal,
               (I.[Preco com IPI] + I.[Subst Tributaria Valor]
                + CASE WHEN DF.[Valor Frete] > 0 THEN I.[Valor Frete] ELSE 0 END
                + CASE WHEN DF.[Valor despesas acessorias] > 0 THEN I.[Valor despesas acessorias] ELSE 0 END)
               * DF.[Valor Total] / (DF.[Valor Total] + DF.[Valor do desconto]) AS Receita,
               I.Quantidade * CASE WHEN I.[Custo reposicao medio] <> 0 THEN I.[Custo reposicao medio] ELSE I.[Custo Atual] END AS Custo
        FROM [Itens dos Documentos Fisc] I WITH (NOLOCK)
        INNER JOIN [Documentos Fiscais] DF WITH (NOLOCK)
                ON DF.Filial = I.Filial AND DF.[Codigo do tipo de documen] = I.[Codigo do tipo de documen] AND DF.[Numero do documento] = I.[Numero do documento]
        INNER JOIN [Condicoes de Faturamento] CF WITH (NOLOCK)
                ON CF.Filial = DF.Filial AND CF.[Numero da Lista] = DF.[Numero da Lista] AND CF.Condicao = DF.Condicao
        INNER JOIN [Tipos de Documentos Fisca] TDF WITH (NOLOCK)
                ON TDF.Filial = I.Filial AND TDF.[Codigo do tipo de documen] = I.[Codigo do tipo de documen]
        LEFT JOIN [Descricao dos Codigos Fis] DCF WITH (NOLOCK)
                ON DCF.[Codigo da descricao ciof] = I.CIOF
        WHERE CF.[Nao considera faturamento] = 0
          AND DF.[Valor Total] > 0
          AND DF.[Valor Total] - DF.[Valor despesas acessorias] > 0
          AND DF.[Valor Total] - DF.[Valor Frete] > 0
          AND DF.[Data de Emissao] >= '20180101'
          AND TDF.Tipodocfiscal IN ('Cupom Fiscal', 'Nota Fiscal', 'Nota Fiscal Fatura', 'Conhecimento de Frete')
          AND ISNULL(DF.[Data competencia], DF.[Data de Emissao]) >= {inicio}
          AND ISNULL(DF.[Data competencia], DF.[Data de Emissao]) < {fim}
    ) v
    CROSS APPLY (VALUES
        -- Receita: devolução vai para Deduções com valor positivo
        (CASE WHEN v.Sinal = -1 THEN 'Deducoes' ELSE 'ReceitaBruta' END,
         CASE WHEN v.Sinal = -1 THEN 'Devoluções de vendas'
              WHEN v.Tipo = 'C' THEN 'Vendas NFC-e'
              WHEN v.Tipo = 'N' THEN 'Vendas NF-e'
              WHEN v.Tipo IN ('V', 'VA') THEN 'Vendas sem nota (V/VA)'
              ELSE 'Outros documentos (' + v.Tipo + ')' END,
         v.Receita),
        -- CMV: devolução estorna
        ('Cmv',
         CASE WHEN v.Sinal = -1 THEN 'Custo das devoluções (estorno)' ELSE 'Custo das mercadorias vendidas' END,
         v.Sinal * v.Custo)
    ) l (Grupo, Conta, Valor)

    UNION ALL

    -- DESPESAS (iguais ao modelo padrão)
    SELECT COALESCE(t.[Data competencia], t.[Data de Emissao]),
           CASE
             WHEN t.[Codigo do cc] IN ('02.06.02', '02.06.04', '02.06.06', '02.06.07', '01.80.01') THEN 'Deducoes'
             WHEN t.[Codigo do cc] IN ('02.04.03', '02.04.83', '02.04.87', '02.04.100') THEN 'DespesasFinanceiras'
             ELSE 'DespesasOperacionais'
           END,
           ISNULL(cc.[Descricao do cc], t.[Codigo do cc]),
           t.[Valor Nominal]
    FROM [Titulos do Contas a Pagar] t
    LEFT JOIN [Centro de custo] cc ON cc.Filial = t.Filial AND cc.[Codigo do cc] = t.[Codigo do cc]
    WHERE ISNULL(t.Eprevisao, 0) = 0
      AND COALESCE(t.[Data competencia], t.[Data de Emissao]) >= {inicio}
      AND COALESCE(t.[Data competencia], t.[Data de Emissao]) < {fim}
      -- parcelas futuras só entram quando o mês chega:
      AND COALESCE(t.[Data competencia], t.[Data de Emissao]) <= (SELECT MAX(u.[Data de Emissao]) FROM [Documentos Fiscais] u WHERE u.Situacao = 'G' AND u.[Codigo do tipo de documen] IN ('N', 'C', 'V', 'VA'))
      AND (   t.[Codigo do cc] LIKE '02.01.%' OR t.[Codigo do cc] LIKE '02.03.%' OR t.[Codigo do cc] LIKE '02.04.%'
           OR t.[Codigo do cc] LIKE '02.05.%' OR t.[Codigo do cc] LIKE '02.06.%' OR t.[Codigo do cc] LIKE '02.09.%'
           OR t.[Codigo do cc] LIKE '08.%'    OR t.[Codigo do cc] LIKE '10.%'
           OR t.[Codigo do cc] IN ('01.80.01', '01.80.02', '01.80.05', '1.80.05'))
      AND t.[Codigo do cc] NOT IN ('02.01.08', '02.04.05', '02.04.10')
) x
GROUP BY DATEADD(month, DATEDIFF(month, 0, x.Data), 0), x.Grupo, x.Conta
