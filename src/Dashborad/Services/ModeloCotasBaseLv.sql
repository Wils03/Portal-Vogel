-- Metas (cotas) do ERP lidas da BASELV (gravadas pelo SincronizadorLV a partir da tabela Cotas de Vendas).
SELECT Competencia, Vendedor, Meta FROM BASELV.dbo.LV_Cota
WHERE Competencia >= {inicio} AND Competencia < {fim}
