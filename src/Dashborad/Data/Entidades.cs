using System.ComponentModel.DataAnnotations;

namespace Portal.Data;

public enum TipoBanco { SqlServer, PostgreSql, MySql, Firebird }

public enum Severidade { Info, Atencao, Critico }

public static class Rotulos
{
    public static string Texto(Severidade s) => s switch
    {
        Severidade.Info => "Informativo",
        Severidade.Atencao => "Atenção",
        Severidade.Critico => "Crítico",
        _ => s.ToString()
    };

    public static string Texto(TipoBanco t) => t switch
    {
        TipoBanco.SqlServer => "SQL Server",
        TipoBanco.PostgreSql => "PostgreSQL",
        TipoBanco.MySql => "MySQL",
        TipoBanco.Firebird => "Firebird",
        _ => t.ToString()
    };
}

public enum StatusAlerta { Novo, Notificado, Resolvido, Erro }

public class Cliente
{
    public int Id { get; set; }

    [Required(ErrorMessage = "O campo {0} é obrigatório."), StringLength(150, ErrorMessage = "O campo {0} aceita no máximo {1} caracteres.")]
    public string Nome { get; set; } = "";

    [StringLength(20, ErrorMessage = "O campo {0} aceita no máximo {1} caracteres.")]
    public string? Cnpj { get; set; }

    /// <summary>
    /// Impressão digital do CNPJ (SHA-256 dos dígitos), como a REV_Base do REV ENTRADAS.
    /// Na versão online identifica de qual empresa vêm os dados sincronizados sem trafegar o CNPJ aberto.
    /// </summary>
    [StringLength(64)]
    public string? Impressao { get; set; }

    public static string? CalcularImpressao(string? cnpj)
    {
        var digitos = new string((cnpj ?? "").Where(char.IsDigit).ToArray());
        return digitos.Length == 0 ? null
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(digitos)));
    }

    [StringLength(100, ErrorMessage = "O campo {0} aceita no máximo {1} caracteres.")]
    public string? Contato { get; set; }

    /// <summary>Telefone com DDI+DDD, só dígitos (ex.: 5551999999999).</summary>
    [StringLength(20, ErrorMessage = "O campo {0} aceita no máximo {1} caracteres.")]
    public string? WhatsApp { get; set; }

    public bool Ativo { get; set; } = true;

    public TipoBanco TipoBanco { get; set; }

    /// <summary>String de conexão criptografada com Data Protection. Nunca é exibida na tela.</summary>
    public string? ConexaoCriptografada { get; set; }

    public string? Observacoes { get; set; }

    /// <summary>SELECT que devolve Competencia, Grupo, Conta, Valor para a DRE. Usa {inicio} e {fim}.</summary>
    public string? SqlDre { get; set; }

    /// <summary>SELECT dos produtos para a precificação. Usa {filial}.</summary>
    public string? SqlPrecificacao { get; set; }

    /// <summary>SELECT das vendas por mês e vendedor (módulo Comercial). Usa {inicio} e {fim}.</summary>
    public string? SqlComercial { get; set; }
    public DateTime? ComercialAtualizadoEm { get; set; }
    public DateTime? ComercialDadosDe { get; set; }

    /// <summary>Quando a DRE foi importada no portal e de quando são os dados (última sincronização da BASELV; nulo = desconhecido, ex.: planilha).</summary>
    public DateTime? DreAtualizadoEm { get; set; }
    public DateTime? DreDadosDe { get; set; }

    /// <summary>Quando a Precificação foi importada no portal e de quando são os dados.</summary>
    public DateTime? PrecificacaoAtualizadoEm { get; set; }
    public DateTime? PrecificacaoDadosDe { get; set; }

    /// <summary>
    /// Rotinas/alertas leem o resultado gravado pelo SincronizadorLV na BASELV (tabela LV_Rotina)
    /// em vez de rodar o SQL na BASE do ERP.
    /// </summary>
    public bool RotinasPelaBaseLv { get; set; }

    /// <summary>Filial da última importação da Precificação pelo banco (usada pela importação automática; nulo = 1).</summary>
    public int? FilialPrecificacao { get; set; }

    /// <summary>Quem recebe os avisos de revisão de preços (tela Precificação).</summary>
    [StringLength(100)] public string? AvisoPrecosNome { get; set; }
    [StringLength(20)] public string? AvisoPrecosWhatsApp { get; set; }

    // Parâmetros de precificação do cliente
    public decimal ImpostosPct { get; set; }
    public decimal ComissaoPct { get; set; }
    public decimal OutrosVariaveisPct { get; set; }
    public decimal MargemDesejadaPct { get; set; }
    public decimal CustosFixosMensais { get; set; }
    public decimal FaturamentoMedioMensal { get; set; }
}

/// <summary>Verificação que roda um SQL no banco do cliente e gera alerta quando o resultado passa do limite.</summary>
public class Rotina
{
    public int Id { get; set; }

    [Required(ErrorMessage = "O campo {0} é obrigatório."), StringLength(150, ErrorMessage = "O campo {0} aceita no máximo {1} caracteres.")]
    public string Nome { get; set; } = "";

    public string? Descricao { get; set; }

    /// <summary>Só roda em clientes deste tipo de banco (SQL varia por banco). Nulo = todos.</summary>
    public TipoBanco? TipoBanco { get; set; }

    /// <summary>SELECT que retorna um número (ex.: COUNT) ou linhas (a quantidade de linhas vira o resultado).</summary>
    [Required(ErrorMessage = "O campo {0} é obrigatório.")]
    [Display(Name = "SQL")]
    public string Sql { get; set; } = "";

    public Severidade Severidade { get; set; } = Severidade.Atencao;

    /// <summary>Gera alerta quando o resultado for maior que este valor.</summary>
    public decimal Limite { get; set; }

    /// <summary>Variáveis: {cliente}, {quantidade}, {rotina}, {data}.</summary>
    public string ModeloMensagem { get; set; } = "";

    public bool Ativa { get; set; } = true;

    /// <summary>Envia a mensagem sozinho (Pagebot) quando surgir um alerta novo, em horário comercial.</summary>
    public bool EnviarAutomaticamente { get; set; }

    /// <summary>Hora do dia (0-23) para a execução automática. Nulo = só manual.</summary>
    public int? HoraExecucao { get; set; }

    public DateTime? UltimaExecucao { get; set; }
}

/// <summary>
/// Programação de uma rotina para uma empresa (o gestor do cliente edita; o SQL continua da Vogel).
/// Sem registro = segue o padrão da rotina.
/// </summary>
public class RotinaCliente
{
    public int Id { get; set; }
    public int RotinaId { get; set; }
    public int ClienteId { get; set; }

    /// <summary>A empresa quer esta verificação (só vale se a Vogel liberou a rotina).</summary>
    public bool Ativa { get; set; } = true;

    /// <summary>A empresa quer o aviso automático no WhatsApp (só vale se a Vogel permitiu na rotina).</summary>
    public bool EnviarAutomaticamente { get; set; } = true;

    /// <summary>Hora escolhida pela empresa (0-23). Nulo = horário padrão da rotina.</summary>
    public int? HoraExecucao { get; set; }

    public DateTime? UltimaExecucao { get; set; }
    public DateTime? AtualizadoEm { get; set; }
    [StringLength(256)] public string? AtualizadoPor { get; set; }
}

/// <summary>O que vale de fato para uma empresa: a Vogel limita, a empresa escolhe dentro do limite.</summary>
public record ConfigRotina(bool Ativa, int? Hora, bool EnvioAutomatico)
{
    public static ConfigRotina Efetiva(Rotina rotina, RotinaCliente? daEmpresa) => new(
        rotina.Ativa && (daEmpresa?.Ativa ?? true),
        daEmpresa?.HoraExecucao ?? rotina.HoraExecucao,
        rotina.EnviarAutomaticamente && (daEmpresa?.EnviarAutomaticamente ?? true));
}

/// <summary>Configuração do envio de mensagens (linha única), editada na tela Configurações.</summary>
public class ConfiguracaoMensagens
{
    public int Id { get; set; }
    [StringLength(20)] public string Modo { get; set; } = "Manual";
    public bool ModoTeste { get; set; } = true;
    [StringLength(20)] public string? NumeroTeste { get; set; }
    public int HorarioInicio { get; set; } = 8;
    public int HorarioFim { get; set; } = 18;
    public bool EnviarSabado { get; set; }
    public bool EnviarDomingo { get; set; }
    public int LimiteDiario { get; set; } = 20;
    public int LimitePorClienteDia { get; set; } = 1;
    public int IntervaloSegundos { get; set; } = 60;
    public int TentativasMaximas { get; set; } = 3;

    /// <summary>forceSend do Pagebot: envia mesmo com atendimento aberto com o número.</summary>
    public bool ForceSend { get; set; }
    [StringLength(300)] public string PagebotUrl { get; set; } = "https://api.pagebot.com.br/core/v2/api/chats/send-text";

    /// <summary>Token do canal Pagebot criptografado (Data Protection). Nunca volta para a tela.</summary>
    public string? TokenCriptografado { get; set; }
    public DateTime? AtualizadoEm { get; set; }
    [StringLength(256)] public string? AtualizadoPor { get; set; }
}

public class Alerta
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public int RotinaId { get; set; }
    public Rotina? Rotina { get; set; }
    public DateTime DataHora { get; set; }
    public decimal Quantidade { get; set; }
    public Severidade Severidade { get; set; }
    public StatusAlerta Status { get; set; }
    public string Mensagem { get; set; } = "";
    public string? Erro { get; set; }

    /// <summary>Última vez que a rotina encontrou o problema de novo (sem gerar alerta duplicado).</summary>
    public DateTime? AtualizadoEm { get; set; }

    public DateTime? ResolvidoEm { get; set; }

    /// <summary>Amostra das linhas retornadas (JSON), para conferência.</summary>
    public string? DetalheJson { get; set; }
}

public class MensagemEnviada
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public int? AlertaId { get; set; }
    public DateTime DataHora { get; set; }
    public string Destino { get; set; } = "";
    public string Texto { get; set; } = "";
    public string Canal { get; set; } = "";
    public bool Sucesso { get; set; }
    public string? Retorno { get; set; }
    public string? Usuario { get; set; }
}

public class Produto
{
    public int Id { get; set; }
    public int ClienteId { get; set; }

    [StringLength(60, ErrorMessage = "O campo {0} aceita no máximo {1} caracteres.")]
    public string Codigo { get; set; } = "";

    [StringLength(200, ErrorMessage = "O campo {0} aceita no máximo {1} caracteres.")]
    public string Descricao { get; set; } = "";

    public decimal Custo { get; set; }
    public decimal PrecoAtual { get; set; }
    public decimal QtdMes { get; set; }

    /// <summary>Impostos sobre a venda do grupo de formação de preço do sistema (%). Nulo = usa o do cliente.</summary>
    public decimal? ImpostosPct { get; set; }

    [StringLength(80)]
    public string? GrupoPreco { get; set; }

    /// <summary>Lucro desejado cadastrado no sistema (%).</summary>
    public decimal? LucroDesejadoPct { get; set; }

    /// <summary>true = lucro sobre o custo (markup); false = sobre o preço de venda (margem).</summary>
    public bool? LucroSobreCusto { get; set; }
}

public enum GrupoDre
{
    ReceitaBruta,
    Deducoes,
    Cmv,
    DespesasOperacionais,
    ReceitasFinanceiras,
    DespesasFinanceiras,
    ImpostosSobreLucro
}

public class LancamentoDre
{
    public int Id { get; set; }
    public int ClienteId { get; set; }

    /// <summary>Primeiro dia do mês de competência.</summary>
    public DateOnly Competencia { get; set; }

    public GrupoDre Grupo { get; set; }

    [StringLength(150, ErrorMessage = "O campo {0} aceita no máximo {1} caracteres.")]
    public string Conta { get; set; } = "";

    /// <summary>Valor positivo; o grupo define se soma ou subtrai (negativo = estorno dentro do grupo).</summary>
    public decimal Valor { get; set; }
}

/// <summary>Vendas de um vendedor (estrutura de venda do ERP) num dia — vêm do SQL comercial (BASELV).</summary>
public class VendaVendedor
{
    public int Id { get; set; }
    public int ClienteId { get; set; }

    /// <summary>Dia da venda.</summary>
    public DateOnly Data { get; set; }

    /// <summary>Primeiro dia do mês da venda (para filtrar por mês/ano).</summary>
    public DateOnly Competencia { get; set; }

    [StringLength(20)] public string Vendedor { get; set; } = "";
    [StringLength(100)] public string? NomeVendedor { get; set; }
    [StringLength(20)] public string? Gerente { get; set; }
    [StringLength(100)] public string? NomeGerente { get; set; }

    public decimal VendaBruta { get; set; }
    public decimal Devolucoes { get; set; }
    public decimal Cmv { get; set; }

    /// <summary>Número de vendas (documentos).</summary>
    public int Documentos { get; set; }

    /// <summary>Itens vendidos (linhas dos documentos).</summary>
    public int Itens { get; set; }

    /// <summary>Vendas com local de pagamento PERMUTA (o Comercial pode desconsiderá-las, como o relatório "Metas" do ERP).</summary>
    public bool Permuta { get; set; }
}

/// <summary>
/// Meta de venda (venda líquida) de um vendedor num mês: a cota cadastrada no ERP (tabela Cotas de Vendas) ou,
/// se o ERP não tiver, a digitada no portal pelo gestor ou pela Vogel.
/// </summary>
public class MetaVendedor
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public DateOnly Competencia { get; set; }
    [StringLength(20)] public string Vendedor { get; set; } = "";
    public decimal Meta { get; set; }

    /// <summary>Veio da cota do ERP (não se edita no portal; a importação atualiza).</summary>
    public bool DoErp { get; set; }
    public DateTime AtualizadoEm { get; set; }
    [StringLength(256)] public string? AtualizadoPor { get; set; }
}

/// <summary>Feriado do ERP (fixos já em datas): dias úteis do Comercial = segunda a sábado, menos feriados.</summary>
public class FeriadoCliente
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public DateOnly Data { get; set; }
    [StringLength(100)] public string? Descricao { get; set; }
}

/// <summary>Dias úteis de um mês ajustados à mão pelo gestor (quando o cálculo seg–sáb menos feriados não serve).</summary>
public class DiasUteisMes
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public DateOnly Competencia { get; set; }
    public int DiasUteis { get; set; }
}
