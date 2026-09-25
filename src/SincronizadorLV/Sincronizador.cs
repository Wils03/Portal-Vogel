using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace SincronizadorLV;

/// <param name="PastaCsv">Se preenchida, grava também um CSV de cada resultado, no formato do "Importar CSV/XLSX" do portal.</param>
/// <param name="PastaRotinas">Pasta com os SQLs das rotinas do portal (rotina-&lt;Id&gt;.sql), exportados pelo portal.</param>
public sealed record Opcoes(string SqlDre, string SqlPrecificacao, int DreAnos, int[] Filiais, int TempoLimiteSegundos, string? PastaCsv = null, string? PastaRotinas = null, string? SqlComercial = null, string? SqlFeriados = null, string? SqlCotas = null);

/// <summary>
/// Roda na BASE os mesmos SQLs do portal (DRE e Precificação; só SELECT, numa transação desfeita no fim)
/// e grava o resultado na BASELV (LV_Dre, LV_Precificacao). Cada parte é gravada numa transação própria:
/// se falhar, a BASELV continua com o resultado anterior e o erro fica em LV_Sincronizacao.
/// </summary>
public sealed partial class Sincronizador(string origem, string destino, Opcoes opcoes, Action<string> log)
{
    public const string Dre = "Dre";
    public const string Precificacao = "Precificacao";
    public const string Rotinas = "Rotinas";
    public const string Comercial = "Comercial";

    /// <summary>Mesmo limite de linhas de amostra que o portal guarda no alerta.</summary>
    public const int LinhasAmostra = 50;

    public async Task<bool> ExecutarAsync(IReadOnlyCollection<string> itens, int? anoForcado, CancellationToken ct)
    {
        await ConferirBancosAsync(ct);
        var ok = true;
        if (itens.Contains(Dre)) ok &= await RegistrandoAsync(Dre, ct => SincronizarDreAsync(anoForcado, ct), ct);
        if (itens.Contains(Precificacao)) ok &= await RegistrandoAsync(Precificacao, SincronizarPrecificacaoAsync, ct);
        if (itens.Contains(Comercial) && opcoes.SqlComercial is not null) ok &= await RegistrandoAsync(Comercial, ct => SincronizarComercialAsync(anoForcado, ct), ct);
        if (itens.Contains(Rotinas)) ok &= await RegistrandoAsync(Rotinas, SincronizarRotinasAsync, ct);
        return ok;
    }

    // ---------------------------------------------------------------- Pedidos do portal ("Sincronizar agora")

    /// <summary>
    /// Pega o pedido pendente mais antigo de LV_Pedido (feito pelo botão do portal), roda todos os itens e marca como concluído
    /// (junto com outros pedidos pendentes feitos antes do início). Sem pedido, não faz nada e devolve nulo.
    /// </summary>
    public async Task<bool?> AtenderPedidoAsync(IReadOnlyCollection<string> itens, CancellationToken ct)
    {
        int id;
        DateTime inicio;
        await using (var cn = new SqlConnection(destino))
        {
            await cn.OpenAsync(ct);
            await using var existe = new SqlCommand("SELECT OBJECT_ID('LV_Pedido')", cn);
            if (await existe.ExecuteScalarAsync(ct) is null or DBNull) return null;
            const string pegar = """
                WITH p AS (SELECT TOP 1 * FROM LV_Pedido WHERE Status = 'Pendente' ORDER BY Id)
                UPDATE p SET Status = 'Executando', IniciadoEm = GETDATE()
                OUTPUT inserted.Id, inserted.IniciadoEm;
                """;
            await using var cmd = new SqlCommand(pegar, cn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            id = r.GetInt32(0);
            inicio = r.GetDateTime(1);
        }

        log($"Pedido {id} do portal: sincronizando agora");
        bool ok;
        string? erro = null;
        try { ok = await ExecutarAsync(itens, null, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { ok = false; erro = ex.Message; }
        if (!ok) erro ??= "Houve erro em algum item (veja LV_Sincronizacao).";

        await using (var cn = new SqlConnection(destino))
        {
            await cn.OpenAsync(CancellationToken.None);
            const string concluir = """
                UPDATE LV_Pedido SET Status = @s, ConcluidoEm = GETDATE(), Erro = @e,
                       IniciadoEm = ISNULL(IniciadoEm, @inicio)
                WHERE Id = @id OR (Status = 'Pendente' AND PedidoEm <= @inicio);
                """;
            await using var cmd = new SqlCommand(concluir, cn);
            cmd.Parameters.AddWithValue("@s", ok ? "Concluido" : "Erro");
            cmd.Parameters.AddWithValue("@e", (object?)(erro is null ? null : Truncar(erro, 1000)) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@inicio", inicio);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
        log($"Pedido {id}: {(ok ? "concluído" : "com erro")}");
        return ok;
    }

    // ---------------------------------------------------------------- DRE

    /// <summary>Ano atual e anteriores (DreAnos), um ano por vez — igual ao botão "Atualizar do banco" da tela DRE.</summary>
    private async Task<int> SincronizarDreAsync(int? anoForcado, CancellationToken ct)
    {
        var anos = anoForcado is int a ? [a] : Enumerable.Range(0, opcoes.DreAnos).Select(i => DateTime.Today.Year - i).ToArray();
        var total = 0;
        foreach (var ano in anos)
        {
            var inicio = new DateTime(ano, 1, 1);
            var fim = inicio.AddYears(1);
            var sql = opcoes.SqlDre.Replace("{inicio}", $"'{inicio:yyyyMMdd}'").Replace("{fim}", $"'{fim:yyyyMMdd}'");
            var origemDados = await LerDaBaseAsync(sql, ["Competencia", "Grupo", "Conta", "Valor"], ct);

            // Soma linhas repetidas (mesmo mês, grupo e conta), como o portal faz ao importar
            var somas = new Dictionary<(DateTime, string, string), decimal>();
            foreach (DataRow r in origemDados.Rows)
            {
                if (r["Competencia"] is DBNull) continue;
                var comp = Convert.ToDateTime(r["Competencia"], CultureInfo.InvariantCulture);
                var chave = (new DateTime(comp.Year, comp.Month, 1), Convert.ToString(r["Grupo"])!.Trim(), Truncar(Convert.ToString(r["Conta"]) ?? "", 150));
                somas[chave] = somas.GetValueOrDefault(chave) + (r["Valor"] is DBNull ? 0 : Convert.ToDecimal(r["Valor"], CultureInfo.InvariantCulture));
            }

            var agora = DateTime.Now;
            var tabela = new DataTable();
            tabela.Columns.Add("Competencia", typeof(DateTime));
            tabela.Columns.Add("Grupo", typeof(string));
            tabela.Columns.Add("Conta", typeof(string));
            tabela.Columns.Add("Valor", typeof(decimal));
            tabela.Columns.Add("AtualizadoEm", typeof(DateTime));
            foreach (var ((comp, grupo, conta), valor) in somas) tabela.Rows.Add(comp, grupo, conta, valor, agora);

            await TrocarAsync("LV_Dre", "[Competencia] >= @a AND [Competencia] < @b",
                [new("@a", inicio), new("@b", fim)], tabela, ct);
            log($"  DRE {ano}: {tabela.Rows.Count:N0} linha(s)");
            GravarCsv($"dre-{ano}.csv", tabela, ["Competencia", "Grupo", "Conta", "Valor"]);
            total += tabela.Rows.Count;
        }
        return total;
    }

    // ---------------------------------------------------------------- Comercial (vendedores)

    /// <summary>Vendas por dia e vendedor, ano atual e anteriores (DreAnos), um ano por vez; e os feriados do ERP.</summary>
    private async Task<int> SincronizarComercialAsync(int? anoForcado, CancellationToken ct)
    {
        var anos = anoForcado is int a ? [a] : Enumerable.Range(0, opcoes.DreAnos).Select(i => DateTime.Today.Year - i).ToArray();
        var total = 0;
        foreach (var ano in anos)
        {
            var inicio = new DateTime(ano, 1, 1);
            var fim = inicio.AddYears(1);
            var sql = opcoes.SqlComercial!.Replace("{inicio}", $"'{inicio:yyyyMMdd}'").Replace("{fim}", $"'{fim:yyyyMMdd}'");
            var origemDados = await LerDaBaseAsync(sql, ["Data", "Vendedor", "VendaBruta", "Devolucoes", "Cmv", "Documentos", "Itens"], ct);

            var agora = DateTime.Now;
            var tabela = new DataTable();
            tabela.Columns.Add("Data", typeof(DateTime));
            foreach (var c in new[] { "Vendedor", "NomeVendedor", "Gerente", "NomeGerente" }) tabela.Columns.Add(c, typeof(string));
            foreach (var c in new[] { "VendaBruta", "Devolucoes", "Cmv" }) tabela.Columns.Add(c, typeof(decimal));
            tabela.Columns.Add("Documentos", typeof(int));
            tabela.Columns.Add("Itens", typeof(int));
            tabela.Columns.Add("AtualizadoEm", typeof(DateTime));
            foreach (DataRow r in origemDados.Rows)
            {
                if (r["Data"] is DBNull) continue;
                var dia = Convert.ToDateTime(r["Data"], CultureInfo.InvariantCulture).Date;
                tabela.Rows.Add(dia, Truncar(Convert.ToString(r["Vendedor"])?.Trim() ?? "", 20),
                    Texto(r, "NomeVendedor", 100), Texto(r, "Gerente", 20), Texto(r, "NomeGerente", 100),
                    Numero(r, "VendaBruta") is decimal vb ? vb : 0m, Numero(r, "Devolucoes") is decimal dv ? dv : 0m, Numero(r, "Cmv") is decimal cm ? cm : 0m,
                    Convert.ToInt32(r["Documentos"], CultureInfo.InvariantCulture), Convert.ToInt32(r["Itens"], CultureInfo.InvariantCulture), agora);
            }
            await TrocarAsync("LV_Comercial", "[Data] >= @a AND [Data] < @b", [new("@a", inicio), new("@b", fim)], tabela, ct);
            log($"  Comercial {ano}: {tabela.Rows.Count:N0} linha(s) (dia × vendedor)");
            total += tabela.Rows.Count;

            if (opcoes.SqlCotas is not null)
            {
                // Metas (cotas) do ERP, por vendedor e mês
                var sqlCotas = opcoes.SqlCotas.Replace("{inicio}", $"'{inicio:yyyyMMdd}'").Replace("{fim}", $"'{fim:yyyyMMdd}'");
                var cotas = await LerDaBaseAsync(sqlCotas, ["Competencia", "Vendedor", "Meta"], ct);
                var tabelaCotas = new DataTable();
                tabelaCotas.Columns.Add("Competencia", typeof(DateTime));
                tabelaCotas.Columns.Add("Vendedor", typeof(string));
                tabelaCotas.Columns.Add("Meta", typeof(decimal));
                tabelaCotas.Columns.Add("AtualizadoEm", typeof(DateTime));
                foreach (DataRow r in cotas.Rows)
                    if (r["Competencia"] is not DBNull && r["Vendedor"] is not DBNull)
                    {
                        var comp = Convert.ToDateTime(r["Competencia"], CultureInfo.InvariantCulture);
                        tabelaCotas.Rows.Add(new DateTime(comp.Year, comp.Month, 1), Truncar(Convert.ToString(r["Vendedor"])!.Trim(), 20),
                            Numero(r, "Meta") is decimal m ? m : 0m, agora);
                    }
                await TrocarAsync("LV_Cota", "[Competencia] >= @a AND [Competencia] < @b", [new("@a", inicio), new("@b", fim)], tabelaCotas, ct);
                log($"  Metas (cotas) {ano}: {tabelaCotas.Rows.Count:N0} vendedor(es) × mês");
            }
        }

        if (opcoes.SqlFeriados is not null)
        {
            // Fixos (dd/mm) viram datas de 3 anos atrás até o ano que vem (sempre a mesma faixa, mesmo com --ano); móveis já têm a data
            var feriados = await LerDaBaseAsync(opcoes.SqlFeriados, ["DiaMes", "Data", "Descricao"], ct);
            var datas = new Dictionary<DateTime, string?>();
            var primeiroAno = DateTime.Today.Year - 3;
            var ultimoAno = DateTime.Today.Year + 1;
            foreach (DataRow r in feriados.Rows)
            {
                var descricao = r["Descricao"] is DBNull ? null : Truncar(Convert.ToString(r["Descricao"])!.Trim(), 100);
                if (r["Data"] is not DBNull) datas.TryAdd(Convert.ToDateTime(r["Data"], CultureInfo.InvariantCulture).Date, descricao);
                else if (r["DiaMes"] is string dm && DateTime.TryParseExact(dm.Trim() + "/2000", "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    for (var ano = primeiroAno; ano <= ultimoAno; ano++)
                        if (d.Month != 2 || d.Day != 29 || DateTime.IsLeapYear(ano)) datas.TryAdd(new DateTime(ano, d.Month, d.Day), descricao);
            }
            var tabelaFeriados = new DataTable();
            tabelaFeriados.Columns.Add("Data", typeof(DateTime));
            tabelaFeriados.Columns.Add("Descricao", typeof(string));
            foreach (var (data, descricao) in datas) tabelaFeriados.Rows.Add(data, (object?)descricao ?? DBNull.Value);
            await TrocarAsync("LV_Feriado", "1 = 1", [], tabelaFeriados, ct);
            log($"  Feriados: {datas.Count} data(s)");
        }
        return total;
    }

    // ---------------------------------------------------------------- Precificação

    private async Task<int> SincronizarPrecificacaoAsync(CancellationToken ct)
    {
        var total = 0;
        foreach (var filial in opcoes.Filiais)
        {
            var sql = opcoes.SqlPrecificacao.Replace("{filial}", filial.ToString(CultureInfo.InvariantCulture));
            var origemDados = await LerDaBaseAsync(sql, ["Codigo", "Descricao", "Custo", "Preco"], ct);

            var agora = DateTime.Now;
            var tabela = new DataTable();
            tabela.Columns.Add("Filial", typeof(int));
            tabela.Columns.Add("Codigo", typeof(string));
            tabela.Columns.Add("Descricao", typeof(string));
            foreach (var c in new[] { "Custo", "Preco", "QtdMes", "ImpostosPct" }) tabela.Columns.Add(c, typeof(decimal));
            tabela.Columns.Add("GrupoPreco", typeof(string));
            tabela.Columns.Add("LucroDesejadoPct", typeof(decimal));
            tabela.Columns.Add("LucroSobre", typeof(string));
            tabela.Columns.Add("AtualizadoEm", typeof(DateTime));

            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var repetidos = 0;
            foreach (DataRow r in origemDados.Rows)
            {
                var codigo = Truncar(Convert.ToString(r["Codigo"])?.Trim() ?? "", 60);
                if (codigo.Length == 0) continue;
                if (!vistos.Add(codigo)) { repetidos++; continue; }
                tabela.Rows.Add(filial, codigo, Texto(r, "Descricao", 255),
                    Numero(r, "Custo"), Numero(r, "Preco"), Numero(r, "QtdMes"), Numero(r, "ImpostosPct"),
                    Texto(r, "GrupoPreco", 255), Numero(r, "LucroDesejadoPct"), Texto(r, "LucroSobre", 20), agora);
            }

            await TrocarAsync("LV_Precificacao", "[Filial] = @f", [new("@f", filial)], tabela, ct);
            GravarCsv($"precificacao-filial-{filial}.csv", tabela,
                ["Codigo", "Descricao", "Custo", "Preco", "QtdMes", "ImpostosPct", "GrupoPreco", "LucroDesejadoPct", "LucroSobre"]);
            log($"  Precificação filial {filial}: {tabela.Rows.Count:N0} produto(s)" + (repetidos > 0 ? $" ({repetidos} código(s) repetido(s) ignorado(s))" : ""));
            total += tabela.Rows.Count;
        }
        return total;
    }

    // ---------------------------------------------------------------- Rotinas (alertas)

    /// <summary>
    /// Roda cada rotina (consultas/rotinas/rotina-&lt;Id&gt;.sql) na BASE e grava o último resultado em LV_Rotina.
    /// Se o SQL de uma rotina falhar, grava o erro na linha dela (o portal transforma em alerta de falha) e segue com as outras.
    /// </summary>
    private async Task<int> SincronizarRotinasAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opcoes.PastaRotinas) || !Directory.Exists(opcoes.PastaRotinas))
        {
            log("  Rotinas: pasta de consultas das rotinas não encontrada; nada a fazer.");
            return 0;
        }
        var total = 0;
        foreach (var arquivo in Directory.GetFiles(opcoes.PastaRotinas, "rotina-*.sql").OrderBy(f => f))
        {
            if (ArquivoRotina().Match(Path.GetFileName(arquivo)) is not { Success: true } m) continue;
            var id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var sql = await File.ReadAllTextAsync(arquivo, ct);
            var nome = NomeRotina().Match(sql) is { Success: true } n ? Truncar(n.Groups[1].Value.Trim(), 150) : $"Rotina {id}";

            var linha = new DataTable();
            foreach (var (col, tipo) in new[] { ("RotinaId", typeof(int)), ("Nome", typeof(string)), ("ExecutadoEm", typeof(DateTime)),
                                                 ("Quantidade", typeof(decimal)), ("Linhas", typeof(int)), ("Amostra", typeof(string)), ("Erro", typeof(string)) })
                linha.Columns.Add(col, tipo);
            try
            {
                var resultado = await LerDaBaseAsync(sql, [], ct);
                linha.Rows.Add(id, nome, DateTime.Now, Quantidade(resultado), resultado.Rows.Count, (object?)Amostra(resultado) ?? DBNull.Value, DBNull.Value);
                log($"  Rotina {id} ({nome}): {Quantidade(resultado):0.##}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                linha.Rows.Add(id, nome, DateTime.Now, DBNull.Value, DBNull.Value, DBNull.Value, Truncar(ex.Message, 1000));
                log($"  Rotina {id} ({nome}): ERRO — {ex.Message}");
            }
            await TrocarAsync("LV_Rotina", "[RotinaId] = @id", [new("@id", id)], linha, ct);
            total++;
        }
        return total;
    }

    /// <summary>1 linha × 1 coluna numérica = o próprio número (ex.: COUNT); senão, a quantidade de linhas. Igual ao portal.</summary>
    public static decimal Quantidade(DataTable tabela)
    {
        if (tabela.Rows.Count == 1 && tabela.Columns.Count == 1
            && decimal.TryParse(Convert.ToString(tabela.Rows[0][0], CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var numero))
            return numero;
        return tabela.Rows.Count;
    }

    /// <summary>Até 50 linhas em JSON (coluna → texto pt-BR), no mesmo formato do detalhe do alerta no portal.</summary>
    public static string? Amostra(DataTable tabela)
    {
        if (tabela.Rows.Count <= 1 && tabela.Columns.Count <= 1) return null;
        var linhas = tabela.Rows.Cast<DataRow>().Take(LinhasAmostra)
            .Select(r => tabela.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => Convert.ToString(r[c], PtBr)));
        return JsonSerializer.Serialize(linhas);
    }

    [GeneratedRegex(@"^rotina-(\d+)\.sql$", RegexOptions.IgnoreCase)]
    private static partial Regex ArquivoRotina();

    [GeneratedRegex(@"^--\s*Rotina:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex NomeRotina();

    // ---------------------------------------------------------------- BASE (só leitura)

    /// <summary>Roda o SELECT na BASE dentro de uma transação que é sempre desfeita (como o portal faz).</summary>
    private async Task<DataTable> LerDaBaseAsync(string sql, string[] colunasObrigatorias, CancellationToken ct)
    {
        ValidarSomenteLeitura(sql);
        await using var cn = new SqlConnection(origem);
        await cn.OpenAsync(ct);
        await using var tr = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var tabela = new DataTable();
        try
        {
            await using var cmd = new SqlCommand(sql, cn, tr) { CommandTimeout = opcoes.TempoLimiteSegundos };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            tabela.Load(r);
        }
        finally
        {
            await tr.RollbackAsync(CancellationToken.None);
        }
        var faltando = colunasObrigatorias.Where(c => !tabela.Columns.Contains(c)).ToList();
        if (faltando.Count > 0)
            throw new InvalidOperationException($"O SQL não devolveu a(s) coluna(s): {string.Join(", ", faltando)}.");
        return tabela;
    }

    /// <summary>Mesma regra do portal: só um SELECT/WITH, sem comandos de alteração.</summary>
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

    // ---------------------------------------------------------------- BASELV

    /// <summary>
    /// Numa transação: apaga o trecho antigo (filtro) e grava o novo. CheckConstraints: com ele o bulk copy
    /// só precisa de INSERT (sem ele o SQL Server exige ALTER TABLE do login do sincronizador).
    /// </summary>
    private async Task TrocarAsync(string tabelaDestino, string filtro, SqlParameter[] parametros, DataTable linhas, CancellationToken ct)
    {
        await using var cn = new SqlConnection(destino);
        await cn.OpenAsync(ct);
        await using var tr = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await using (var del = new SqlCommand($"DELETE FROM [{tabelaDestino}] WHERE {filtro}", cn, tr) { CommandTimeout = opcoes.TempoLimiteSegundos })
        {
            del.Parameters.AddRange(parametros);
            await del.ExecuteNonQueryAsync(ct);
        }
        using (var copia = new SqlBulkCopy(cn, SqlBulkCopyOptions.CheckConstraints, tr) { DestinationTableName = tabelaDestino, BulkCopyTimeout = opcoes.TempoLimiteSegundos })
        {
            foreach (DataColumn c in linhas.Columns) copia.ColumnMappings.Add(c.ColumnName, c.ColumnName);
            await copia.WriteToServerAsync(linhas, ct);
        }
        await tr.CommitAsync(ct);
    }

    /// <summary>Recusa rodar se origem e destino forem o mesmo banco (nunca gravar na BASE do ERP).</summary>
    private async Task ConferirBancosAsync(CancellationToken ct)
    {
        var o = await IdentificarAsync(origem, ct);
        var d = await IdentificarAsync(destino, ct);
        log($"Origem: {o}  →  Destino: {d}");
        if (string.Equals(o, d, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Origem e destino são o mesmo banco. Nada foi gravado.");
        await using var cn = new SqlConnection(destino);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('LV_Dre') IS NULL OR OBJECT_ID('LV_Precificacao') IS NULL OR OBJECT_ID('LV_Sincronizacao') IS NULL THEN 0 ELSE 1 END", cn);
        if ((int)(await cmd.ExecuteScalarAsync(ct))! == 0)
            throw new InvalidOperationException("O destino não tem as tabelas LV_Dre, LV_Precificacao e LV_Sincronizacao (rode docs/criar-baselv-resultados.sql). Nada foi gravado.");
    }

    private static async Task<string> IdentificarAsync(string conexao, CancellationToken ct)
    {
        await using var cn = new SqlConnection(conexao);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT @@SERVERNAME + '/' + DB_NAME()", cn);
        return (string)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Executa uma parte e registra o resultado em LV_Sincronizacao (em caso de erro, mantém o último UltimoFim bom).</summary>
    private async Task<bool> RegistrandoAsync(string item, Func<CancellationToken, Task<int>> parte, CancellationToken ct)
    {
        var relogio = Stopwatch.StartNew();
        var inicio = DateTime.Now;
        try
        {
            var linhas = await parte(ct);
            await RegistrarAsync(item, inicio, DateTime.Now, linhas, null, ct);
            log($"{item}: ok, {linhas:N0} linha(s) em {relogio.Elapsed:mm\\:ss}");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log($"{item}: ERRO — {ex.Message}");
            try { await RegistrarAsync(item, inicio, null, null, ex.Message, CancellationToken.None); }
            catch (Exception ex2) { log($"  (não consegui registrar o erro em LV_Sincronizacao: {ex2.Message})"); }
            return false;
        }
    }

    private async Task RegistrarAsync(string item, DateTime inicio, DateTime? fim, int? linhas, string? erro, CancellationToken ct)
    {
        const string sql = """
            MERGE LV_Sincronizacao AS s
            USING (SELECT @t AS Tabela) AS n ON s.Tabela = n.Tabela
            WHEN MATCHED THEN UPDATE SET UltimoInicio = @i, UltimoFim = ISNULL(@f, s.UltimoFim), Linhas = ISNULL(@l, s.Linhas), Erro = @e
            WHEN NOT MATCHED THEN INSERT (Tabela, UltimoInicio, UltimoFim, Linhas, Erro) VALUES (@t, @i, @f, @l, @e);
            """;
        await using var cn = new SqlConnection(destino);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@t", item);
        cmd.Parameters.AddWithValue("@i", inicio);
        cmd.Parameters.AddWithValue("@f", (object?)fim ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@l", (object?)linhas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@e", erro is null ? DBNull.Value : Truncar(erro, 1000));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------------------------------------------------------------- CSV (para "Importar CSV/XLSX" do portal)

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>CSV com ';', números e datas em pt-BR (competência como MM/aaaa), UTF-8 — o formato que o portal lê.</summary>
    private void GravarCsv(string nomeArquivo, DataTable tabela, string[] colunas)
    {
        if (string.IsNullOrWhiteSpace(opcoes.PastaCsv)) return;
        Directory.CreateDirectory(opcoes.PastaCsv);
        var caminho = Path.Combine(opcoes.PastaCsv, nomeArquivo);
        using var saida = new StreamWriter(caminho, false, new System.Text.UTF8Encoding(true));
        saida.WriteLine(string.Join(';', colunas));
        foreach (DataRow r in tabela.Rows)
            saida.WriteLine(string.Join(';', colunas.Select(c => CampoCsv(r[c]))));
        log($"    CSV: {caminho}");
    }

    private static string CampoCsv(object valor)
    {
        var texto = valor switch
        {
            DBNull => "",
            DateTime d => d.ToString("MM/yyyy", PtBr),
            decimal n => n.ToString("0.####", PtBr),
            _ => Convert.ToString(valor, PtBr) ?? "",
        };
        return texto.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + texto.Replace("\"", "\"\"") + "\"" : texto;
    }

    // ---------------------------------------------------------------- utilidades

    private static string Truncar(string texto, int maximo) => texto.Length > maximo ? texto[..maximo] : texto;

    private static object Texto(DataRow r, string coluna, int maximo) =>
        !r.Table.Columns.Contains(coluna) || r[coluna] is DBNull ? DBNull.Value : Truncar(Convert.ToString(r[coluna])!.Trim(), maximo);

    private static object Numero(DataRow r, string coluna) =>
        !r.Table.Columns.Contains(coluna) || r[coluna] is DBNull ? DBNull.Value : Convert.ToDecimal(r[coluna], CultureInfo.InvariantCulture);
}
