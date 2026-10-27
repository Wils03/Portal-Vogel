using Microsoft.EntityFrameworkCore;
using Portal.Data;

namespace Portal.Services;

/// <summary>Lançamentos da DRE já com a classificação das despesas do cliente (fixa, variável, depreciação).</summary>
public static class DreDados
{
    public static Task<Dictionary<string, GrupoDre>> ClassificacoesAsync(ApplicationDbContext db, int clienteId) =>
        db.ClassificacoesDre.AsNoTracking().Where(c => c.ClienteId == clienteId).ToDictionaryAsync(c => c.Conta, c => c.Grupo);

    /// <summary>Competências de <paramref name="de"/> a <paramref name="ate"/> (primeiro dia do mês, inclusive).</summary>
    public static async Task<List<LancamentoDre>> CarregarAsync(ApplicationDbContext db, int clienteId, DateOnly de, DateOnly ate)
    {
        var lancamentos = await db.LancamentosDre.AsNoTracking()
            .Where(l => l.ClienteId == clienteId && l.Competencia >= de && l.Competencia <= ate).ToListAsync();
        return Dre.Classificar(lancamentos, await ClassificacoesAsync(db, clienteId));
    }
}
