// Vogel SincronizadorLV: uma vez por dia, roda na BASE do ERP os mesmos SQLs do Portal Vogel (DRE e Precificação)
// e grava só o resultado na BASELV (LV_Dre, LV_Precificacao). Na BASE só faz SELECT; grava apenas na BASELV.
// Agendar no Agendador de Tarefas do Windows do servidor do cliente.
//
// Uso:
//   SincronizadorLV                          DRE (ano atual e anterior) + Precificação (filiais do appsettings)
//   SincronizadorLV --item Dre               só a DRE (ou --item Precificacao, --item Comercial, --item Rotinas)
//   SincronizadorLV --item Dre --ano 2024    recalcula a DRE de um ano específico
//   SincronizadorLV --csv C:\Vogel\csv         também gera os CSVs para o "Importar CSV/XLSX" do portal
//   SincronizadorLV --pedidos                 atende o botão "Sincronizar agora" do portal (agendar a cada 5 min);
//                                             sem pedido pendente, sai sem fazer nada nem gravar log
using System.Globalization;
using Microsoft.Extensions.Configuration;
using SincronizadorLV;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddCommandLine(args.Where(a => a != "--pedidos").ToArray())
    .Build();

var pastaLogs = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(pastaLogs);
var arquivoLog = Path.Combine(pastaLogs, $"sincronizador-{DateTime.Now:yyyyMMdd}.log");
void Log(string texto)
{
    var linha = $"{DateTime.Now:HH:mm:ss} {texto}";
    Console.WriteLine(linha);
    File.AppendAllText(arquivoLog, linha + Environment.NewLine);
}

string LerConsulta(string chave)
{
    var caminho = Path.Combine(AppContext.BaseDirectory, config[$"Sincronizacao:{chave}"] ?? throw new InvalidOperationException($"Falta Sincronizacao:{chave} no appsettings.json."));
    return File.Exists(caminho) ? File.ReadAllText(caminho) : throw new InvalidOperationException($"Arquivo de consulta não encontrado: {caminho}");
}

// Consulta que pode faltar em pacotes antigos (ex.: comercial): sem o arquivo, o item é pulado
string? ConsultaOpcional(string chave, string padrao)
{
    var caminho = Path.Combine(AppContext.BaseDirectory, config[$"Sincronizacao:{chave}"] is { Length: > 0 } c ? c : padrao);
    return File.Exists(caminho) ? File.ReadAllText(caminho) : null;
}

try
{
    var origem = config.GetConnectionString("Origem") ?? throw new InvalidOperationException("Falta ConnectionStrings:Origem no appsettings.json.");
    var destino = config.GetConnectionString("Destino") ?? throw new InvalidOperationException("Falta ConnectionStrings:Destino no appsettings.json.");
    var opcoes = new Opcoes(
        LerConsulta("ConsultaDre"),
        LerConsulta("ConsultaPrecificacao"),
        config.GetValue("Sincronizacao:DreAnos", 2),
        config.GetSection("Sincronizacao:Filiais").Get<int[]>() is { Length: > 0 } f ? f : [1],
        config.GetValue("Sincronizacao:TempoLimiteSegundos", 1800),
        config["csv"] is { Length: > 0 } pastaCsv ? pastaCsv
            : config["Sincronizacao:PastaCsv"] is { Length: > 0 } p ? Path.Combine(AppContext.BaseDirectory, p) : null,
        Path.Combine(AppContext.BaseDirectory, config["Sincronizacao:PastaRotinas"] is { Length: > 0 } pr ? pr : "consultas/rotinas"),
        ConsultaOpcional("ConsultaComercial", "consultas/comercial.sql"),
        ConsultaOpcional("ConsultaFeriados", "consultas/feriados.sql"),
        ConsultaOpcional("ConsultaCotas", "consultas/cotas.sql"));

    string[] todos = [Sincronizador.Dre, Sincronizador.Precificacao, Sincronizador.Comercial, Sincronizador.Rotinas];
    var itens = config["item"] is { Length: > 0 } item
        ? todos.Where(t => t.Equals(item, StringComparison.OrdinalIgnoreCase)).ToArray()
        : todos;
    if (itens.Length == 0) throw new InvalidOperationException($"Item desconhecido: {config["item"]}. Opções: {string.Join(", ", todos)}");
    int? ano = config["ano"] is { Length: > 0 } a ? int.Parse(a, CultureInfo.InvariantCulture) : null;

    if (args.Contains("--pedidos"))
    {
        // Só grava log quando há pedido: esta chamada roda a cada poucos minutos
        var atendido = await new Sincronizador(origem, destino, opcoes, Log).AtenderPedidoAsync(itens, CancellationToken.None);
        return atendido == false ? 1 : 0;
    }

    Log("Início da sincronização");
    var ok = await new Sincronizador(origem, destino, opcoes, Log).ExecutarAsync(itens, ano, CancellationToken.None);
    Log(ok ? "Fim: tudo gravado." : "Fim: houve erro (veja acima e em LV_Sincronizacao).");
    return ok ? 0 : 1;
}
catch (Exception ex)
{
    Log($"ERRO: {ex.Message}");
    return 2;
}
