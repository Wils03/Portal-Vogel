SELECT P.[Codigo do Produto] AS Codigo,
       P.Descricao,
       PD.[Custo Atual] AS Custo,
       PR.Preco1 AS Preco,
       ISNULL(V.Quantidade, 0) / 3 AS QtdMes,
       ISNULL(G.Percentual, 0) AS ImpostosPct,
       GF.Descricao AS GrupoPreco,
       PR.[Lucro Desejado] AS LucroDesejadoPct,
       PR.[Lucro desejado sobre] AS LucroSobre
FROM Produtos P
INNER JOIN Produtos_dados PD
        ON PD.[Codigo do Produto] = P.[Codigo do Produto] AND PD.Filial = {filial}
INNER JOIN Precos PR
        ON PR.[Codigo do Produto] = P.[Codigo do Produto] AND PR.Filial = PD.Filial AND PR.[Numero da Lista] = 1
LEFT JOIN [Grupos formacao preco] GF
       ON GF.[Codigo grupo form preco] = PD.[Codigo grupo form preco]
LEFT JOIN (SELECT GI.[Codigo grupo form preco], SUM(GI.Percentual) AS Percentual
           FROM [Grupo form preco itens] GI
           GROUP BY GI.[Codigo grupo form preco]) G
       ON G.[Codigo grupo form preco] = PD.[Codigo grupo form preco]
LEFT JOIN (SELECT I.[Codigo do Produto],
                  SUM(CASE WHEN I.[Codigo Fiscal] IN (2, 4) THEN -I.Quantidade ELSE I.Quantidade END) AS Quantidade
           FROM [Documentos Fiscais] D
           INNER JOIN [Itens dos Documentos Fisc] I
                   ON I.Filial = D.Filial AND I.[Codigo do tipo de documen] = D.[Codigo do tipo de documen] AND I.[Numero do documento] = D.[Numero do documento]
           WHERE D.Filial = {filial}
             AND D.Situacao = 'G'
             AND D.[Codigo do tipo de documen] IN ('N', 'C', 'V', 'VA')
             AND I.[Codigo Fiscal] IN (1, 3, 28, 2, 4)
             -- últimos 90 dias até a última venda registrada
             AND D.[Data de Emissao] >= DATEADD(day, -90, (SELECT MAX(U.[Data de Emissao]) FROM [Documentos Fiscais] U
                                                           WHERE U.Situacao = 'G' AND U.[Codigo do tipo de documen] IN ('N', 'C', 'V', 'VA')))
           GROUP BY I.[Codigo do Produto]) V
       ON V.[Codigo do Produto] = P.[Codigo do Produto]
WHERE ISNULL(P.Desativado, 0) = 0
  AND PR.Preco1 > 0
