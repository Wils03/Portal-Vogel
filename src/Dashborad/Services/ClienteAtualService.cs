using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Portal.Data;

namespace Portal.Services;

/// <summary>
/// Cliente escolhido no topo da tela, válido para todas as páginas (um por sessão do navegador).
/// Equipe Vogel escolhe (0 = todos); usuário de cliente fica sempre na própria empresa.
/// É só filtro de tela: quem pode ver o quê continua sendo decidido por <see cref="Acesso"/>.
/// </summary>
public class ClienteAtualService(AcessoService acessos, IDbContextFactory<ApplicationDbContext> fabrica, ProtectedLocalStorage armazenamento)
{
    private const string Chave = "portal.clienteAtual";
    private bool restaurado;

    /// <summary>0 = todos os clientes.</summary>
    public int ClienteId { get; private set; }
    public string? Nome { get; private set; }

    /// <summary>Avisa as telas abertas quando o cliente muda.</summary>
    public event Func<Task>? Mudou;

    /// <summary>Filtro para as consultas: nulo = todos.</summary>
    public int? Filtro => ClienteId > 0 ? ClienteId : null;

    /// <summary>Chamado pelo seletor do topo depois da primeira renderização (precisa do navegador).</summary>
    public async Task RestaurarAsync()
    {
        if (restaurado) return;
        restaurado = true;
        var acesso = await acessos.ObterAsync();
        if (acesso.EhCliente) { await AplicarAsync(acesso.ClienteId!.Value); return; }
        if (!acesso.EhVogel) return;
        try
        {
            var salvo = await armazenamento.GetAsync<int>(Chave);
            if (salvo.Success && salvo.Value > 0) await AplicarAsync(salvo.Value);
        }
        catch { /* navegador sem armazenamento ou chave antiga: começa em "todos" */ }
    }

    public async Task DefinirAsync(int clienteId)
    {
        var acesso = await acessos.ObterAsync();
        if (!acesso.EhVogel) return; // cliente não troca de empresa
        await AplicarAsync(clienteId);
        try { await armazenamento.SetAsync(Chave, ClienteId); } catch { }
    }

    private async Task AplicarAsync(int clienteId)
    {
        var acesso = await acessos.ObterAsync();
        string? nome = null;
        if (clienteId > 0 && acesso.PodeVerCliente(clienteId))
        {
            await using var db = await fabrica.CreateDbContextAsync();
            nome = await db.Clientes.Where(c => c.Id == clienteId && c.Ativo).Select(c => c.Nome).FirstOrDefaultAsync();
        }
        var novo = nome is null ? 0 : clienteId;
        if (novo == ClienteId && nome == Nome) return;
        ClienteId = novo;
        Nome = nome;
        if (Mudou is not null)
            foreach (var h in Mudou.GetInvocationList().Cast<Func<Task>>())
                await h();
    }
}
