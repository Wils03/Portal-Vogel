# =====================================================================================
# PASSO 4 — GERAR O PACOTE DO SINCRONIZADORLV PARA UM CLIENTE (roda na máquina da Vogel)
# Gera a pasta publicar\SincronizadorLV-<Cliente> pronta para copiar ao servidor do cliente
# (não precisa instalar .NET lá). Guia: docs\implantacao\GUIA.md
#
# Exemplos (PowerShell, na raiz do projeto TesteWil):
#   .\docs\implantacao\gerar-pacote.ps1 -Cliente "Loja X" -Servidor "SERVIDOR01"
#   .\docs\implantacao\gerar-pacote.ps1 -Cliente "Loja Z" -Servidor "SERVIDOR02" -ConsultaDre .\docs\dre-propria-tabelas-sqlserver.sql
#   .\docs\implantacao\gerar-pacote.ps1 -Cliente "Loja Y" -Servidor "SRV\SQLEXPRESS" -Filiais 1,2 -Autenticacao Windows
#
# -Autenticacao Sql (padrão): login vogel_sincronizador (passo 3); a senha fica para digitar NO SERVIDOR do cliente.
# -Autenticacao Windows: usa o usuário do Windows que rodar o programa/tarefa.
# -ConsultaDre: SQL da DRE específico do cliente (senão, usa o modelo padrão do portal).
# =====================================================================================
param(
    [Parameter(Mandatory = $true)] [string]$Cliente,
    [Parameter(Mandatory = $true)] [string]$Servidor,
    [string]$BaseErp = "BASE",
    [int[]]$Filiais = @(1),
    [string]$ConsultaDre = "",
    [ValidateSet("Sql", "Windows")] [string]$Autenticacao = "Sql",
    [int]$DreAnos = 2
)
$ErrorActionPreference = "Stop"

$raiz = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$projeto = Join-Path $raiz "src\SincronizadorLV\SincronizadorLV.csproj"
$nomePasta = "SincronizadorLV-" + (($Cliente -replace '[^\w\- ]', '') -replace '\s+', '-')
$saida = Join-Path $raiz "publicar\$nomePasta"

if ($ConsultaDre -and -not (Test-Path $ConsultaDre)) { throw "Arquivo da DRE não encontrado: $ConsultaDre" }

Write-Host "Gerando $saida ..."
if (Test-Path $saida) { Remove-Item $saida -Recurse -Force }
dotnet publish $projeto -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -nologo -v q -o $saida
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar o SincronizadorLV." }
Remove-Item (Join-Path $saida "*.pdb") -ErrorAction SilentlyContinue

if ($ConsultaDre) { Copy-Item $ConsultaDre (Join-Path $saida "consultas\dre.sql") -Force }

# SQLs das rotinas/alertas, exportados do portal (rotina-<Id>.sql): o sincronizador grava o resultado em LV_Rotina
$portal = Join-Path $raiz "src\Dashborad"
Push-Location $portal
try {
    dotnet run --no-build -- --exportar-rotinas (Join-Path $saida "consultas\rotinas")
    if ($LASTEXITCODE -ne 0) { Write-Warning "Não consegui exportar as rotinas (compile o portal e rode de novo). O pacote sai sem rotinas." }
} finally { Pop-Location }

$login = if ($Autenticacao -eq "Sql") { "User Id=vogel_sincronizador;Password=DIGITE_A_SENHA_NO_SERVIDOR" } else { "Integrated Security=True" }
$filiaisJson = ($Filiais -join ", ")
$config = @"
{
  // $Cliente — gerado em $(Get-Date -Format 'dd/MM/yyyy HH:mm') por docs\implantacao\gerar-pacote.ps1
  // Origem = banco do ERP (só leitura). Destino = BASELV (tabelas LV_). Guia: docs\implantacao\GUIA.md
  // Login SQL: troque DIGITE_A_SENHA_NO_SERVIDOR pela senha do vogel_sincronizador (passo 3) nas DUAS linhas.
  "ConnectionStrings": {
    "Origem": "Server=$Servidor;Database=$BaseErp;$login;TrustServerCertificate=True;Application Intent=ReadOnly;Application Name=Vogel SincronizadorLV",
    "Destino": "Server=$Servidor;Database=BASELV;$login;TrustServerCertificate=True;Application Name=Vogel SincronizadorLV"
  },
  "Sincronizacao": {
    "ConsultaDre": "consultas/dre.sql",
    "ConsultaPrecificacao": "consultas/precificacao.sql",
    "DreAnos": $DreAnos,
    "Filiais": [ $filiaisJson ],
    "TempoLimiteSegundos": 1800,
    "PastaCsv": ""
  }
}
"@
Set-Content -Path (Join-Path $saida "appsettings.json") -Value $config -Encoding UTF8

Write-Host ""
Write-Host "Pacote pronto: $saida"
Write-Host "  Servidor: $Servidor   ERP: $BaseErp   Filiais: $filiaisJson   Autenticação: $Autenticacao"
Write-Host "  DRE: $(if ($ConsultaDre) { $ConsultaDre } else { 'modelo padrão do portal' })"
Write-Host "  Rotinas: $((Get-ChildItem (Join-Path $saida 'consultas\rotinas') -Filter 'rotina-*.sql' -ErrorAction SilentlyContinue).Count) arquivo(s) em consultas\rotinas"
Write-Host "Próximo passo (GUIA, passo 5): copiar a pasta para o servidor do cliente e rodar SincronizadorLV.exe."
