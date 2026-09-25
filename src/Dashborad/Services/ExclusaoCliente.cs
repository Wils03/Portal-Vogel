using Microsoft.EntityFrameworkCore;
using Portal.Data;

namespace Portal.Services;

public record ResumoExclusao(int Produtos, int Lancamentos, int Alertas, int Programacoes, int Usuarios, int Mensagens);

/// <summary>
/// Exclui um cliente do PORTAL (nunca mexe no banco do cliente): conexão, produtos, DRE, alertas,
/// programação das rotinas e o cadastro. Usuários da empresa são desativados. O histórico de mensagens fica como registro.
/// </summary>
public static class ExclusaoCliente
{
    public static async Task<ResumoExclusao> ContarAsync(IDbContextFactory<ApplicationDbContext> fabrica, int clienteId)
    {
        await using var db = await fabrica.CreateDbContextAsync();
        return new(
            await db.Produtos.CountAsync(p => p.ClienteId == clienteId),
            await db.LancamentosDre.CountAsync(l => l.ClienteId == clienteId),
            await db.Alertas.CountAsync(a => a.ClienteId == clienteId),
            await db.RotinasClientes.CountAsync(r => r.ClienteId == clienteId),
            await db.Users.CountAsync(u => u.ClienteId == clienteId),
            await db.Mensagens.CountAsync(m => m.ClienteId == clienteId));
    }

    public static async Task ExcluirAsync(IDbContextFactory<ApplicationDbContext> fabrica, int clienteId)
    {
        await using var db = await fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();

        await db.Produtos.Where(p => p.ClienteId == clienteId).ExecuteDeleteAsync();
        await db.LancamentosDre.Where(l => l.ClienteId == clienteId).ExecuteDeleteAsync();
        await db.VendasVendedores.Where(v => v.ClienteId == clienteId).ExecuteDeleteAsync();
        await db.MetasVendedores.Where(m => m.ClienteId == clienteId).ExecuteDeleteAsync();
        await db.Feriados.Where(f => f.ClienteId == clienteId).ExecuteDeleteAsync();
        await db.DiasUteis.Where(d => d.ClienteId == clienteId).ExecuteDeleteAsync();
        await db.RotinasClientes.Where(r => r.ClienteId == clienteId).ExecuteDeleteAsync();
        await db.Alertas.Where(a => a.ClienteId == clienteId).ExecuteDeleteAsync();

        // Usuários da empresa: desativados (não entram mais) e desconectados; o cadastro fica para o histórico
        foreach (var u in await db.Users.Where(u => u.ClienteId == clienteId).ToListAsync())
        {
            u.LockoutEnabled = true;
            u.LockoutEnd = DateTimeOffset.MaxValue;
            u.SecurityStamp = Guid.NewGuid().ToString();
        }
        await db.SaveChangesAsync();

        // Cadastro (com a conexão criptografada, SQLs e parâmetros)
        await db.Clientes.Where(c => c.Id == clienteId).ExecuteDeleteAsync();
        await transacao.CommitAsync();
    }
}
