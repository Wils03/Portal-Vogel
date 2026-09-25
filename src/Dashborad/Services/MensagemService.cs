using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Data;

namespace Portal.Services;

public class OpcoesMensagens
{
    /// <summary>"Manual" = só gera o link do WhatsApp para a pessoa enviar; "Pagebot" = envia pela API.</summary>
    public string Modo { get; set; } = "Manual";

    /// <summary>
    /// Modo teste: TODA mensagem do Pagebot vai para <see cref="NumeroTeste"/>, com "[TESTE → cliente]" no início.
    /// Ligado por padrão; sem NumeroTeste, nada é enviado.
    /// </summary>
    public bool ModoTeste { get; set; } = true;
    public string? NumeroTeste { get; set; }

    /// <summary>Envio automático só entre estas horas (início inclusivo, fim exclusivo), de segunda a sexta.</summary>
    public int HorarioInicio { get; set; } = 8;
    public int HorarioFim { get; set; } = 18;
    public bool EnviarSabado { get; set; }
    public bool EnviarDomingo { get; set; }

    /// <summary>Máximo de mensagens pelo Pagebot por dia, somando todos os clientes (protege o número).</summary>
    public int LimiteDiario { get; set; } = 20;

    /// <summary>Máximo de mensagens automáticas por cliente por dia. Vários alertas do mesmo cliente viram uma mensagem só.</summary>
    public int LimitePorClienteDia { get; set; } = 1;

    /// <summary>Espera mínima entre dois envios automáticos (nunca em rajada).</summary>
    public int IntervaloSegundos { get; set; } = 60;

    /// <summary>Tentativas de envio automático por alerta antes de desistir (evita repetir em caso de erro).</summary>
    public int TentativasMaximas { get; set; } = 3;

    /// <summary>
    /// forceSend do Pagebot. Ligado: envia mesmo que exista atendimento aberto com o número (a mensagem entra na conversa).
    /// Desligado: o Pagebot recusa (chat_03) e o portal tenta de novo depois.
    /// </summary>
    public bool ForceSend { get; set; }

    public OpcoesPagebot Pagebot { get; set; } = new();
}

/// <summary>
/// API do Pagebot: POST /core/v2/api/chats/send-text com o token do canal no cabeçalho "access-token"
/// (documentação: https://api.pagebot.com.br/swagger/index.html).
/// O corpo aceita as variáveis {telefone} e {mensagem}, já escapadas para JSON.
/// forceSend vem de <see cref="OpcoesMensagens.ForceSend"/> (tela Configurações).
/// O token fica fora do appsettings: dotnet user-secrets set "Mensagens:Pagebot:Token" "..."
/// </summary>
public class OpcoesPagebot
{
    public string Url { get; set; } = "https://api.pagebot.com.br/core/v2/api/chats/send-text";
    public string? Token { get; set; }
    public string CabecalhoToken { get; set; } = "access-token";
    public string PrefixoToken { get; set; } = "";
    public string CorpoJson { get; set; } = "{\"number\":\"{telefone}\",\"message\":\"{mensagem}\",\"forceSend\":false,\"isWhisper\":false,\"verifyContact\":false}";
}

public record ResultadoEnvio(bool Sucesso, string Canal, string? Retorno, string? LinkManual);

/// <summary>Uma mensagem automática planejada: para quem, o quê, e o motivo se estiver bloqueada.</summary>
public record PlanoEnvio(Cliente Cliente, List<Alerta> Alertas, string Destino, string Texto, string? Bloqueio);

public record SituacaoEnvio(bool PagebotConfigurado, bool ModoTeste, string? NumeroTeste, string Horario, bool DentroDoHorario,
    int EnviadosHoje, int LimiteDiario, int LimitePorClienteDia, int IntervaloSegundos);

public class MensagemService(IDbContextFactory<ApplicationDbContext> fabrica, HttpClient http, ConfiguracaoMensagensStore configuracao)
{
    public const string UsuarioAutomatico = "Vobô (automático)";
    private const string CanalPagebot = "Pagebot";

    /// <summary>Recusa do Pagebot quando já existe atendimento aberto com o número e forceSend = false.</summary>
    public const string ErroAtendimentoAberto = "chat_03";

    /// <summary>Depois de uma recusa por atendimento aberto, espera este tempo para tentar de novo.</summary>
    public static readonly TimeSpan EsperaAtendimentoAberto = TimeSpan.FromHours(2);

    /// <summary>Traduz as respostas conhecidas do Pagebot para o operador.</summary>
    public static string ExplicarRetorno(string? retorno) =>
        retorno is not null && retorno.Contains(ErroAtendimentoAberto)
            ? $"já existe um atendimento aberto com este número no Pagebot, e o envio não interrompe conversas em andamento. Encerre o atendimento no Pagebot (ou aguarde: o envio automático tenta de novo em {EsperaAtendimentoAberto.TotalHours:0} h)."
            : retorno ?? "";

    private OpcoesMensagens Opcoes => configuracao.Atual;

    /// <summary>Pagebot configurado (Modo = Pagebot + URL + token).</summary>
    public bool EnvioAutomatico => Opcoes.Modo.Equals("Pagebot", StringComparison.OrdinalIgnoreCase)
                                   && !string.IsNullOrWhiteSpace(Opcoes.Pagebot.Url)
                                   && !string.IsNullOrWhiteSpace(Opcoes.Pagebot.Token);

    public bool ModoTeste => Opcoes.ModoTeste;
    public string? NumeroTeste => SomenteDigitos(Opcoes.NumeroTeste) is { Length: >= 10 } n ? n : null;

    public string DescricaoHorario => $"{Opcoes.HorarioInicio:00}h às {Opcoes.HorarioFim:00}h, seg a {(Opcoes.EnviarSabado ? "sáb" : "sex")}";

    public bool DentroDoHorario(DateTime quando)
    {
        var o = Opcoes;
        if ((quando.DayOfWeek == DayOfWeek.Sunday && !o.EnviarDomingo) || (quando.DayOfWeek == DayOfWeek.Saturday && !o.EnviarSabado)) return false;
        return quando.Hour >= o.HorarioInicio && quando.Hour < o.HorarioFim;
    }

    /// <summary>Envia UMA mensagem de teste para o NumeroTeste (botão da tela Configurações). Conta no limite diário.</summary>
    public async Task<ResultadoEnvio> EnviarTesteAsync(string? usuario, bool forcar = false, CancellationToken ct = default)
    {
        if (!EnvioAutomatico) return new(false, CanalPagebot, "Pagebot não configurado: escolha o modo Pagebot e informe o token do canal.", null);
        if (NumeroTeste is not string numero) return new(false, CanalPagebot, "Informe o número de teste.", null);
        await using (var db = await fabrica.CreateDbContextAsync(ct))
        {
            if (await EnviosPagebotHojeAsync(db, ct) >= Opcoes.LimiteDiario)
                return new(false, CanalPagebot, $"Limite diário de {Opcoes.LimiteDiario} mensagens atingido.", null);
            var cliente = await db.Clientes.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
            if (cliente is null) return new(false, CanalPagebot, "Cadastre ao menos um cliente para registrar o teste no histórico.", null);
            var texto = $"[TESTE] Mensagem de teste do Portal Vogel em {DateTime.Now:dd/MM/yyyy HH:mm}. Se você recebeu, o envio pelo Pagebot está funcionando.";
            return await RegistrarPagebotAsync(cliente, numero, texto, [], usuario, ct, forcar);
        }
    }

    public async Task<SituacaoEnvio> SituacaoAsync(CancellationToken ct = default)
    {
        var o = Opcoes;
        await using var db = await fabrica.CreateDbContextAsync(ct);
        return new(EnvioAutomatico, o.ModoTeste, NumeroTeste, DescricaoHorario, DentroDoHorario(DateTime.Now),
            await EnviosPagebotHojeAsync(db, ct), o.LimiteDiario, o.LimitePorClienteDia, o.IntervaloSegundos);
    }

    private static Task<int> EnviosPagebotHojeAsync(ApplicationDbContext db, CancellationToken ct)
    {
        // Conta só o que o Pagebot aceitou (recusas não chegam ao WhatsApp).
        // Um envio agrupado grava uma linha por alerta com o mesmo horário: conta envios, não linhas.
        var hoje = DateTime.Today;
        return db.Mensagens.Where(m => m.Canal == CanalPagebot && m.Sucesso && m.DataHora >= hoje)
            .Select(m => new { m.ClienteId, m.DataHora }).Distinct().CountAsync(ct);
    }

    /// <summary>
    /// Monta (sem enviar nada) as mensagens automáticas que sairiam agora: um envio por cliente juntando os alertas novos,
    /// com o motivo quando algo impede o envio. É o que a tela "Simular" mostra e o que <see cref="DispararPendentesAsync"/> usa.
    /// </summary>
    public async Task<List<PlanoEnvio>> PlanejarAsync(CancellationToken ct = default)
    {
        var o = Opcoes;
        var agora = DateTime.Now;
        var hoje = DateTime.Today;
        await using var db = await fabrica.CreateDbContextAsync(ct);

        var pendentes = await db.Alertas.AsNoTracking()
            .Include(a => a.Cliente)
            .Include(a => a.Rotina)
            .Where(a => a.Status == StatusAlerta.Novo && a.Rotina!.EnviarAutomaticamente && a.Rotina.Ativa)
            // A empresa pode desligar a rotina ou o aviso automático só para ela
            .Where(a => !db.RotinasClientes.Any(r => r.RotinaId == a.RotinaId && r.ClienteId == a.ClienteId && (!r.Ativa || !r.EnviarAutomaticamente)))
            .Where(a => a.Cliente!.Ativo && a.Cliente.WhatsApp != null && a.Cliente.WhatsApp != "")
            .Where(a => !db.Mensagens.Any(m => m.AlertaId == a.Id && m.Sucesso))
            .Where(a => db.Mensagens.Count(m => m.AlertaId == a.Id && !m.Sucesso
                                                && (m.Retorno == null || !m.Retorno.Contains(ErroAtendimentoAberto))) < o.TentativasMaximas)
            .OrderBy(a => a.DataHora)
            .ToListAsync(ct);

        var enviadosHoje = await EnviosPagebotHojeAsync(db, ct);
        var porClienteHoje = await db.Mensagens
            .Where(m => m.Canal == CanalPagebot && m.Sucesso && m.Usuario == UsuarioAutomatico && m.DataHora >= hoje)
            .Select(m => new { m.ClienteId, m.DataHora }).Distinct()
            .GroupBy(m => m.ClienteId)
            .Select(g => new { g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

        // Clientes com atendimento aberto no Pagebot há pouco: espera antes de tentar de novo
        var desde = agora - EsperaAtendimentoAberto;
        var atendimentoAberto = await db.Mensagens
            .Where(m => m.Canal == CanalPagebot && !m.Sucesso && m.DataHora >= desde && m.Retorno != null && m.Retorno.Contains(ErroAtendimentoAberto))
            .GroupBy(m => m.ClienteId)
            .Select(g => new { g.Key, Ultima = g.Max(m => m.DataHora) })
            .ToDictionaryAsync(x => x.Key, x => x.Ultima, ct);

        var planos = new List<PlanoEnvio>();
        var liberados = 0;
        foreach (var grupo in pendentes.GroupBy(a => a.ClienteId))
        {
            var alertas = grupo.ToList();
            var cliente = alertas[0].Cliente!;
            var (destino, texto) = AplicarModoTeste(cliente, MontarTexto(cliente, alertas));

            string? bloqueio =
                !EnvioAutomatico ? "Pagebot não configurado (Modo = Pagebot e token do canal)" :
                o.ModoTeste && NumeroTeste is null ? "Modo teste ligado sem NumeroTeste: nada é enviado" :
                !DentroDoHorario(agora) ? $"Fora do horário comercial ({DescricaoHorario}) — sai quando abrir" :
                atendimentoAberto.TryGetValue(cliente.Id, out var recusa) ? $"Atendimento aberto no Pagebot com este número — nova tentativa às {recusa + EsperaAtendimentoAberto:HH:mm}" :
                porClienteHoje.GetValueOrDefault(cliente.Id) >= o.LimitePorClienteDia ? $"Cliente já recebeu {o.LimitePorClienteDia} mensagem(ns) hoje — sai amanhã" :
                enviadosHoje + liberados >= o.LimiteDiario ? $"Limite diário de {o.LimiteDiario} mensagens atingido — sai amanhã" :
                null;
            if (bloqueio is null) liberados++;
            planos.Add(new PlanoEnvio(cliente, alertas, destino, texto, bloqueio));
        }
        return planos;
    }

    /// <summary>
    /// Envia no máximo UMA mensagem automática por chamada e só se já passou o intervalo desde o último envio.
    /// O agendador chama a cada minuto, então as mensagens saem espaçadas, nunca em rajada.
    /// </summary>
    public async Task<int> DispararPendentesAsync(CancellationToken ct = default)
    {
        if (!EnvioAutomatico) return 0;

        await using (var db = await fabrica.CreateDbContextAsync(ct))
        {
            var ultimo = await db.Mensagens.Where(m => m.Canal == CanalPagebot).MaxAsync(m => (DateTime?)m.DataHora, ct);
            if (ultimo is DateTime u && (DateTime.Now - u).TotalSeconds < Opcoes.IntervaloSegundos) return 0;
        }

        var plano = (await PlanejarAsync(ct)).FirstOrDefault(p => p.Bloqueio is null);
        if (plano is null) return 0;

        var r = await RegistrarPagebotAsync(plano.Cliente, plano.Destino, plano.Texto, plano.Alertas.Select(a => a.Id).ToList(), UsuarioAutomatico, ct);
        return r.Sucesso ? 1 : 0;
    }

    /// <summary>Texto de um cliente: a mensagem do alerta, ou um resumo quando há vários alertas juntos.</summary>
    public static string MontarTexto(Cliente cliente, IReadOnlyList<Alerta> alertas)
    {
        if (alertas.Count == 1) return alertas[0].Mensagem;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var texto = new StringBuilder($"Olá {cliente.Contato ?? cliente.Nome}! Encontramos pendências no seu sistema em {DateTime.Now:dd/MM/yyyy}:\n");
        foreach (var a in alertas)
            texto.Append($"• {a.Rotina?.Nome}: {a.Quantidade.ToString("0.##", pt)}\n");
        texto.Append("Favor revisar. Qualquer dúvida estamos à disposição. Equipe Vogel");
        return texto.ToString();
    }

    private (string Destino, string Texto) AplicarModoTeste(Cliente cliente, string texto, string? telefone = null)
    {
        var real = SomenteDigitos(telefone ?? cliente.WhatsApp);
        return Opcoes.ModoTeste
            ? (NumeroTeste ?? "", $"[TESTE → {cliente.Nome} ({real})]\n{texto}")
            : (real, texto);
    }

    public static string SomenteDigitos(string? telefone) => new((telefone ?? "").Where(char.IsDigit).ToArray());

    public static string LinkWhatsApp(string? telefone, string texto) =>
        $"https://wa.me/{SomenteDigitos(telefone)}?text={Uri.EscapeDataString(texto)}";

    /// <summary>Envio pela tela de Alertas (uma pessoa clicou). Respeita o modo teste e o limite diário.</summary>
    public Task<ResultadoEnvio> EnviarAsync(Cliente cliente, string texto, int? alertaId, string? usuario, CancellationToken ct = default) =>
        EnviarAsync(cliente, cliente.WhatsApp, texto, alertaId, usuario, ct);

    /// <summary>
    /// Envio manual para um número escolhido (ex.: responsável pelos preços). Mesmas proteções:
    /// modo teste desvia para o número de teste e conta no limite diário.
    /// </summary>
    public Task<ResultadoEnvio> EnviarParaAsync(Cliente cliente, string? telefone, string texto, string? usuario, CancellationToken ct = default) =>
        EnviarAsync(cliente, telefone, texto, null, usuario, ct);

    private async Task<ResultadoEnvio> EnviarAsync(Cliente cliente, string? numero, string texto, int? alertaId, string? usuario, CancellationToken ct)
    {
        var telefone = SomenteDigitos(numero);
        if (telefone.Length < 10)
            return new(false, "-", "WhatsApp inválido: informe 55 + DDD + número.", null);

        if (!EnvioAutomatico)
        {
            // Modo manual: registra e devolve o link; quem clica confirma o envio no WhatsApp.
            await using var db = await fabrica.CreateDbContextAsync(ct);
            var link = new ResultadoEnvio(true, "WhatsApp (manual)", "Link gerado", LinkWhatsApp(telefone, texto));
            await GravarAsync(db, cliente, telefone, texto, alertaId is int a ? [a] : [], usuario, link, ct);
            return link;
        }

        if (Opcoes.ModoTeste && NumeroTeste is null)
            return new(false, CanalPagebot, "Modo teste ligado sem NumeroTeste configurado: nada foi enviado.", null);
        await using (var db = await fabrica.CreateDbContextAsync(ct))
        {
            if (await EnviosPagebotHojeAsync(db, ct) >= Opcoes.LimiteDiario)
                return new(false, CanalPagebot, $"Limite diário de {Opcoes.LimiteDiario} mensagens atingido: tente amanhã.", null);
        }

        var (destino, textoFinal) = AplicarModoTeste(cliente, texto, telefone);
        return await RegistrarPagebotAsync(cliente, destino, textoFinal, alertaId is int id ? [id] : [], usuario, ct);
    }

    private async Task<ResultadoEnvio> RegistrarPagebotAsync(Cliente cliente, string destino, string texto, List<int> alertaIds, string? usuario,
        CancellationToken ct, bool forcar = false)
    {
        var resultado = await EnviarPagebotAsync(destino, texto, ct, forcar);
        await using var db = await fabrica.CreateDbContextAsync(ct);
        await GravarAsync(db, cliente, destino, texto, alertaIds, usuario, resultado, ct);
        return resultado;
    }

    private static async Task GravarAsync(ApplicationDbContext db, Cliente cliente, string destino, string texto, List<int> alertaIds,
        string? usuario, ResultadoEnvio resultado, CancellationToken ct)
    {
        // Uma linha por alerta (a mensagem é a mesma), para cada alerta ter seu histórico e contagem de tentativas.
        var quando = DateTime.Now;
        foreach (var alertaId in alertaIds.DefaultIfEmpty())
        {
            db.Mensagens.Add(new MensagemEnviada
            {
                ClienteId = cliente.Id,
                AlertaId = alertaId == 0 ? null : alertaId,
                DataHora = quando,
                Destino = destino,
                Texto = texto,
                Canal = resultado.Canal,
                Sucesso = resultado.Sucesso,
                Retorno = resultado.Retorno is { Length: > 1000 } r ? r[..1000] : resultado.Retorno,
                Usuario = usuario
            });
        }
        if (resultado.Sucesso && alertaIds.Count > 0)
            await db.Alertas.Where(a => alertaIds.Contains(a.Id) && a.Status == StatusAlerta.Novo)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, StatusAlerta.Notificado), ct);
        await db.SaveChangesAsync(ct);
    }

    /// <param name="forcar">Só o botão de teste usa: envia mesmo com atendimento aberto (forceSend = true).</param>
    private async Task<ResultadoEnvio> EnviarPagebotAsync(string telefone, string texto, CancellationToken ct, bool forcar = false)
    {
        var opcoes = Opcoes.Pagebot;
        var corpo = opcoes.CorpoJson
            .Replace("{telefone}", EscaparJson(telefone))
            .Replace("{mensagem}", EscaparJson(texto));
        if (forcar || Opcoes.ForceSend)
            corpo = corpo.Replace("\"forceSend\":false", "\"forceSend\":true");

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, opcoes.Url)
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(opcoes.Token))
            requisicao.Headers.TryAddWithoutValidation(opcoes.CabecalhoToken, opcoes.PrefixoToken + opcoes.Token);

        try
        {
            using var resposta = await http.SendAsync(requisicao, ct);
            var retorno = await resposta.Content.ReadAsStringAsync(ct);
            return new(resposta.IsSuccessStatusCode, CanalPagebot, $"{(int)resposta.StatusCode} {retorno}", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, CanalPagebot, ex.Message, null);
        }
    }

    private static string EscaparJson(string valor) => JsonSerializer.Serialize(valor)[1..^1];
}
