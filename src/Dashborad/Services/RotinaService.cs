using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Data;

namespace Portal.Services;

public enum SituacaoRotina { Ok, AlertaNovo, AlertaAtualizado, Resolvido, Erro }

public record ResultadoRotina(string Cliente, decimal? Quantidade, SituacaoRotina Situacao, string? Erro);

/// <summary>
/// Executa as rotinas. Um problema gera UM alerta: enquanto continuar aparecendo, o alerta aberto é só atualizado;
/// quando a rotina não encontrar mais nada, o alerta é resolvido sozinho; se o problema voltar, nasce um alerta novo.
/// </summary>
public class RotinaService(IDbContextFactory<ApplicationDbContext> fabrica, BancoClienteService bancos, MensagemService mensageiro, ILogger<RotinaService> logger)
{
    /// <summary>Roda a rotina em todos os clientes ativos compatíveis e grava os alertas.</summary>
    public Task<List<ResultadoRotina>> ExecutarAsync(int rotinaId, CancellationToken ct = default) => ExecutarAsync(rotinaId, null, ct);

    /// <summary>Roda a rotina nos clientes indicados (nulo = todos). Empresas que desligaram a rotina ficam de fora.</summary>
    public async Task<List<ResultadoRotina>> ExecutarAsync(int rotinaId, IReadOnlyCollection<int>? somenteClientes, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var rotina = await db.Rotinas.FindAsync([rotinaId], ct)
            ?? throw new InvalidOperationException("Rotina não encontrada.");

        var consulta = db.Clientes
            .Where(c => c.Ativo && c.ConexaoCriptografada != null)
            .Where(c => rotina.TipoBanco == null || c.TipoBanco == rotina.TipoBanco);
        if (somenteClientes is not null) consulta = consulta.Where(c => somenteClientes.Contains(c.Id));
        var clientes = await consulta.OrderBy(c => c.Nome).ToListAsync(ct);
        var programacao = await db.RotinasClientes.Where(r => r.RotinaId == rotinaId).ToDictionaryAsync(r => r.ClienteId, ct);

        var agora = DateTime.Now;
        var resultados = new List<ResultadoRotina>();
        foreach (var cliente in clientes)
        {
            programacao.TryGetValue(cliente.Id, out var daEmpresa);
            if (daEmpresa is { Ativa: false }) continue; // a empresa desligou esta verificação
            resultados.Add(await ExecutarNoClienteAsync(db, rotina, cliente, ct));
            if (daEmpresa is null)
            {
                daEmpresa = new RotinaCliente { RotinaId = rotina.Id, ClienteId = cliente.Id };
                db.RotinasClientes.Add(daEmpresa);
            }
            daEmpresa.UltimaExecucao = agora; // controla o "já rodou hoje" de cada empresa
        }

        rotina.UltimaExecucao = agora;
        await db.SaveChangesAsync(ct);

        if (rotina.EnviarAutomaticamente && resultados.Any(r => r.Situacao == SituacaoRotina.AlertaNovo))
            await mensageiro.DispararPendentesAsync(ct);
        return resultados;
    }

    private async Task<ResultadoRotina> ExecutarNoClienteAsync(ApplicationDbContext db, Rotina rotina, Cliente cliente, CancellationToken ct)
    {
        var agora = DateTime.Now;
        var abertos = await db.Alertas
            .Where(a => a.ClienteId == cliente.Id && a.RotinaId == rotina.Id)
            .Where(a => a.Status == StatusAlerta.Novo || a.Status == StatusAlerta.Notificado || a.Status == StatusAlerta.Erro)
            .OrderByDescending(a => a.DataHora)
            .ToListAsync(ct);
        var problemaAberto = abertos.Where(a => a.Status != StatusAlerta.Erro).ToList();
        var falhasAbertas = abertos.Where(a => a.Status == StatusAlerta.Erro).ToList();

        void Resolver(IEnumerable<Alerta> alertas)
        {
            foreach (var a in alertas)
            {
                a.Status = StatusAlerta.Resolvido;
                a.ResolvidoEm = agora;
                a.AtualizadoEm = agora;
            }
        }

        try
        {
            var (quantidade, detalhe) = await ObterResultadoAsync(rotina, cliente, ct);
            Resolver(falhasAbertas); // a consulta voltou a funcionar

            if (quantidade <= rotina.Limite)
            {
                if (problemaAberto.Count == 0) return new(cliente.Nome, quantidade, SituacaoRotina.Ok, null);
                Resolver(problemaAberto); // a lista zerou: resolve sozinho; se voltar, nasce alerta novo (e novo envio)
                return new(cliente.Nome, quantidade, SituacaoRotina.Resolvido, null);
            }

            if (problemaAberto.Count > 0)
            {
                // Problema continua: atualiza o alerta aberto, sem mandar mensagem de novo
                var atual = problemaAberto[0];
                Resolver(problemaAberto.Skip(1)); // limpa duplicados antigos
                atual.Quantidade = quantidade;
                atual.DetalheJson = detalhe;
                atual.AtualizadoEm = agora;
                if (atual.Status == StatusAlerta.Novo)
                    atual.Mensagem = MontarMensagem(rotina, cliente, quantidade);
                return new(cliente.Nome, quantidade, SituacaoRotina.AlertaAtualizado, null);
            }

            db.Alertas.Add(new Alerta
            {
                ClienteId = cliente.Id,
                RotinaId = rotina.Id,
                DataHora = agora,
                Quantidade = quantidade,
                Severidade = rotina.Severidade,
                Status = StatusAlerta.Novo,
                Mensagem = MontarMensagem(rotina, cliente, quantidade),
                DetalheJson = detalhe
            });
            return new(cliente.Nome, quantidade, SituacaoRotina.AlertaNovo, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Rotina {Rotina} falhou no cliente {Cliente}", rotina.Nome, cliente.Nome);
            if (falhasAbertas.Count > 0)
            {
                falhasAbertas[0].Erro = ex.Message;
                falhasAbertas[0].AtualizadoEm = agora;
                Resolver(falhasAbertas.Skip(1));
            }
            else
            {
                db.Alertas.Add(new Alerta
                {
                    ClienteId = cliente.Id,
                    RotinaId = rotina.Id,
                    DataHora = agora,
                    Severidade = rotina.Severidade,
                    Status = StatusAlerta.Erro,
                    Mensagem = $"Falha ao executar \"{rotina.Nome}\"",
                    Erro = ex.Message
                });
            }
            return new(cliente.Nome, null, SituacaoRotina.Erro, ex.Message);
        }
    }

    /// <summary>Resultados da BASELV mais velhos que isso viram alerta de falha (a sincronização parou).</summary>
    public static readonly TimeSpan LimiteResultadoBaseLv = TimeSpan.FromHours(36);

    /// <summary>
    /// Quantidade e detalhe da rotina: rodando o SQL na BASE do ERP, ou, para empresas com "Rotinas pela BASELV",
    /// lendo o último resultado que o SincronizadorLV gravou em LV_Rotina. Falhas viram alerta de falha.
    /// </summary>
    private async Task<(decimal Quantidade, string? Detalhe)> ObterResultadoAsync(Rotina rotina, Cliente cliente, CancellationToken ct)
    {
        if (!cliente.RotinasPelaBaseLv)
        {
            var tabela = await bancos.ConsultarAsync(cliente, rotina.Sql, ct);
            return (ExtrairQuantidade(tabela), tabela.Rows.Count > 1 || tabela.Columns.Count > 1 ? Amostra(tabela) : null);
        }

        var lv = await bancos.ConsultarAsync(cliente, $"SELECT ExecutadoEm, Quantidade, Amostra, Erro FROM BASELV.dbo.LV_Rotina WHERE RotinaId = {rotina.Id}", ct);
        if (lv.Rows.Count == 0)
            throw new InvalidOperationException("A rotina ainda não foi sincronizada na BASELV do cliente (gere o pacote do SincronizadorLV de novo para incluir as rotinas).");
        var r = lv.Rows[0];
        if (r["Erro"] is string erro && erro.Length > 0)
            throw new InvalidOperationException($"O SQL da rotina falhou no servidor do cliente: {erro}");
        var executadoEm = Convert.ToDateTime(r["ExecutadoEm"], CultureInfo.InvariantCulture);
        if (DateTime.Now - executadoEm > LimiteResultadoBaseLv)
            throw new InvalidOperationException($"Resultado da BASELV desatualizado: a última sincronização da rotina foi em {executadoEm:dd/MM/yyyy HH:mm}.");
        var quantidade = r["Quantidade"] is DBNull ? 0m : Convert.ToDecimal(r["Quantidade"], CultureInfo.InvariantCulture);
        return (quantidade, r["Amostra"] is string amostra && amostra.Length > 0 ? amostra : null);
    }

    /// <summary>
    /// Arquivos rotina-&lt;Id&gt;.sql para o pacote do SincronizadorLV (rotinas compatíveis com SQL Server).
    /// O Id liga o resultado gravado na BASELV à rotina do portal.
    /// </summary>
    public static async Task<int> ExportarParaSincronizadorAsync(IDbContextFactory<ApplicationDbContext> fabrica, string pasta, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var rotinas = await db.Rotinas.AsNoTracking()
            .Where(r => r.TipoBanco == null || r.TipoBanco == TipoBanco.SqlServer)
            .OrderBy(r => r.Id).ToListAsync(ct);
        Directory.CreateDirectory(pasta);
        foreach (var r in rotinas)
        {
            var conteudo = $"-- Rotina: {r.Nome}\n-- Exportada do Portal Vogel em {DateTime.Now:dd/MM/yyyy HH:mm}. O resultado vai para BASELV.dbo.LV_Rotina (RotinaId = {r.Id}).\n{r.Sql.Trim()}\n";
            await File.WriteAllTextAsync(Path.Combine(pasta, $"rotina-{r.Id}.sql"), conteudo, ct);
        }
        return rotinas.Count;
    }

    /// <summary>
    /// Roda o que está na hora: para cada rotina liberada e cada empresa, usa o horário da empresa (ou o padrão)
    /// e roda uma vez por dia. Devolve quantas execuções (rotina × empresa) fez.
    /// </summary>
    public async Task<int> ExecutarAgendadasAsync(DateTime agora, CancellationToken ct = default)
    {
        List<Rotina> rotinas;
        List<Cliente> clientes;
        List<RotinaCliente> programacao;
        await using (var db = await fabrica.CreateDbContextAsync(ct))
        {
            rotinas = await db.Rotinas.AsNoTracking().Where(r => r.Ativa).ToListAsync(ct);
            clientes = await db.Clientes.AsNoTracking().Where(c => c.Ativo && c.ConexaoCriptografada != null).ToListAsync(ct);
            programacao = await db.RotinasClientes.AsNoTracking().ToListAsync(ct);
        }

        var total = 0;
        foreach (var rotina in rotinas)
        {
            var naHora = clientes
                .Where(c => rotina.TipoBanco == null || c.TipoBanco == rotina.TipoBanco)
                .Where(c =>
                {
                    var daEmpresa = programacao.FirstOrDefault(p => p.RotinaId == rotina.Id && p.ClienteId == c.Id);
                    var efetiva = ConfigRotina.Efetiva(rotina, daEmpresa);
                    return efetiva is { Ativa: true, Hora: int hora } && hora <= agora.Hour
                        && (daEmpresa?.UltimaExecucao is not DateTime ultima || ultima < agora.Date);
                })
                .Select(c => c.Id)
                .ToList();
            if (naHora.Count == 0) continue;
            logger.LogInformation("Executando rotina agendada {Rotina} em {Quantidade} empresa(s)", rotina.Nome, naHora.Count);
            await ExecutarAsync(rotina.Id, naHora, ct);
            total += naHora.Count;
        }
        return total;
    }

    /// <summary>Grava a programação da empresa. Não mexe no SQL nem no que a Vogel liberou.</summary>
    public async Task SalvarProgramacaoAsync(int rotinaId, int clienteId, bool ativa, bool envioAutomatico, int? hora, string? usuario, CancellationToken ct = default)
    {
        if (hora is < 0 or > 23) throw new InvalidOperationException("A hora deve estar entre 0 e 23.");
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var registro = await db.RotinasClientes.FirstOrDefaultAsync(r => r.RotinaId == rotinaId && r.ClienteId == clienteId, ct);
        if (registro is null)
        {
            registro = new RotinaCliente { RotinaId = rotinaId, ClienteId = clienteId };
            db.RotinasClientes.Add(registro);
        }
        registro.Ativa = ativa;
        registro.EnviarAutomaticamente = envioAutomatico;
        registro.HoraExecucao = hora;
        registro.AtualizadoEm = DateTime.Now;
        registro.AtualizadoPor = usuario;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>1 linha x 1 coluna numérica = o próprio número (ex.: COUNT). Caso contrário, conta as linhas.</summary>
    public static decimal ExtrairQuantidade(DataTable tabela)
    {
        if (tabela.Rows.Count == 1 && tabela.Columns.Count == 1)
        {
            var valor = tabela.Rows[0][0];
            if (valor is not null && decimal.TryParse(Convert.ToString(valor, CultureInfo.InvariantCulture),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var numero))
                return numero;
        }
        return tabela.Rows.Count;
    }

    public static string MontarMensagem(Rotina rotina, Cliente cliente, decimal quantidade)
    {
        var modelo = string.IsNullOrWhiteSpace(rotina.ModeloMensagem)
            ? "Olá {cliente}! A verificação \"{rotina}\" encontrou {quantidade} ocorrência(s). Favor revisar."
            : rotina.ModeloMensagem;
        return modelo
            .Replace("{cliente}", cliente.Contato ?? cliente.Nome)
            .Replace("{empresa}", cliente.Nome)
            .Replace("{quantidade}", quantidade.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")))
            .Replace("{rotina}", rotina.Nome)
            .Replace("{data}", DateTime.Now.ToString("dd/MM/yyyy"));
    }

    private static string Amostra(DataTable tabela)
    {
        var linhas = tabela.Rows.Cast<DataRow>().Take(50)
            .Select(r => tabela.Columns.Cast<DataColumn>()
                .ToDictionary(c => c.ColumnName, c => Convert.ToString(r[c], CultureInfo.GetCultureInfo("pt-BR"))));
        return JsonSerializer.Serialize(linhas);
    }
}

/// <summary>A cada minuto verifica se alguma rotina deve rodar na hora atual (de cada empresa) e ainda não rodou hoje.</summary>
public class AgendadorRotinas(IServiceScopeFactory scopes, ILogger<AgendadorRotinas> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await VerificarAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Erro no agendador de rotinas");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task VerificarAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        // Cada empresa no seu horário (o escolhido por ela ou o padrão da rotina), uma vez por dia
        await scope.ServiceProvider.GetRequiredService<RotinaService>().ExecutarAgendadasAsync(DateTime.Now, ct);

        // DRE e Precificação da BASELV das empresas (uma vez por dia, a partir da hora configurada; sem conexão, tenta mais tarde)
        await scope.ServiceProvider.GetRequiredService<ImportacaoBaseLv>().ExecutarAgendadaAsync(DateTime.Now, ct);

        // Alertas novos que ficaram esperando o horário comercial (ou o Pagebot voltar)
        var enviados = await scope.ServiceProvider.GetRequiredService<MensagemService>().DispararPendentesAsync(ct);
        if (enviados > 0) logger.LogInformation("{Quantidade} mensagem(ns) automática(s) enviada(s)", enviados);
    }
}
