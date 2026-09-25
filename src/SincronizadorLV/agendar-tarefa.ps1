# Cria (ou atualiza) a tarefa "Vogel SincronizadorLV" no Agendador de Tarefas do Windows:
# roda todo dia na virada da data (padrão 00:05), a partir desta mesma pasta.
#
# Como usar (PowerShell "Executar como administrador", dentro da pasta do SincronizadorLV):
#   powershell -ExecutionPolicy Bypass -File .\agendar-tarefa.ps1
#   powershell -ExecutionPolicy Bypass -File .\agendar-tarefa.ps1 -Horario 00:30
#   powershell -ExecutionPolicy Bypass -File .\agendar-tarefa.ps1 -Usuario "DOMINIO\usuario"
#   powershell -ExecutionPolicy Bypass -File .\agendar-tarefa.ps1 -RotinasACadaHoras 2
#     → além da virada do dia, roda só as ROTINAS (alertas) das 07:00 às 19:00 a cada 2 horas (tarefa "... Rotinas")
#   Também cria a tarefa "... Pedidos" (a cada 5 min): atende o botão "Sincronizar agora" do portal.
#   -PedidosACadaMinutos 0 não cria essa tarefa.
#
# Usuário da tarefa:
#   - Padrão SYSTEM: use quando o appsettings.json tiver login SQL (User Id/Password).
#   - Com login do Windows (Integrated Security=True), informe -Usuario com um usuário que tenha acesso ao SQL;
#     o Windows vai pedir a senha dele numa janela própria (o script não guarda nem mostra a senha).
param(
    [string]$Horario = "00:05",
    [string]$Usuario = "SYSTEM",
    [string]$NomeTarefa = "Vogel SincronizadorLV",
    [int]$RotinasACadaHoras = 0,
    [int]$PedidosACadaMinutos = 5
)

$ErrorActionPreference = "Stop"
$pasta = $PSScriptRoot
$exe = Join-Path $pasta "SincronizadorLV.exe"
if (-not (Test-Path $exe)) { throw "Não encontrei $exe. Rode este script dentro da pasta do SincronizadorLV." }

$acao = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $pasta
$gatilho = New-ScheduledTaskTrigger -Daily -At $Horario
$config = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2) -MultipleInstances IgnoreNew

if ($Usuario -eq "SYSTEM") {
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $NomeTarefa -Action $acao -Trigger $gatilho -Settings $config -Principal $principal -Force | Out-Null
} else {
    $credencial = Get-Credential -UserName $Usuario -Message "Senha do usuário que vai rodar a tarefa"
    Register-ScheduledTask -TaskName $NomeTarefa -Action $acao -Trigger $gatilho -Settings $config `
        -User $credencial.UserName -Password $credencial.GetNetworkCredential().Password -RunLevel Highest -Force | Out-Null
}

if ($RotinasACadaHoras -gt 0) {
    # Rotinas mais frescas durante o expediente: 07:00 às 19:00, a cada N horas, só o item Rotinas
    $acaoRotinas = New-ScheduledTaskAction -Execute $exe -Argument "--item Rotinas" -WorkingDirectory $pasta
    $gatilhoRotinas = New-ScheduledTaskTrigger -Daily -At "07:00"
    $gatilhoRotinas.Repetition = (New-ScheduledTaskTrigger -Once -At "07:00" -RepetitionInterval (New-TimeSpan -Hours $RotinasACadaHoras) -RepetitionDuration (New-TimeSpan -Hours 12)).Repetition
    $nomeRotinas = "$NomeTarefa Rotinas"
    if ($Usuario -eq "SYSTEM") {
        Register-ScheduledTask -TaskName $nomeRotinas -Action $acaoRotinas -Trigger $gatilhoRotinas -Settings $config -Principal $principal -Force | Out-Null
    } else {
        Register-ScheduledTask -TaskName $nomeRotinas -Action $acaoRotinas -Trigger $gatilhoRotinas -Settings $config `
            -User $credencial.UserName -Password $credencial.GetNetworkCredential().Password -RunLevel Highest -Force | Out-Null
    }
    Write-Host "Tarefa '$nomeRotinas' agendada: rotinas das 07:00 às 19:00 a cada $RotinasACadaHoras hora(s)."
}

if ($PedidosACadaMinutos -gt 0) {
    # Botão "Sincronizar agora" do portal: confere a cada N minutos se há pedido (sem pedido, sai na hora)
    $acaoPedidos = New-ScheduledTaskAction -Execute $exe -Argument "--pedidos" -WorkingDirectory $pasta
    $gatilhoPedidos = New-ScheduledTaskTrigger -Daily -At "00:00"
    $gatilhoPedidos.Repetition = (New-ScheduledTaskTrigger -Once -At "00:00" -RepetitionInterval (New-TimeSpan -Minutes $PedidosACadaMinutos) -RepetitionDuration (New-TimeSpan -Hours 24)).Repetition
    $nomePedidos = "$NomeTarefa Pedidos"
    if ($Usuario -eq "SYSTEM") {
        Register-ScheduledTask -TaskName $nomePedidos -Action $acaoPedidos -Trigger $gatilhoPedidos -Settings $config -Principal $principal -Force | Out-Null
    } else {
        Register-ScheduledTask -TaskName $nomePedidos -Action $acaoPedidos -Trigger $gatilhoPedidos -Settings $config `
            -User $credencial.UserName -Password $credencial.GetNetworkCredential().Password -RunLevel Highest -Force | Out-Null
    }
    Write-Host "Tarefa '$nomePedidos' agendada: confere pedidos do portal a cada $PedidosACadaMinutos minuto(s)."
}

Write-Host "Tarefa '$NomeTarefa' agendada: todo dia às $Horario, usuário $Usuario."
Write-Host "Para rodar agora e testar:  Start-ScheduledTask -TaskName '$NomeTarefa'"
Write-Host "Resultado: pasta logs\ e tabela LV_Sincronizacao na BASELV."
