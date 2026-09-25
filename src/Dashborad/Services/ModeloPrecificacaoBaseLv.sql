-- Precificação lida da BASELV (resultado pronto, gravado todo dia pelo SincronizadorLV no servidor do cliente).
-- Não consulta as tabelas do ERP: só a tabela LV_Precificacao.
SELECT Codigo, Descricao, Custo, Preco, QtdMes, ImpostosPct, GrupoPreco, LucroDesejadoPct, LucroSobre
FROM BASELV.dbo.LV_Precificacao
WHERE Filial = {filial}
