using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portal.Data;

namespace Portal.Services;

/// <summary>Nomes de perfis para [Authorize(Roles = ...)].</summary>
public static class Perfis
{
    public const string Administrador = nameof(Perfil.VogelAdministrador);
    public const string Vogel = nameof(Perfil.VogelAdministrador) + "," + nameof(Perfil.VogelOperador);
    public const string GerenciamUsuarios = nameof(Perfil.VogelAdministrador) + "," + nameof(Perfil.ClienteGestor);
    /// <summary>Vogel e gestor do cliente: DRE e programação das rotinas.</summary>
    public const string Gestao = Vogel + "," + nameof(Perfil.ClienteGestor);
    public const string ClaimCliente = "cliente_id";

    public static string Texto(Perfil p) => p switch
    {
        Perfil.VogelAdministrador => "Vogel · Administrador",
        Perfil.VogelOperador => "Vogel · Operador",
        Perfil.ClienteGestor => "Cliente · Gestor",
        Perfil.ClienteUsuario => "Cliente · Usuário",
        _ => "Sem perfil"
    };

    public static string Descricao(Perfil p) => p switch
    {
        Perfil.VogelAdministrador => "Tudo: clientes e conexões, SQL das rotinas, configurações do Pagebot e usuários.",
        Perfil.VogelOperador => "Todos os clientes: alertas, envio de mensagens, rotinas (executar), precificação e DRE. Não mexe em conexões, SQL nem configurações.",
        Perfil.ClienteGestor => "Só a própria empresa: painel, alertas, precificação, DRE e mensagens; programa as rotinas (liga/desliga, horário, envio automático), salva parâmetros e cria usuários da empresa.",
        Perfil.ClienteUsuario => "Só a própria empresa: vê painel, alertas, precificação e mensagens (sem DRE e rotinas). Pode exportar.",
        _ => ""
    };

    public static bool EhCliente(Perfil p) => p is Perfil.ClienteGestor or Perfil.ClienteUsuario;
}

/// <summary>
/// Quem está usando a tela e o que pode ver. Lido do banco a cada tela (não só do cookie),
/// para que mudança de perfil/empresa ou desativação valha na hora.
/// </summary>
public record Acesso(string UsuarioId, string? Email, Perfil Perfil, int? ClienteId, string? ClienteNome)
{
    public static readonly Acesso Nenhum = new("", null, Perfil.Indefinido, null, null);

    public bool EhVogel => Perfil is Perfil.VogelAdministrador or Perfil.VogelOperador;
    public bool EhAdministrador => Perfil == Perfil.VogelAdministrador;
    public bool EhCliente => Perfis.EhCliente(Perfil) && ClienteId is not null;

    /// <summary>Consultas livres no banco do cliente (testar SQL, executar rotina): só a Vogel.</summary>
    public bool ConsultaBancoCliente => EhVogel;

    /// <summary>
    /// "Atualizar do banco" na DRE e na precificação, com o SQL que a Vogel cadastrou (o gestor não vê nem edita o SQL).
    /// Na versão online vira "sincronizar".
    /// </summary>
    public bool AtualizaDoBanco => GestorOuVogel;

    /// <summary>Liga/desliga, horário e envio automático das rotinas (de uma empresa). O SQL continua só com o administrador.</summary>
    public bool ProgramaRotinas => GestorOuVogel;

    public bool VeDre => GestorOuVogel;
    public bool VeComercial => GestorOuVogel;

    /// <summary>Marca produtos na precificação e avisa um responsável pelo WhatsApp.</summary>
    public bool EnviaAvisoPrecos => GestorOuVogel;

    private bool GestorOuVogel => EhVogel || (Perfil == Perfil.ClienteGestor && EhCliente);
    public bool EditaSql => EhAdministrador;
    public bool EnviaMensagens => EhVogel;
    /// <summary>Grava dados no portal: importar planilha, salvar parâmetros, marcar alerta resolvido.</summary>
    public bool Altera => EhVogel || (Perfil == Perfil.ClienteGestor && EhCliente);
    public bool GerenciaUsuarios => EhAdministrador || (Perfil == Perfil.ClienteGestor && ClienteId is not null);

    public bool PodeVerCliente(int clienteId) => EhVogel || (EhCliente && ClienteId == clienteId);

    /// <summary>Filtra qualquer consulta que tenha ClienteId. Perfil sem empresa não vê nada.</summary>
    public IQueryable<Cliente> Filtrar(IQueryable<Cliente> q) =>
        EhVogel ? q : EhCliente ? q.Where(c => c.Id == ClienteId) : q.Where(c => false);

    public IQueryable<Alerta> Filtrar(IQueryable<Alerta> q) =>
        EhVogel ? q : EhCliente ? q.Where(a => a.ClienteId == ClienteId) : q.Where(a => false);

    public IQueryable<MensagemEnviada> Filtrar(IQueryable<MensagemEnviada> q) =>
        EhVogel ? q : EhCliente ? q.Where(m => m.ClienteId == ClienteId) : q.Where(m => false);

    /// <summary>Perfis que este usuário pode dar a outros.</summary>
    public IReadOnlyList<Perfil> PerfisQuePodeAtribuir =>
        EhAdministrador ? [Perfil.VogelAdministrador, Perfil.VogelOperador, Perfil.ClienteGestor, Perfil.ClienteUsuario]
        : GerenciaUsuarios ? [Perfil.ClienteGestor, Perfil.ClienteUsuario]
        : [];

    /// <summary>Pode editar/desativar este usuário?</summary>
    public bool PodeGerenciar(ApplicationUser u) =>
        u.Id != UsuarioId && (EhAdministrador || (GerenciaUsuarios && Perfis.EhCliente(u.Perfil) && u.ClienteId == ClienteId));
}

public class AcessoService(AuthenticationStateProvider autenticacao, IDbContextFactory<ApplicationDbContext> fabrica)
{
    private Acesso? atual;

    public async Task<Acesso> ObterAsync()
    {
        if (atual is not null) return atual;
        var principal = (await autenticacao.GetAuthenticationStateAsync()).User;
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return Acesso.Nenhum;

        await using var db = await fabrica.CreateDbContextAsync();
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (u is null || (u.LockoutEnd is { } fim && fim > DateTimeOffset.UtcNow)) return atual = Acesso.Nenhum;
        var cliente = u.ClienteId is int cid ? await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid && c.Ativo) : null;
        // Perfil de cliente com empresa inativa/excluída: sem acesso a dados
        var clienteId = Perfis.EhCliente(u.Perfil) ? cliente?.Id : null;
        return atual = new Acesso(u.Id, u.Email, u.Perfil, clienteId, cliente?.Nome);
    }
}

/// <summary>Coloca o perfil (como papel) e a empresa no cookie de login, usados pelo menu e pelo [Authorize(Roles)].</summary>
public class ClaimsPortal(UserManager<ApplicationUser> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identidade = await base.GenerateClaimsAsync(user);
        if (user.Perfil != Perfil.Indefinido)
            identidade.AddClaim(new Claim(ClaimTypes.Role, user.Perfil.ToString()));
        if (user.ClienteId is int id)
            identidade.AddClaim(new Claim(Perfis.ClaimCliente, id.ToString()));
        return identidade;
    }
}
