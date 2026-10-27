using System.Data;
using System.Globalization;
using Portal.Data;

namespace Portal.Services;

public sealed record SituacaoPedido(int Id, DateTime PedidoEm, string Status, DateTime? IniciadoEm, DateTime? ConcluidoEm, string? Erro)
{
    public bool EmAndamento => Status is PedidoSincronizacao.Pendente or PedidoSincronizacao.Executando;

    /// <summary>Pendente há mais de 20 min: o servidor do cliente não está atendendo pedidos (o botão volta a ficar livre).</summary>
    public bool SemResposta(DateTime agora) => Status == PedidoSincronizacao.Pendente && agora - PedidoEm > PedidoSincronizacao.SemResposta;
}

public sealed record ResultadoPedido(bool Registrado, string Mensagem, SituacaoPedido? Situacao);

/// <summary>
/// Botão "Sincronizar agora": registra um pedido em BASELV.dbo.LV_Pedido; o SincronizadorLV no servidor do cliente
/// confere a cada poucos minutos, roda e marca como concluído. Evita pedidos repetidos para não sobrecarregar o ERP.
/// </summary>
public class PedidoSincronizacao(BancoClienteService bancos)
{
    public const string Pendente = "Pendente";
    public const string Executando = "Executando";
    public const string Concluido = "Concluido";
    public const string Erro = "Erro";

    /// <summary>Intervalo mínimo entre dois pedidos da mesma empresa.</summary>
    public static readonly TimeSpan IntervaloMinimo = TimeSpan.FromMinutes(10);

    /// <summary>Pedido pendente há mais que isso: o sincronizador não está conferindo os pedidos (tarefa não agendada).</summary>
    public static readonly TimeSpan SemResposta = TimeSpan.FromMinutes(20);

    /// <summary>Executando há mais que isso: considera travado e aceita um pedido novo.</summary>
    public static readonly TimeSpan Travado = TimeSpan.FromMinutes(60);

    /// <summary>A empresa lê algo da BASELV (DRE, Precificação ou rotinas): o botão faz sentido.</summary>
    public static bool Disponivel(Cliente c) =>
        c.TipoBanco == TipoBanco.SqlServer && c.ConexaoCriptografada is not null
        && (ImportacaoBaseLv.UsaBaseLv(c) || c.RotinasPelaBaseLv);

    public async Task<SituacaoPedido?> UltimoAsync(Cliente cliente, CancellationToken ct = default)
    {
        var t = await bancos.ConsultarAsync(cliente,
            """
            SELECT TOP 1 Id, PedidoEm, Status, IniciadoEm, ConcluidoEm, Erro,
                   (SELECT MIN(s.UltimoFim) FROM BASELV.dbo.LV_Sincronizacao s WHERE s.UltimoInicio >= p.PedidoEm AND s.Erro IS NULL) AS SincronizadoEm
            FROM BASELV.dbo.LV_Pedido p ORDER BY Id DESC
            """, ct);
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        DateTime? Data(string c) => !t.Columns.Contains(c) || r[c] is DBNull or null ? null : Convert.ToDateTime(r[c], CultureInfo.InvariantCulture);
        var situacao = new SituacaoPedido(Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), Data("PedidoEm")!.Value,
            Convert.ToString(r["Status"], CultureInfo.InvariantCulture)!.Trim(), Data("IniciadoEm"), Data("ConcluidoEm"),
            r["Erro"] is string e && e.Length > 0 ? e : null);
        return Atendido(situacao, Data("SincronizadoEm"));
    }

    /// <summary>
    /// Pedido ainda pendente, mas o servidor já sincronizou depois dele (rodado à mão ou pela tarefa diária):
    /// os dados novos já estão na BASELV, então conta como concluído.
    /// </summary>
    public static SituacaoPedido Atendido(SituacaoPedido p, DateTime? sincronizadoEm) =>
        p.Status == Pendente && sincronizadoEm is DateTime fim ? p with { Status = Concluido, ConcluidoEm = fim } : p;

    public async Task<ResultadoPedido> PedirAsync(Cliente cliente, string? usuario, DateTime agora, CancellationToken ct = default)
    {
        if (!Disponivel(cliente))
            return new(false, "Esta empresa não usa a BASELV (DRE, Precificação e rotinas vêm direto do banco do ERP).", null);

        var ultimo = await UltimoAsync(cliente, ct);
        if (ultimo is { Status: Pendente })
            return new(false, $"Já existe um pedido de {ultimo.PedidoEm:HH:mm} aguardando o servidor do cliente.", ultimo);
        if (ultimo is { Status: Executando } exec && agora - (exec.IniciadoEm ?? exec.PedidoEm) < Travado)
            return new(false, "O servidor do cliente já está sincronizando.", ultimo);
        if (ultimo is not null && !ultimo.EmAndamento && agora - ultimo.PedidoEm < IntervaloMinimo)
        {
            var falta = Math.Max(1, (int)Math.Ceiling((IntervaloMinimo - (agora - ultimo.PedidoEm)).TotalMinutes));
            return new(false, $"A última sincronização foi pedida às {ultimo.PedidoEm:HH:mm}. Aguarde {falta} min para pedir de novo.", ultimo);
        }

        await bancos.RegistrarPedidoSincronizacaoAsync(cliente, usuario, ct);
        return new(true, "Pedido enviado. O servidor do cliente confere a cada 5 minutos; os dados aparecem aqui quando terminar.", await UltimoAsync(cliente, ct));
    }

    /// <summary>Texto da situação para a tela.</summary>
    public static string Descrever(SituacaoPedido p, DateTime agora) => p.Status switch
    {
        Pendente when agora - p.PedidoEm > SemResposta =>
            $"Pedido de {p.PedidoEm:HH:mm} sem resposta do servidor do cliente. Confira se a tarefa \"Vogel SincronizadorLV Pedidos\" está agendada e se o servidor está ligado.",
        Pendente => $"Pedido de {p.PedidoEm:HH:mm} aguardando o servidor do cliente (confere a cada 5 minutos)…",
        Executando => $"Sincronizando no servidor do cliente desde {(p.IniciadoEm ?? p.PedidoEm):HH:mm}…",
        Concluido => $"Última sincronização pedida às {p.PedidoEm:HH:mm}, concluída às {p.ConcluidoEm:HH:mm}.",
        Erro => $"A sincronização pedida às {p.PedidoEm:HH:mm} terminou com erro: {p.Erro}",
        _ => p.Status
    };
}
