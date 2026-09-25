-- =====================================================================================
-- PASSO 6 — CONFERÊNCIA DEPOIS DA PRIMEIRA SINCRONIZAÇÃO (somente leitura)
-- Mostra só totais e contagens (nenhum nome de produto, cliente ou conta).
-- Use os totais do mês para comparar com a tela de indicadores do ERP. Guia: docs/implantacao/GUIA.md
-- =====================================================================================
USE BASELV;
SET NOCOUNT ON;

-- 1) Situação da sincronização. Esperado: Dre e Precificacao com UltimoFim de hoje e Erro vazio.
SELECT Tabela, UltimoInicio, UltimoFim, Linhas, Erro,
       CASE WHEN Erro IS NOT NULL THEN 'ERRO' WHEN UltimoFim < DATEADD(hour, -26, GETDATE()) THEN 'ATRASADA' ELSE 'ok' END AS Situacao
FROM dbo.LV_Sincronizacao ORDER BY Tabela;

-- 2) DRE por ano e grupo. Esperado: ano atual e anterior, com ReceitaBruta, Deducoes, Cmv e despesas.
SELECT YEAR(Competencia) AS Ano, Grupo, CAST(SUM(Valor) AS decimal(18,2)) AS Total, COUNT(*) AS Linhas
FROM dbo.LV_Dre GROUP BY YEAR(Competencia), Grupo ORDER BY 1 DESC, 2;

-- 3) Conferência com o ERP, últimos 3 meses. "VendaLiquida" deve bater com a "Venda" da tela de indicadores do ERP.
--    O CMV pode ser diferente do ERP (o portal usa custo de reposição médio; o ERP calcula o dele no programa).
SELECT CONVERT(char(7), Competencia, 120) AS Mes,
       CAST(SUM(CASE WHEN Grupo = 'ReceitaBruta' THEN Valor ELSE 0 END) AS decimal(18,2)) AS ReceitaBruta,
       CAST(SUM(CASE WHEN Grupo = 'Deducoes' AND Conta LIKE 'Devolu%' THEN Valor ELSE 0 END) AS decimal(18,2)) AS Devolucoes,
       CAST(SUM(CASE WHEN Grupo = 'ReceitaBruta' THEN Valor WHEN Grupo = 'Deducoes' AND Conta LIKE 'Devolu%' THEN -Valor ELSE 0 END) AS decimal(18,2)) AS VendaLiquida,
       CAST(SUM(CASE WHEN Grupo = 'Cmv' THEN Valor ELSE 0 END) AS decimal(18,2)) AS Cmv
FROM dbo.LV_Dre
WHERE Competencia >= DATEADD(month, -2, DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0))
GROUP BY CONVERT(char(7), Competencia, 120) ORDER BY 1 DESC;

-- 3b) Comercial (vendedores), últimos 3 meses. Esperado: "Vendas" e "Itens" iguais a "Número de Vendas" e "Itens Vendidos" da tela de indicadores do ERP;
--     VendaLiquida igual à "Venda". (Só totais: nenhum nome de vendedor.)
SELECT CONVERT(char(7), Data, 120) AS Mes, COUNT(DISTINCT Vendedor) AS Vendedores, COUNT(DISTINCT Data) AS DiasComVenda,
       CAST(SUM(VendaBruta - Devolucoes) AS decimal(18,2)) AS VendaLiquida, SUM(Documentos) AS Vendas, SUM(Itens) AS Itens
FROM dbo.LV_Comercial
WHERE Data >= DATEADD(month, -2, DATEADD(month, DATEDIFF(month, 0, GETDATE()), 0))
GROUP BY CONVERT(char(7), Data, 120) ORDER BY 1 DESC;
SELECT COUNT(*) AS Feriados, MIN(Data) AS De, MAX(Data) AS Ate FROM dbo.LV_Feriado;

-- 4) Rotinas/alertas. Esperado: uma linha por rotina, ExecutadoEm recente e Erro vazio.
--    (Quantidade = ocorrências que a rotina encontrou; o portal abre o alerta quando passar do limite.)
SELECT RotinaId, Nome, ExecutadoEm, Quantidade, Linhas, Erro FROM dbo.LV_Rotina ORDER BY RotinaId;

-- 5) Precificação por filial. Esperado: produtos > 0; "SemCusto" pequeno (produtos sem custo no cadastro do ERP).
SELECT Filial, COUNT(*) AS Produtos, SUM(CASE WHEN QtdMes > 0 THEN 1 ELSE 0 END) AS ComVenda90Dias,
       SUM(CASE WHEN ISNULL(Custo, 0) <= 0 THEN 1 ELSE 0 END) AS SemCusto, MAX(AtualizadoEm) AS AtualizadoEm
FROM dbo.LV_Precificacao GROUP BY Filial ORDER BY Filial;
