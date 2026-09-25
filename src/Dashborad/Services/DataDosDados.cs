using Microsoft.EntityFrameworkCore;
using Portal.Data;

namespace Portal.Services;

/// <summary>
/// Registra no cadastro do cliente quando a DRE/Precificação foi importada e de quando são os dados,
/// para o Painel mostrar "dados de dd/mm hh:mm" e avisar quando estiverem desatualizados.
/// </summary>
public static class DataDosDados
{
    public const string Dre = "Dre";
    public const string Precificacao = "Precificacao";
    public const string Comercial = "Comercial";

    /// <summary>Dados mais velhos que isso aparecem como desatualizados no Painel (a sincronização é diária).</summary>
    public static readonly TimeSpan LimiteDesatualizado = TimeSpan.FromHours(36);

    /// <summary>
    /// De quando são os dados que o SQL trouxe: se leu a BASELV, a última sincronização registrada em LV_Sincronizacao;
    /// se consultou o ERP direto, agora. Nulo se não der para saber.
    /// </summary>
    public static async Task<DateTime?> ObterAsync(BancoClienteService bancos, Cliente cliente, string sqlUsado, string item)
    {
        if (!sqlUsado.Contains("BASELV", StringComparison.OrdinalIgnoreCase)) return DateTime.Now;
        try
        {
            var tabela = await bancos.ConsultarAsync(cliente, $"SELECT UltimoFim FROM BASELV.dbo.LV_Sincronizacao WHERE Tabela = '{item}'");
            return tabela.Rows.Count > 0 && tabela.Rows[0][0] is DateTime fim ? fim : null;
        }
        catch
        {
            return null; // sem a tabela de controle ou sem permissão: a importação vale, só a data fica desconhecida
        }
    }

    public static Task RegistrarAsync(IDbContextFactory<ApplicationDbContext> fabrica, int clienteId, string item, DateTime? dadosDe) =>
        RegistrarAsync(fabrica, clienteId, item, DateTime.Now, dadosDe);

    public static async Task RegistrarAsync(IDbContextFactory<ApplicationDbContext> fabrica, int clienteId, string item, DateTime agora, DateTime? dadosDe)
    {
        await using var db = await fabrica.CreateDbContextAsync();
        var clientes = db.Clientes.Where(c => c.Id == clienteId);
        if (item == Dre)
            await clientes.ExecuteUpdateAsync(u => u.SetProperty(c => c.DreAtualizadoEm, agora).SetProperty(c => c.DreDadosDe, dadosDe));
        else if (item == Comercial)
            await clientes.ExecuteUpdateAsync(u => u.SetProperty(c => c.ComercialAtualizadoEm, agora).SetProperty(c => c.ComercialDadosDe, dadosDe));
        else
            await clientes.ExecuteUpdateAsync(u => u.SetProperty(c => c.PrecificacaoAtualizadoEm, agora).SetProperty(c => c.PrecificacaoDadosDe, dadosDe));
    }

    /// <summary>A data que vale para o usuário: a dos dados, ou, se desconhecida, a da importação.</summary>
    public static DateTime? Referencia(DateTime? dadosDe, DateTime? atualizadoEm) => dadosDe ?? atualizadoEm;

    public static bool Desatualizado(DateTime? referencia, DateTime agora) => referencia is null || agora - referencia.Value > LimiteDesatualizado;
}
