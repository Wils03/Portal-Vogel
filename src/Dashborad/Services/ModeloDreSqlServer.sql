SELECT DATEADD(month, DATEDIFF(month, 0, x.Data), 0) AS Competencia, x.Grupo, x.Conta, SUM(x.Valor) AS Valor
FROM (
    SELECT d.[Data de Emissao] AS Data,
           CASE WHEN i.[Codigo Fiscal] IN (2, 4) THEN 'Deducoes' ELSE 'ReceitaBruta' END AS Grupo,
           CASE WHEN i.[Codigo Fiscal] IN (2, 4) THEN 'Devoluções de vendas'
                WHEN d.[Codigo do tipo de documen] = 'C' THEN 'Vendas NFC-e'
                WHEN d.[Codigo do tipo de documen] = 'N' THEN 'Vendas NF-e'
                ELSE 'Vendas sem nota (V/VA)' END AS Conta,
           i.[Preco Final] AS Valor
    FROM [Documentos Fiscais] d
    INNER JOIN [Itens dos Documentos Fisc] i
            ON i.Filial = d.Filial AND i.[Codigo do tipo de documen] = d.[Codigo do tipo de documen] AND i.[Numero do documento] = d.[Numero do documento]
    WHERE d.Situacao = 'G'
      AND d.[Codigo do tipo de documen] IN ('N', 'C', 'V', 'VA')
      AND i.[Codigo Fiscal] IN (1, 3, 28, 2, 4)
      AND d.[Data de Emissao] >= {inicio} AND d.[Data de Emissao] < {fim}

    UNION ALL

    SELECT d.[Data de Emissao],
           'Cmv',
           CASE WHEN i.[Codigo Fiscal] IN (2, 4) THEN 'Custo das devoluções (estorno)' ELSE 'Custo das mercadorias vendidas' END,
           CASE WHEN i.[Codigo Fiscal] IN (2, 4) THEN -1 ELSE 1 END
             * i.Quantidade * CASE WHEN ISNULL(i.[Custo reposicao medio], 0) > 0 THEN i.[Custo reposicao medio] ELSE ISNULL(i.[Custo Atual], 0) END
    FROM [Documentos Fiscais] d
    INNER JOIN [Itens dos Documentos Fisc] i
            ON i.Filial = d.Filial AND i.[Codigo do tipo de documen] = d.[Codigo do tipo de documen] AND i.[Numero do documento] = d.[Numero do documento]
    WHERE d.Situacao = 'G'
      AND d.[Codigo do tipo de documen] IN ('N', 'C', 'V', 'VA')
      AND i.[Codigo Fiscal] IN (1, 3, 28, 2, 4)
      AND d.[Data de Emissao] >= {inicio} AND d.[Data de Emissao] < {fim}

    UNION ALL

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
      -- parcelas futuras (empréstimos, parcelamentos, pró-labore programado) só entram quando o mês chega:
      AND COALESCE(t.[Data competencia], t.[Data de Emissao]) <= (SELECT MAX(u.[Data de Emissao]) FROM [Documentos Fiscais] u WHERE u.Situacao = 'G' AND u.[Codigo do tipo de documen] IN ('N', 'C', 'V', 'VA'))
      AND (   t.[Codigo do cc] LIKE '02.01.%' OR t.[Codigo do cc] LIKE '02.03.%' OR t.[Codigo do cc] LIKE '02.04.%'
           OR t.[Codigo do cc] LIKE '02.05.%' OR t.[Codigo do cc] LIKE '02.06.%' OR t.[Codigo do cc] LIKE '02.09.%'
           OR t.[Codigo do cc] LIKE '08.%'    OR t.[Codigo do cc] LIKE '10.%'
           OR t.[Codigo do cc] IN ('01.80.01', '01.80.02', '01.80.05', '1.80.05'))
      AND t.[Codigo do cc] NOT IN ('02.01.08', '02.04.05', '02.04.10')
) x
GROUP BY DATEADD(month, DATEDIFF(month, 0, x.Data), 0), x.Grupo, x.Conta
