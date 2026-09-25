-- Indicadores comerciais por DIA e vendedor (estrutura de venda), DIRETO NAS TABELAS do ERP. Somente leitura.
-- NÃO usa a viewFaturamentoLV (ela se desconfigura nas atualizações do ERP); só reproduz as regras dela, que são as da
-- tela de indicadores do ERP (as mesmas da DRE própria do cliente piloto, docs/dre-propria-tabelas-sqlserver.sql):
--   tipos Cupom Fiscal / Nota Fiscal / Nota Fiscal Fatura / Conhecimento de Frete; sem condições "Não considera faturamento";
--   valor = (preço com IPI + ST + frete + despesas acessórias do item) com o desconto do documento rateado;
--   devolução = CFOP "Devolução de Venda" (sinal -1).
-- Como o relatório "Metas" do ERP: NÃO considera vendas com local de pagamento PERMUTA (no ERP, local 25).
-- Vendedor = estrutura de venda do DOCUMENTO (assim o número de vendas da empresa é a soma dos vendedores).
-- Gerente = estrutura "pai" do vendedor. CMV: quantidade × "Custo total" do item (o "Custo Total" do relatório de metas do ERP).
-- {inicio} e {fim} são trocados pelo período (ano).
SELECT CAST(v.Data AS date) AS Data,
       v.Vendedor,
       MAX(ev.[Descricao da Estrutura]) AS NomeVendedor,
       MAX(ev.[Codigo da estrutura pai]) AS Gerente,
       MAX(eg.[Descricao da Estrutura]) AS NomeGerente,
       SUM(CASE WHEN v.Sinal = 1 THEN v.Receita ELSE 0 END) AS VendaBruta,
       SUM(CASE WHEN v.Sinal = -1 THEN v.Receita ELSE 0 END) AS Devolucoes,
       SUM(v.Sinal * v.Custo) AS Cmv,
       COUNT(DISTINCT CASE WHEN v.Sinal = 1 THEN v.Documento END) AS Documentos,
       SUM(CASE WHEN v.Sinal = 1 THEN 1 ELSE 0 END) AS Itens
FROM (
    SELECT ISNULL(DF.[Data competencia], DF.[Data de Emissao]) AS Data,
           ISNULL(NULLIF(DF.[Codigo da Estrutura], ''), '(sem vendedor)') AS Vendedor,
           CAST(DF.Filial AS varchar(10)) + '|' + DF.[Codigo do tipo de documen] + '|' + CAST(DF.[Numero do documento] AS varchar(20)) AS Documento,
           CASE WHEN DCF.Devolucao = 'Devolução de Venda' THEN -1 ELSE 1 END AS Sinal,
           (I.[Preco com IPI] + I.[Subst Tributaria Valor]
            + CASE WHEN DF.[Valor Frete] > 0 THEN I.[Valor Frete] ELSE 0 END
            + CASE WHEN DF.[Valor despesas acessorias] > 0 THEN I.[Valor despesas acessorias] ELSE 0 END)
           * DF.[Valor Total] / (DF.[Valor Total] + DF.[Valor do desconto]) AS Receita,
           I.Quantidade * ISNULL(I.[Custo total], 0) AS Custo
    FROM [Itens dos Documentos Fisc] I WITH (NOLOCK)
    INNER JOIN [Documentos Fiscais] DF WITH (NOLOCK)
            ON DF.Filial = I.Filial AND DF.[Codigo do tipo de documen] = I.[Codigo do tipo de documen] AND DF.[Numero do documento] = I.[Numero do documento]
    INNER JOIN [Condicoes de Faturamento] CF WITH (NOLOCK)
            ON CF.Filial = DF.Filial AND CF.[Numero da Lista] = DF.[Numero da Lista] AND CF.Condicao = DF.Condicao
    INNER JOIN [Tipos de Documentos Fisca] TDF WITH (NOLOCK)
            ON TDF.Filial = I.Filial AND TDF.[Codigo do tipo de documen] = I.[Codigo do tipo de documen]
    LEFT JOIN [Descricao dos Codigos Fis] DCF WITH (NOLOCK)
            ON DCF.[Codigo da descricao ciof] = I.CIOF
    LEFT JOIN [Locais de Pagamento] LP WITH (NOLOCK)
            ON LP.[Codigo do Local] = DF.[Codigo do Local]
    WHERE CF.[Nao considera faturamento] = 0
      AND ISNULL(LP.[Descricao do Local], '') NOT LIKE 'PERMUTA%'
      AND DF.[Valor Total] > 0
      AND DF.[Valor Total] - DF.[Valor despesas acessorias] > 0
      AND DF.[Valor Total] - DF.[Valor Frete] > 0
      AND DF.[Data de Emissao] >= '20180101'
      AND TDF.Tipodocfiscal IN ('Cupom Fiscal', 'Nota Fiscal', 'Nota Fiscal Fatura', 'Conhecimento de Frete')
      AND ISNULL(DF.[Data competencia], DF.[Data de Emissao]) >= {inicio}
      AND ISNULL(DF.[Data competencia], DF.[Data de Emissao]) < {fim}
) v
LEFT JOIN [Estrutura de Vendas] ev WITH (NOLOCK) ON ev.[Codigo da Estrutura] = v.Vendedor
LEFT JOIN [Estrutura de Vendas] eg WITH (NOLOCK) ON eg.[Codigo da Estrutura] = ev.[Codigo da estrutura pai]
GROUP BY CAST(v.Data AS date), v.Vendedor
