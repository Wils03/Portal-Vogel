using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portal.Components;
using Portal.Components.Account;
using Portal.Data;
using Portal.Services;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

if (args.Contains("--self-test"))
    return AutoTeste.Executar();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// Banco do portal: SQL Server PORTAL_VG. A string completa (com a senha do login do portal) fica nos user-secrets.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// A fábrica também registra o ApplicationDbContext como scoped (usado pelo Identity).
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        // Bloqueia por 15 min após 5 senhas erradas
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddErrorDescriber<IdentityErrosPtBr>()
    .AddClaimsPrincipalFactory<ClaimsPortal>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Confere o login a cada minuto: mudança de perfil/empresa ou desativação vale logo (e renova os dados do cookie).
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

// Chaves que criptografam as strings de conexão dos clientes: guardar junto com o banco (e fazer backup!).
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "Data", "keys")))
    .SetApplicationName("Portal");

builder.Services.AddScoped<AcessoService>();
builder.Services.AddScoped<ClienteAtualService>();
builder.Services.AddSingleton<BancoClienteService>();
builder.Services.AddSingleton(sp => new ConfiguracaoMensagensStore(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IDataProtectionProvider>()));
builder.Services.AddScoped<RotinaService>();
builder.Services.AddScoped<ImportacaoBaseLv>();
builder.Services.AddScoped<PedidoSincronizacao>();
builder.Services.AddHttpClient<MensagemService>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHostedService<AgendadorRotinas>();

var ptBr = CultureInfo.GetCultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = ptBr;
CultureInfo.DefaultThreadCurrentUICulture = ptBr;

var app = builder.Build();

// Ferramentas de linha de comando (não sobem o site)
if (args.Contains("--copiar-sqlite"))
    return await CopiaSqlite.ExecutarAsync(args[Array.IndexOf(args, "--copiar-sqlite") + 1], app.Services);
if (args.Contains("--importar-baselv"))
{
    // Importa agora da BASELV (todas as empresas que leem a BASELV, ou só a indicada): dotnet run -- --importar-baselv [clienteId]
    var posicao = Array.IndexOf(args, "--importar-baselv");
    int? somente = posicao + 1 < args.Length && int.TryParse(args[posicao + 1], out var id) ? id : null; // dotnet run -- --importar-baselv 1 --ano 2024
    using var escopo = app.Services.CreateScope();
    var fabricaPortal = escopo.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    var importacao = escopo.ServiceProvider.GetRequiredService<ImportacaoBaseLv>();
    await using var dbPortal = await fabricaPortal.CreateDbContextAsync();
    var empresas = await dbPortal.Clientes.AsNoTracking().Where(c => c.Ativo && c.ConexaoCriptografada != null && (somente == null || c.Id == somente)).ToListAsync();
    foreach (var empresa in empresas.Where(ImportacaoBaseLv.UsaBaseLv))
    {
        var r = await importacao.ImportarAsync(empresa, DateTime.Now, forcar: true);
        Console.WriteLine($"{r.Cliente}: DRE {(r.Dre ? "ok" : "-")} · Precificação {(r.Precificacao ? "ok" : "-")}{(r.Erro is null ? "" : " · ERRO: " + r.Erro)}");
        // --ano N: também o Comercial de um ano específico (ex.: o ano anterior ao anterior, para comparar)
        if (args.Contains("--ano") && int.TryParse(args[Array.IndexOf(args, "--ano") + 1], out var anoExtra) && ImportacaoBaseLv.LeBaseLv(empresa.SqlComercial))
            Console.WriteLine($"  Comercial {anoExtra}: {await ImportacaoDados.ImportarComercialAsync(fabricaPortal, escopo.ServiceProvider.GetRequiredService<BancoClienteService>(), empresa, empresa.SqlComercial!, anoExtra)} linha(s)");
    }
    return 0;
}
if (args.Contains("--exportar-rotinas"))
{
    // SQLs das rotinas para o pacote do SincronizadorLV: dotnet run -- --exportar-rotinas <pasta>
    var pasta = args[Array.IndexOf(args, "--exportar-rotinas") + 1];
    var quantas = await RotinaService.ExportarParaSincronizadorAsync(app.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(), pasta);
    Console.WriteLine($"{quantas} rotina(s) exportada(s) para {pasta}");
    return 0;
}
if (args.Contains("--painel"))
{
    // Mesmo cálculo dos cartões do Painel, só com totais: dotnet run -- --painel <clienteId>
    var idPainel = int.Parse(args[Array.IndexOf(args, "--painel") + 1]);
    await using var dbPainel = await app.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();
    var cli = await dbPainel.Clientes.AsNoTracking().SingleAsync(c => c.Id == idPainel);
    var fimDre = await dbPainel.LancamentosDre.Where(l => l.ClienteId == idPainel && l.Grupo == GrupoDre.ReceitaBruta && l.Valor > 0).MaxAsync(l => (DateOnly?)l.Competencia);
    var lanc = fimDre is DateOnly f ? await dbPainel.LancamentosDre.AsNoTracking().Where(l => l.ClienteId == idPainel && l.Competencia >= f.AddMonths(-13) && l.Competencia <= f).ToListAsync() : [];
    var rv = PainelResumo.Resumir(lanc, DataDosDados.Referencia(cli.DreDadosDe, cli.DreAtualizadoEm));
    var rp = PainelResumo.ResumirPrecos(cli, await dbPainel.Produtos.AsNoTracking().Where(p => p.ClienteId == idPainel && p.QtdMes > 0).ToListAsync());
    Console.WriteLine($"Venda {rv?.Mes:MM/yyyy}: {rv?.VendaLiquida:N2} parcial={rv?.Parcial} dias={rv?.DiasComDados} projeção={rv?.ProjecaoVenda:N2} vsMesAnt={rv?.VariacaoMesAnterior:P1} vsAnoAnt={rv?.VariacaoAnoAnterior:P1}");
    Console.WriteLine($"PE: {rv?.Pe?.PontoEquilibrio:N2} progresso={rv?.ProgressoPe:P1} projeçãoRB={rv?.ProjecaoReceitaBruta:N2}");
    Console.WriteLine($"Margem bruta: {rv?.MargemBrutaPct:P1}  Resultado {rv?.MesFechado:MM/yyyy}: {rv?.ResultadoMesFechado:N2} ({rv?.MargemLiquidaMesFechado:P1})");
    Console.WriteLine($"Preços: {rp.ComProblema} de {rp.ComVenda} (abaixo da regra {rp.AbaixoRegra}, margem negativa {rp.MargemNegativa}, sem custo {rp.SemCusto}) venda em risco {rp.VendaMesEmRisco:N2}/mês");
    return 0;
}
if (args.Contains("--consulta"))
    return await ConsultaDiagnostico.ExecutarAsync(args, app.Services);

await DadosIniciais.AplicarAsync(app.Services);
await app.Services.GetRequiredService<ConfiguracaoMensagensStore>().CarregarAsync(app.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>());

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();
return 0;
