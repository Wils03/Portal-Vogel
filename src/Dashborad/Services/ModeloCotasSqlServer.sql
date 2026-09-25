-- Metas (cotas) de venda por vendedor e mês, cadastradas no ERP (tabela Cotas de Vendas; não usa views). Somente leitura.
-- A mesma cota que o relatório "Metas" do ERP mostra. Data = 1º dia do mês.
SELECT CAST(c.Data AS date) AS Competencia, c.[Codigo da Estrutura] AS Vendedor, SUM(c.Cota) AS Meta
FROM [Cotas de Vendas] c WITH (NOLOCK)
WHERE c.Data >= {inicio} AND c.Data < {fim}
GROUP BY CAST(c.Data AS date), c.[Codigo da Estrutura]
