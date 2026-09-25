using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Portal.Data;

namespace Portal.Services;

/// <summary>Abre conexões nos bancos dos clientes e executa consultas somente leitura.</summary>
public partial class BancoClienteService(IDataProtectionProvider dataProtection, IConfiguration config)
{
    private readonly IDataProtector protector = dataProtection.CreateProtector("Portal.ConexaoCliente");
    private readonly int timeoutSegundos = config.GetValue("BancosClientes:TimeoutSegundos", 60);
    private readonly int maxLinhas = config.GetValue("BancosClientes:MaxLinhas", 5000);

    public string Criptografar(string conexao) => protector.Protect(conexao);

    public string Descriptografar(string conexaoCriptografada) => protector.Unprotect(conexaoCriptografada);

    public static string ExemploConexao(TipoBanco tipo) => tipo switch
    {
        TipoBanco.SqlServer => "Server=IP,1433;Database=BANCO;User Id=leitura;Password=***;TrustServerCertificate=True",
        TipoBanco.PostgreSql => "Host=IP;Port=5432;Database=banco;Username=leitura;Password=***",
        TipoBanco.MySql => "Server=IP;Port=3306;Database=banco;User ID=leitura;Password=***",
        TipoBanco.Firebird => "DataSource=IP;Port=3050;Database=C:\\Dados\\BANCO.FDB;User=SYSDBA;Password=***;Charset=WIN1252",
        _ => ""
    };

    private static DbConnection CriarConexao(TipoBanco tipo, string conexao) => tipo switch
    {
        TipoBanco.SqlServer => new SqlConnection(conexao),
        TipoBanco.PostgreSql => new NpgsqlConnection(conexao),
        TipoBanco.MySql => new MySqlConnection(conexao),
        TipoBanco.Firebird => new FbConnection(conexao),
        _ => throw new NotSupportedException($"Banco {tipo} não suportado.")
    };

    public async Task<string> TestarAsync(TipoBanco tipo, string conexao, CancellationToken ct = default)
    {
        await using var cn = CriarConexao(tipo, conexao);
        await cn.OpenAsync(ct);
        return $"servidor versão {cn.ServerVersion}.";
    }

    public virtual Task<DataTable> ConsultarAsync(Cliente cliente, string sql, CancellationToken ct = default, int? limiteLinhas = null)
    {
        if (string.IsNullOrWhiteSpace(cliente.ConexaoCriptografada))
            throw new InvalidOperationException($"Cliente {cliente.Nome} não tem conexão configurada.");
        return ConsultarAsync(cliente.TipoBanco, Descriptografar(cliente.ConexaoCriptografada), sql, ct, limiteLinhas);
    }

    /// <summary>
    /// ÚNICA gravação do portal no servidor do cliente: registra um pedido de "Sincronizar agora" na tabela LV_Pedido
    /// da BASELV (banco da Vogel; nunca na BASE do ERP). SQL fixo, parametrizado; o login do portal só tem INSERT nessa tabela.
    /// </summary>
    public virtual async Task RegistrarPedidoSincronizacaoAsync(Cliente cliente, string? usuario, CancellationToken ct = default)
    {
        if (cliente.TipoBanco != TipoBanco.SqlServer || string.IsNullOrWhiteSpace(cliente.ConexaoCriptografada))
            throw new InvalidOperationException("Sincronizar agora só funciona para clientes SQL Server com conexão configurada.");
        await using var cn = new SqlConnection(Descriptografar(cliente.ConexaoCriptografada));
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("INSERT INTO BASELV.dbo.LV_Pedido (PedidoPor) VALUES (@usuario)", cn) { CommandTimeout = timeoutSegundos };
        cmd.Parameters.AddWithValue("@usuario", (object?)usuario ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Executa um SELECT dentro de uma transação que é sempre desfeita.
    /// Proteção extra: recomenda-se usar um usuário de banco somente leitura para cada cliente.
    /// </summary>
    public async Task<DataTable> ConsultarAsync(TipoBanco tipo, string conexao, string sql, CancellationToken ct = default, int? limiteLinhas = null)
    {
        var limite = limiteLinhas ?? maxLinhas;
        ValidarSomenteLeitura(sql);

        await using var cn = CriarConexao(tipo, conexao);
        await cn.OpenAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql.Trim().TrimEnd(';');
        cmd.CommandTimeout = timeoutSegundos;

        var tabela = new DataTable();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            for (var i = 0; i < reader.FieldCount; i++)
                tabela.Columns.Add(NomeColunaUnico(tabela, reader.GetName(i)), typeof(object));

            var valores = new object[reader.FieldCount];
            while (await reader.ReadAsync(ct) && tabela.Rows.Count < limite)
            {
                reader.GetValues(valores);
                tabela.Rows.Add(valores.Select(v => v is DBNull ? null : v).ToArray());
            }
        }

        await tx.RollbackAsync(ct);
        return tabela;
    }

    private static string NomeColunaUnico(DataTable tabela, string nome)
    {
        nome = string.IsNullOrWhiteSpace(nome) ? "Coluna" : nome;
        var candidato = nome;
        for (var i = 2; tabela.Columns.Contains(candidato); i++) candidato = $"{nome}_{i}";
        return candidato;
    }

    public static void ValidarSomenteLeitura(string sql)
    {
        var semComentarios = Comentarios().Replace(sql, " ").Trim().TrimEnd(';');
        if (!InicioSelect().IsMatch(semComentarios))
            throw new InvalidOperationException("Somente consultas SELECT/WITH são permitidas.");
        if (semComentarios.Contains(';'))
            throw new InvalidOperationException("Use apenas um comando por consulta (sem ';' no meio).");
        if (ComandosEscrita().IsMatch(semComentarios))
            throw new InvalidOperationException("A consulta contém comandos de alteração, que não são permitidos.");
    }

    [GeneratedRegex(@"--[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comentarios();

    [GeneratedRegex(@"^\s*(select|with)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InicioSelect();

    [GeneratedRegex(@"\b(insert|update|delete|merge|drop|alter|create|truncate|exec|execute|grant|revoke)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ComandosEscrita();
}
