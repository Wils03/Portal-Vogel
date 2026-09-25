-- SUBSTITUÍDO em 25/09/2026 por docs/dre-propria-tabelas-sqlserver.sql (mesmas regras, direto nas tabelas; a view se desconfigura nas atualizações do ERP).
-- DRE do cliente piloto (salvo só no cadastro dele): receita pela viewFaturamentoLV do ERP, que bate com a DRE do ERP.
-- Só lê uma visão que o próprio ERP já cria no banco do cliente; não cria nem altera nada.
-- Em relação ao modelo padrão, mudou a receita/devoluções e os documentos do CMV. Despesas iguais.
-- {inicio} e {fim} são trocados pelo portal pelo ano escolhido.

SELECT DATEADD(month, DATEDIFF(month, 0, x.Data), 0) AS Competencia, x.Grupo, x.Conta, SUM(x.Valor) AS Valor
FROM (
    -- RECEITA: mesma regra da tela de indicadores do ERP
    --   (tipos Cupom/Nota Fiscal/NF Fatura/Conhecimento de Frete, sem condições "Não considera faturamento",
    --    desconto rateado, + ST, frete e despesas acessórias)
    -- Devolução (eDev = -1) vai para Deduções com o valor positivo.
    SELECT ISNULL(v.[Data competencia], v.[Data de Emissao]) AS Data,
           CASE WHEN v.eDev = -1 THEN 'Deducoes' ELSE 'ReceitaBruta' END AS Grupo,
           CASE WHEN v.eDev = -1 THEN 'Devoluções de vendas'
                WHEN v.[Codigo do tipo de documen] = 'C' THEN 'Vendas NFC-e'
                WHEN v.[Codigo do tipo de documen] = 'N' THEN 'Vendas NF-e'
                WHEN v.[Codigo do tipo de documen] IN ('V', 'VA') THEN 'Vendas sem nota (V/VA)'
                ELSE 'Outros documentos (' + v.[Codigo do tipo de documen] + ')' END AS Conta,
           v.eDev * v.ValorTotal AS Valor
    FROM viewFaturamentoLV v
    WHERE ISNULL(v.[Data competencia], v.[Data de Emissao]) >= {inicio}
      AND ISNULL(v.[Data competencia], v.[Data de Emissao]) < {fim}

    UNION ALL

    -- CMV: mesmos documentos da receita, com a NOSSA regra de custo (a mesma do modelo padrão do portal):
    --   Quantidade × Custo reposição médio; sem ele, Custo Atual. Devolução estorna (eDev = -1).
    --   (Na view, Quantidade e o custo já vêm com o sinal da devolução; um anula o outro, por isso × eDev.)
    -- O CMV do E-Diretor (tela de indicadores) é calculado no programa do ERP: fica para depois.
    SELECT ISNULL(v.[Data competencia], v.[Data de Emissao]),
           'Cmv',
           CASE WHEN v.eDev = -1 THEN 'Custo das devoluções (estorno)' ELSE 'Custo das mercadorias vendidas' END,
           v.eDev * v.Quantidade * CASE WHEN v.CRMedio <> 0 THEN v.CRMedio ELSE v.CA END
    FROM viewFaturamentoLV v
    WHERE ISNULL(v.[Data competencia], v.[Data de Emissao]) >= {inicio}
      AND ISNULL(v.[Data competencia], v.[Data de Emissao]) < {fim}

    UNION ALL

    -- DESPESAS (sem mudança)
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
