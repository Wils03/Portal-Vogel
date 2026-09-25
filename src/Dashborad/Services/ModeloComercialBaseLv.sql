-- Indicadores comerciais (por dia e vendedor) lidos da BASELV (resultado pronto, gravado todo dia pelo SincronizadorLV).
-- Não consulta as tabelas do ERP: só a tabela LV_Comercial.
SELECT Data, Vendedor, NomeVendedor, Gerente, NomeGerente, VendaBruta, Devolucoes, Cmv, Documentos, Itens
FROM BASELV.dbo.LV_Comercial
WHERE Data >= {inicio} AND Data < {fim}
