using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Portal.Data;

namespace Portal.Services;

/// <summary>
/// Gravação da DRE e da Precificação no portal, a mesma para o botão "Atualizar do banco", a planilha e a importação automática.
/// </summary>
public static class ImportacaoDados
{
    // ---------------------------------------------------------------- DRE

    /// <summary>Linhas (competência, grupo, conta, valor) de uma consulta: colunas pelo nome, sem acento/maiúscula.</summary>
    public static IEnumerable<(string Competencia, string Grupo, string Conta, string Valor)> LinhasDre(DataTable tabela)
    {
        string Col(DataRow r, string nome)
        {
            foreach (DataColumn c in tabela.Columns)
                if (Planilhas.Normalizar(c.ColumnName) == nome)
                    return r[c] switch
                    {
                        DateTime dt => dt.ToString("dd/MM/yyyy"),
                        null => "",
                        var v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
                    };
            return "";
        }
        return tabela.Rows.Cast<DataRow>().Select(r => (Col(r, "competencia"), Col(r, "grupo"), Col(r, "conta"), Col(r, "valor")));
    }

    /// <summary>Converte as linhas em lançamentos; conta as que têm competência ou grupo inválido.</summary>
    public static List<LancamentoDre> Lancamentos(IEnumerable<(string Competencia, string Grupo, string Conta, string Valor)> origem, int clienteId, out int rejeitadas)
    {
        var novos = new List<LancamentoDre>();
        rejeitadas = 0;
        foreach (var (competencia, grupoTexto, conta, valor) in origem)
        {
            if (Planilhas.Mes(competencia) is not { } mes || !Dre.TentarGrupo(grupoTexto, out var grupo)) { rejeitadas++; continue; }
            novos.Add(new LancamentoDre
            {
                ClienteId = clienteId,
                Competencia = mes,
                Grupo = grupo,
                Conta = conta.Length > 150 ? conta[..150] : conta,
                Valor = Planilhas.Decimal(valor)
            });
        }
        return novos;
    }

    /// <summary>Troca os lançamentos: o ano inteiro (consulta do banco) ou só os meses que vieram (planilha).</summary>
    public static async Task GravarDreAsync(IDbContextFactory<ApplicationDbContext> fabrica, int clienteId, List<LancamentoDre> novos, int? anoInteiro, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        if (anoInteiro is int a)
        {
            var de = new DateOnly(a, 1, 1);
            var ate = new DateOnly(a, 12, 1);
            await db.LancamentosDre.Where(l => l.ClienteId == clienteId && l.Competencia >= de && l.Competencia <= ate).ExecuteDeleteAsync(ct);
        }
        else
        {
            var meses = novos.Select(n => n.Competencia).Distinct().ToList();
            await db.LancamentosDre.Where(l => l.ClienteId == clienteId && meses.Contains(l.Competencia)).ExecuteDeleteAsync(ct);
        }
        db.LancamentosDre.AddRange(novos);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- Comercial (vendedores)

    /// <summary>Vendas por dia e vendedor de uma consulta (colunas pelo nome, sem acento/maiúscula).</summary>
    public static List<VendaVendedor> VendasVendedores(DataTable tabela, int clienteId)
    {
        var colunas = tabela.Columns.Cast<DataColumn>().ToDictionary(c => Planilhas.Normalizar(c.ColumnName), c => c);
        object? V(DataRow r, string nome) => colunas.TryGetValue(nome, out var c) && r[c] is not DBNull ? r[c] : null;
        string? T(DataRow r, string nome, int max) => V(r, nome) is { } v && Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } t ? (t.Length > max ? t[..max] : t) : null;
        decimal D(DataRow r, string nome) => V(r, nome) is { } v ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;
        int I(DataRow r, string nome) => V(r, nome) is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;

        var lista = new List<VendaVendedor>();
        foreach (DataRow r in tabela.Rows)
        {
            if ((V(r, "data") ?? V(r, "competencia")) is not { } dia || T(r, "vendedor", 20) is not { } vendedor) continue;
            var data = Convert.ToDateTime(dia, CultureInfo.InvariantCulture);
            lista.Add(new VendaVendedor
            {
                ClienteId = clienteId,
                Data = DateOnly.FromDateTime(data),
                Competencia = new DateOnly(data.Year, data.Month, 1),
                Vendedor = vendedor,
                NomeVendedor = T(r, "nomevendedor", 100),
                Gerente = T(r, "gerente", 20),
                NomeGerente = T(r, "nomegerente", 100),
                VendaBruta = D(r, "vendabruta"),
                Devolucoes = D(r, "devolucoes"),
                Cmv = D(r, "cmv"),
                Documentos = I(r, "documentos"),
                Itens = I(r, "itens")
            });
        }
        return lista;
    }

    /// <summary>Troca as vendas por vendedor do ano inteiro. As metas não são tocadas.</summary>
    public static async Task GravarComercialAsync(IDbContextFactory<ApplicationDbContext> fabrica, int clienteId, List<VendaVendedor> novos, int ano, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var de = new DateOnly(ano, 1, 1);
        var ate = new DateOnly(ano, 12, 1);
        await db.VendasVendedores.Where(v => v.ClienteId == clienteId && v.Competencia >= de && v.Competencia <= ate).ExecuteDeleteAsync(ct);
        db.VendasVendedores.AddRange(novos);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Feriados de uma consulta: "Data" (móveis ou já calculados na BASELV) ou "DiaMes" dd/mm (fixos do ERP),
    /// que viram datas de <paramref name="anoInicial"/> a <paramref name="anoFinal"/>.
    /// </summary>
    public static List<FeriadoCliente> Feriados(DataTable tabela, int clienteId, int anoInicial, int anoFinal)
    {
        var colunas = tabela.Columns.Cast<DataColumn>().ToDictionary(c => Planilhas.Normalizar(c.ColumnName), c => c);
        object? V(DataRow r, string nome) => colunas.TryGetValue(nome, out var c) && r[c] is not DBNull ? r[c] : null;
        var datas = new Dictionary<DateOnly, string?>();
        foreach (DataRow r in tabela.Rows)
        {
            var descricao = Convert.ToString(V(r, "descricao"), CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } d ? (d.Length > 100 ? d[..100] : d) : null;
            if (V(r, "data") is { } data) datas.TryAdd(DateOnly.FromDateTime(Convert.ToDateTime(data, CultureInfo.InvariantCulture)), descricao);
            else if (Convert.ToString(V(r, "diames"), CultureInfo.InvariantCulture) is { } dm
                     && DateOnly.TryParseExact(dm.Trim() + "/2000", "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fixo))
                for (var ano = anoInicial; ano <= anoFinal; ano++)
                    if (fixo.Month != 2 || fixo.Day != 29 || DateTime.IsLeapYear(ano)) datas.TryAdd(new DateOnly(ano, fixo.Month, fixo.Day), descricao);
        }
        return datas.Select(x => new FeriadoCliente { ClienteId = clienteId, Data = x.Key, Descricao = x.Value }).ToList();
    }

    /// <summary>
    /// Importa o Comercial de um ano (vendas por dia e vendedor) e os feriados, a partir do SQL comercial da empresa.
    /// Lendo a BASELV, os feriados vêm de LV_Feriado; direto do ERP, das tabelas de feriados.
    /// </summary>
    public static async Task<int> ImportarComercialAsync(IDbContextFactory<ApplicationDbContext> fabrica, BancoClienteService bancos, Cliente cliente, string sqlComercial, int ano, CancellationToken ct = default)
    {
        var sql = ModelosSql.AplicarPeriodo(sqlComercial, new DateOnly(ano, 1, 1), new DateOnly(ano + 1, 1, 1));
        var vendas = VendasVendedores(await bancos.ConsultarAsync(cliente, sql, ct, limiteLinhas: 200_000), cliente.Id);
        await GravarComercialAsync(fabrica, cliente.Id, vendas, ano, ct);

        await ImportarCotasAsync(fabrica, bancos, cliente, sqlComercial, ano, ct);

        var sqlFeriados = ImportacaoBaseLv.LeBaseLv(sqlComercial) ? ModelosSql.FeriadosBaseLv : ModelosSql.FeriadosSqlServer;
        var feriados = Feriados(await bancos.ConsultarAsync(cliente, sqlFeriados, ct), cliente.Id, DateTime.Today.Year - 3, DateTime.Today.Year + 1);
        await using var db = await fabrica.CreateDbContextAsync(ct);
        await db.Feriados.Where(f => f.ClienteId == cliente.Id).ExecuteDeleteAsync(ct);
        db.Feriados.AddRange(feriados);
        await db.SaveChangesAsync(ct);
        return vendas.Count;
    }

    /// <summary>
    /// Metas (cotas) do ERP do ano: gravadas como metas "do ERP" (substituem a meta digitada no portal no mesmo vendedor e mês).
    /// Cotas que sumiram do ERP são apagadas; metas digitadas no portal sem cota no ERP ficam.
    /// </summary>
    public static async Task ImportarCotasAsync(IDbContextFactory<ApplicationDbContext> fabrica, BancoClienteService bancos, Cliente cliente, string sqlComercial, int ano, CancellationToken ct = default)
    {
        var sqlCotas = ModelosSql.AplicarPeriodo(ImportacaoBaseLv.LeBaseLv(sqlComercial) ? ModelosSql.CotasBaseLv : ModelosSql.CotasSqlServer,
            new DateOnly(ano, 1, 1), new DateOnly(ano + 1, 1, 1));
        var tabela = await bancos.ConsultarAsync(cliente, sqlCotas, ct, limiteLinhas: 50_000);
        var colunas = tabela.Columns.Cast<DataColumn>().ToDictionary(c => Planilhas.Normalizar(c.ColumnName), c => c);
        var cotas = new Dictionary<(DateOnly, string), decimal>();
        foreach (DataRow r in tabela.Rows)
        {
            if (r[colunas["competencia"]] is DBNull or null || r[colunas["vendedor"]] is DBNull or null) continue;
            var data = Convert.ToDateTime(r[colunas["competencia"]], CultureInfo.InvariantCulture);
            var vendedor = Convert.ToString(r[colunas["vendedor"]], CultureInfo.InvariantCulture)!.Trim();
            var chave = (new DateOnly(data.Year, data.Month, 1), vendedor.Length > 20 ? vendedor[..20] : vendedor);
            cotas[chave] = cotas.GetValueOrDefault(chave) + Convert.ToDecimal(r[colunas["meta"]], CultureInfo.InvariantCulture);
        }

        await using var db = await fabrica.CreateDbContextAsync(ct);
        var de = new DateOnly(ano, 1, 1);
        var ate = new DateOnly(ano, 12, 1);
        var existentes = await db.MetasVendedores.Where(m => m.ClienteId == cliente.Id && m.Competencia >= de && m.Competencia <= ate).ToListAsync(ct);
        foreach (var m in existentes.Where(m => m.DoErp && !cotas.ContainsKey((m.Competencia, m.Vendedor))))
            db.MetasVendedores.Remove(m);
        foreach (var ((mes, vendedor), valor) in cotas)
        {
            var meta = existentes.FirstOrDefault(m => m.Competencia == mes && m.Vendedor == vendedor);
            if (meta is null) db.MetasVendedores.Add(meta = new MetaVendedor { ClienteId = cliente.Id, Competencia = mes, Vendedor = vendedor });
            if (meta.DoErp && meta.Meta == valor) continue;
            meta.Meta = valor;
            meta.DoErp = true;
            meta.AtualizadoEm = DateTime.Now;
            meta.AtualizadoPor = "ERP (Cotas de Vendas)";
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- Precificação

    public static Produto NovoProduto(Func<string[], string> campo) => new()
    {
        Codigo = campo(["codigo", "cod", "sku", "codigodoproduto"]),
        Descricao = campo(["descricao", "produto", "nome"]),
        Custo = Planilhas.Decimal(campo(["custo", "custounitario", "precocusto", "custoatual"])),
        PrecoAtual = Planilhas.Decimal(campo(["preco", "precoatual", "precovenda", "venda", "preco1"])),
        QtdMes = Planilhas.Decimal(campo(["qtdmes", "quantidade", "qtd", "qtdvendida"])),
        ImpostosPct = campo(["impostospct", "impostos"]) is { Length: > 0 } imp ? Planilhas.Decimal(imp) : null,
        GrupoPreco = campo(["grupopreco", "grupo"]) is { Length: > 0 } g ? (g.Length > 80 ? g[..80] : g) : null,
        LucroDesejadoPct = campo(["lucrodesejadopct", "lucrodesejado"]) is { Length: > 0 } l ? Planilhas.Decimal(l) : null,
        LucroSobreCusto = campo(["lucrosobre", "lucrodesejadosobre"]) is { Length: > 0 } s ? Planilhas.Normalizar(s).StartsWith("custo") : null
    };

    /// <summary>Produtos de uma consulta: colunas pelo nome, sem acento/maiúscula.</summary>
    public static IEnumerable<Produto> Produtos(DataTable tabela)
    {
        var colunas = tabela.Columns.Cast<DataColumn>().ToDictionary(c => Planilhas.Normalizar(c.ColumnName), c => c);
        return tabela.Rows.Cast<DataRow>().Select(r => NovoProduto(nomes =>
        {
            foreach (var n in nomes)
                if (colunas.TryGetValue(n, out var col))
                    return Convert.ToString(r[col], CultureInfo.InvariantCulture)?.Trim() ?? "";
            return "";
        }));
    }

    /// <summary>Troca todos os produtos da empresa pelos novos (linhas sem código e sem descrição são ignoradas).</summary>
    public static async Task<List<Produto>> SubstituirProdutosAsync(IDbContextFactory<ApplicationDbContext> fabrica, int clienteId, IEnumerable<Produto> novos, CancellationToken ct = default)
    {
        var lista = novos.Where(p => !string.IsNullOrWhiteSpace(p.Codigo) || !string.IsNullOrWhiteSpace(p.Descricao)).ToList();
        foreach (var p in lista)
        {
            p.ClienteId = clienteId;
            p.Codigo = p.Codigo.Length > 60 ? p.Codigo[..60] : p.Codigo;
            p.Descricao = p.Descricao.Length > 200 ? p.Descricao[..200] : p.Descricao;
        }
        await using var db = await fabrica.CreateDbContextAsync(ct);
        await db.Produtos.Where(p => p.ClienteId == clienteId).ExecuteDeleteAsync(ct);
        db.Produtos.AddRange(lista);
        await db.SaveChangesAsync(ct);
        return lista;
    }
}

public sealed record ResultadoImportacao(string Cliente, bool Dre, bool Precificacao, string? Erro);

/// <summary>
/// Importação automática diária da BASELV para o portal: para cada empresa que lê a BASELV (modelos "Usar BASELV"),
/// traz a DRE (ano atual e anterior) e a Precificação quando a sincronização do servidor do cliente tiver dados mais novos
/// que os do portal. Sem conexão (servidor desligado, sem internet), não apaga nada: tenta de novo mais tarde.
/// </summary>
public class ImportacaoBaseLv(IDbContextFactory<ApplicationDbContext> fabrica, BancoClienteService bancos, IConfiguration config, ILogger<ImportacaoBaseLv> logger)
{
    /// <summary>Última tentativa por empresa (em memória): sem conexão, tenta no máximo uma vez por intervalo.</summary>
    private static readonly ConcurrentDictionary<int, DateTime> UltimaTentativa = new();

    public int Hora => config.GetValue("ImportacaoBaseLv:Hora", 6);
    public TimeSpan IntervaloTentativas => TimeSpan.FromMinutes(config.GetValue("ImportacaoBaseLv:IntervaloMinutos", 60));

    public static bool LeBaseLv(string? sql) => sql?.Contains("BASELV", StringComparison.OrdinalIgnoreCase) == true;

    public static bool UsaBaseLv(Cliente c) => LeBaseLv(c.SqlDre) || LeBaseLv(c.SqlPrecificacao) || LeBaseLv(c.SqlComercial);

    /// <summary>Chamado pelo agendador a cada minuto: a partir da hora configurada, importa o que tiver dado novo.</summary>
    public async Task<List<ResultadoImportacao>> ExecutarAgendadaAsync(DateTime agora, CancellationToken ct = default)
    {
        if (agora.Hour < Hora) return [];
        List<Cliente> clientes;
        await using (var db = await fabrica.CreateDbContextAsync(ct))
            clientes = await db.Clientes.AsNoTracking()
                .Where(c => c.Ativo && c.ConexaoCriptografada != null)
                .ToListAsync(ct);

        var resultados = new List<ResultadoImportacao>();
        foreach (var cliente in clientes.Where(UsaBaseLv))
        {
            if (UltimaTentativa.TryGetValue(cliente.Id, out var ultima) && agora - ultima < IntervaloTentativas) continue;
            UltimaTentativa[cliente.Id] = agora;
            var r = await ImportarAsync(cliente, agora, forcar: false, ct);
            if (r.Dre || r.Precificacao || r.Erro is not null) resultados.Add(r);
        }
        return resultados;
    }

    /// <summary>
    /// Importa a empresa. Sem <paramref name="forcar"/>, só o que a sincronização tiver mais novo que o portal.
    /// </summary>
    public async Task<ResultadoImportacao> ImportarAsync(Cliente cliente, DateTime agora, bool forcar, CancellationToken ct = default)
    {
        try
        {
            var sincronizacao = await LerSincronizacaoAsync(cliente, ct);
            DateTime? Fim(string item) => sincronizacao.GetValueOrDefault(item);
            bool Novo(string item, DateTime? noPortal) => forcar || (Fim(item) is DateTime fim && (noPortal is null || fim > noPortal));

            var dre = false;
            if (LeBaseLv(cliente.SqlDre) && Novo(DataDosDados.Dre, cliente.DreDadosDe))
            {
                foreach (var ano in new[] { agora.Year - 1, agora.Year })
                {
                    var sql = ModelosSql.AplicarPeriodo(cliente.SqlDre!, new DateOnly(ano, 1, 1), new DateOnly(ano + 1, 1, 1));
                    var lancamentos = ImportacaoDados.Lancamentos(ImportacaoDados.LinhasDre(await bancos.ConsultarAsync(cliente, sql, ct)), cliente.Id, out _);
                    await ImportacaoDados.GravarDreAsync(fabrica, cliente.Id, lancamentos, ano, ct);
                }
                await DataDosDados.RegistrarAsync(fabrica, cliente.Id, DataDosDados.Dre, agora, Fim(DataDosDados.Dre));
                dre = true;
            }

            var precificacao = false;
            if (LeBaseLv(cliente.SqlPrecificacao) && Novo(DataDosDados.Precificacao, cliente.PrecificacaoDadosDe))
            {
                var sql = ModelosSql.AplicarFilial(cliente.SqlPrecificacao!, cliente.FilialPrecificacao ?? 1);
                var produtos = ImportacaoDados.Produtos(await bancos.ConsultarAsync(cliente, sql, ct, limiteLinhas: 200_000));
                await ImportacaoDados.SubstituirProdutosAsync(fabrica, cliente.Id, produtos, ct);
                await DataDosDados.RegistrarAsync(fabrica, cliente.Id, DataDosDados.Precificacao, agora, Fim(DataDosDados.Precificacao));
                precificacao = true;
            }

            var comercial = false;
            if (LeBaseLv(cliente.SqlComercial) && Novo(DataDosDados.Comercial, cliente.ComercialDadosDe))
            {
                foreach (var ano in new[] { agora.Year - 1, agora.Year })
                    await ImportacaoDados.ImportarComercialAsync(fabrica, bancos, cliente, cliente.SqlComercial!, ano, ct);
                await DataDosDados.RegistrarAsync(fabrica, cliente.Id, DataDosDados.Comercial, agora, Fim(DataDosDados.Comercial));
                comercial = true;
            }

            if (dre || precificacao || comercial)
                logger.LogInformation("Importação da BASELV: {Cliente} (DRE: {Dre}, Precificação: {Precificacao}, Comercial: {Comercial})", cliente.Nome, dre, precificacao, comercial);
            return new(cliente.Nome, dre || comercial, precificacao, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sem conexão ou falha na consulta: os dados atuais do portal continuam valendo
            logger.LogWarning("Importação da BASELV falhou para {Cliente}: {Erro}", cliente.Nome, ex.Message);
            return new(cliente.Nome, false, false, ex.Message);
        }
    }

    /// <summary>Última sincronização de cada item (Dre, Precificacao) registrada na BASELV do cliente.</summary>
    private async Task<Dictionary<string, DateTime>> LerSincronizacaoAsync(Cliente cliente, CancellationToken ct)
    {
        var tabela = await bancos.ConsultarAsync(cliente, "SELECT Tabela, UltimoFim FROM BASELV.dbo.LV_Sincronizacao", ct);
        var resultado = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow r in tabela.Rows)
            if (r[0] is string item && r[1] is DateTime fim) resultado[item.Trim()] = fim;
        return resultado;
    }

    /// <summary>Só para testes: esquece as tentativas anteriores.</summary>
    public static void LimparTentativas() => UltimaTentativa.Clear();
}
