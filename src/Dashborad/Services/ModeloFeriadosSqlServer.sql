-- Feriados cadastrados no ERP (tabelas, não views). Somente leitura.
-- Fixos: "Dia mes" no formato dd/mm (vale para todo ano). Móveis: data completa.
-- Usados para os dias úteis do Comercial (segunda a sábado, menos feriados).
SELECT [Dia mes] AS DiaMes, CAST(NULL AS datetime) AS Data, Descricao FROM [Feriados fixos] WITH (NOLOCK)
UNION ALL
SELECT NULL, Data, Descricao FROM [Feriados moveis] WITH (NOLOCK)
