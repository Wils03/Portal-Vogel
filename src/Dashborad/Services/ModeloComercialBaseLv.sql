-- Indicadores comerciais (por dia e vendedor) lidos da BASELV (resultado pronto, gravado todo dia pelo SincronizadorLV).
-- Não consulta as tabelas do ERP: só a tabela LV_Comercial. SELECT * de propósito: a coluna Permuta só existe
-- depois do script de atualização (sem ela, a BASELV só tem vendas sem permuta).
SELECT * FROM BASELV.dbo.LV_Comercial
WHERE Data >= {inicio} AND Data < {fim}
