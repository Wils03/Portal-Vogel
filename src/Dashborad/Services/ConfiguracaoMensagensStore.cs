using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Portal.Data;

namespace Portal.Services;

/// <summary>
/// Configuração do envio de mensagens editada pela tela Configurações (gravada no banco do portal, token criptografado).
/// Enquanto ninguém salvar pela tela, vale a seção "Mensagens" do appsettings.json / user-secrets.
/// </summary>
public class ConfiguracaoMensagensStore
{
    private readonly IConfiguration config;
    private readonly IDataProtector? protector;

    public ConfiguracaoMensagensStore(IConfiguration config, IDataProtectionProvider? dataProtection = null)
    {
        this.config = config;
        protector = dataProtection?.CreateProtector("Portal.TokenPagebot");
        Atual = DoAppSettings();
    }

    public OpcoesMensagens Atual { get; private set; }
    public DateTime? AtualizadoEm { get; private set; }
    public string? AtualizadoPor { get; private set; }

    private OpcoesMensagens DoAppSettings() => config.GetSection("Mensagens").Get<OpcoesMensagens>() ?? new();

    public async Task CarregarAsync(IDbContextFactory<ApplicationDbContext> fabrica, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var salvo = await db.ConfiguracoesMensagens.AsNoTracking().FirstOrDefaultAsync(ct);
        if (salvo is not null) Aplicar(salvo);
    }

    public async Task SalvarAsync(IDbContextFactory<ApplicationDbContext> fabrica, ConfiguracaoMensagens dados, string? novoToken, string? usuario, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var registro = await db.ConfiguracoesMensagens.FirstOrDefaultAsync(ct);
        if (registro is null)
        {
            registro = new ConfiguracaoMensagens();
            db.ConfiguracoesMensagens.Add(registro);
        }

        registro.Modo = dados.Modo;
        registro.ModoTeste = dados.ModoTeste;
        registro.NumeroTeste = MensagemService.SomenteDigitos(dados.NumeroTeste);
        registro.HorarioInicio = Math.Clamp(dados.HorarioInicio, 0, 23);
        registro.HorarioFim = Math.Clamp(dados.HorarioFim, 1, 24);
        registro.EnviarSabado = dados.EnviarSabado;
        registro.EnviarDomingo = dados.EnviarDomingo;
        registro.LimiteDiario = Math.Max(0, dados.LimiteDiario);
        registro.LimitePorClienteDia = Math.Max(0, dados.LimitePorClienteDia);
        registro.IntervaloSegundos = Math.Max(0, dados.IntervaloSegundos);
        registro.TentativasMaximas = Math.Clamp(dados.TentativasMaximas, 1, 10);
        registro.ForceSend = dados.ForceSend;
        registro.PagebotUrl = string.IsNullOrWhiteSpace(dados.PagebotUrl) ? new OpcoesPagebot().Url : dados.PagebotUrl.Trim();
        if (!string.IsNullOrWhiteSpace(novoToken))
            registro.TokenCriptografado = protector?.Protect(novoToken.Trim());
        registro.AtualizadoEm = DateTime.Now;
        registro.AtualizadoPor = usuario;
        await db.SaveChangesAsync(ct);
        Aplicar(registro);
    }

    public async Task RemoverTokenAsync(IDbContextFactory<ApplicationDbContext> fabrica, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var registro = await db.ConfiguracoesMensagens.FirstOrDefaultAsync(ct);
        if (registro is null) return;
        registro.TokenCriptografado = null;
        await db.SaveChangesAsync(ct);
        Aplicar(registro);
    }

    /// <summary>Token veio da tela (banco) ou do appsettings/user-secrets.</summary>
    public string OrigemToken { get; private set; } = "";

    private void Aplicar(ConfiguracaoMensagens r)
    {
        var baseConfig = DoAppSettings();
        string? token = null;
        if (r.TokenCriptografado is not null && protector is not null)
        {
            try { token = protector.Unprotect(r.TokenCriptografado); }
            catch { token = null; } // chaves perdidas: pede para informar o token de novo
        }

        OrigemToken = token is not null ? "tela" : string.IsNullOrWhiteSpace(baseConfig.Pagebot.Token) ? "" : "appsettings/user-secrets";
        Atual = new OpcoesMensagens
        {
            Modo = r.Modo,
            ModoTeste = r.ModoTeste,
            NumeroTeste = r.NumeroTeste,
            HorarioInicio = r.HorarioInicio,
            HorarioFim = r.HorarioFim,
            EnviarSabado = r.EnviarSabado,
            EnviarDomingo = r.EnviarDomingo,
            LimiteDiario = r.LimiteDiario,
            LimitePorClienteDia = r.LimitePorClienteDia,
            IntervaloSegundos = r.IntervaloSegundos,
            TentativasMaximas = r.TentativasMaximas,
            ForceSend = r.ForceSend,
            Pagebot = new OpcoesPagebot
            {
                Url = r.PagebotUrl,
                Token = token ?? baseConfig.Pagebot.Token,
                CabecalhoToken = baseConfig.Pagebot.CabecalhoToken,
                PrefixoToken = baseConfig.Pagebot.PrefixoToken,
                CorpoJson = baseConfig.Pagebot.CorpoJson
            }
        };
        AtualizadoEm = r.AtualizadoEm;
        AtualizadoPor = r.AtualizadoPor;
    }
}
