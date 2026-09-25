namespace Portal.Services;

/// <summary>SQLs prontos para os sistemas dos clientes (somente leitura).</summary>
public static class ModelosSql
{
    /// <summary>
    /// DRE para o banco SQL Server do sistema (tabelas Documentos Fiscais, Itens dos Documentos Fisc,
    /// Titulos do Contas a Pagar, Centro de custo). Detalhes em docs/dre-sqlserver.md.
    /// </summary>
    public static string DreSqlServer { get; } = Ler("ModeloDreSqlServer.sql");

    /// <summary>
    /// Produtos para a precificação (Produtos + Produtos_dados + Precos lista 1 + grupo de formação de preço
    /// + quantidade vendida nos últimos 90 dias). Detalhes em docs/precificacao-sqlserver.md.
    /// </summary>
    public static string PrecificacaoSqlServer { get; } = Ler("ModeloPrecificacaoSqlServer.sql");

    /// <summary>
    /// DRE e Precificação lidas da BASELV (tabelas LV_Dre e LV_Precificacao), que o SincronizadorLV grava todo dia
    /// no servidor do cliente com os modelos acima. O portal não consulta as tabelas do ERP.
    /// </summary>
    public static string DreBaseLv { get; } = Ler("ModeloDreBaseLv.sql");

    public static string PrecificacaoBaseLv { get; } = Ler("ModeloPrecificacaoBaseLv.sql");

    /// <summary>Comercial: vendas por mês e vendedor direto no ERP (regras da tela de indicadores) e lidas da BASELV.</summary>
    public static string ComercialSqlServer { get; } = Ler("ModeloComercialSqlServer.sql");

    public static string ComercialBaseLv { get; } = Ler("ModeloComercialBaseLv.sql");

    /// <summary>Feriados do ERP: direto nas tabelas (fixos dd/mm + móveis) ou já em datas na BASELV.</summary>
    public static string FeriadosSqlServer { get; } = Ler("ModeloFeriadosSqlServer.sql");

    public static string FeriadosBaseLv { get; } = Ler("ModeloFeriadosBaseLv.sql");

    /// <summary>Metas (cotas) de venda do ERP: direto na tabela Cotas de Vendas ou já na BASELV.</summary>
    public static string CotasSqlServer { get; } = Ler("ModeloCotasSqlServer.sql");

    public static string CotasBaseLv { get; } = Ler("ModeloCotasBaseLv.sql");

    public static string AplicarFilial(string sql, int filial) => sql.Replace("{filial}", filial.ToString());

    /// <summary>Troca {inicio} e {fim} por datas literais 'yyyyMMdd' (fim exclusivo).</summary>
    public static string AplicarPeriodo(string sql, DateOnly inicio, DateOnly fimExclusivo) => sql
        .Replace("{inicio}", $"'{inicio:yyyyMMdd}'")
        .Replace("{fim}", $"'{fimExclusivo:yyyyMMdd}'");

    private static string Ler(string nome)
    {
        using var stream = typeof(ModelosSql).Assembly.GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException($"Modelo {nome} não encontrado.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
