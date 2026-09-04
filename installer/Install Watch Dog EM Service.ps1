param([Parameter(Mandatory = $true)][string]$InstallDirectory)

$ErrorActionPreference = 'Stop'
$serviceName = 'WatchDogEM'
$displayName = 'Watch Dog EM Server'
$executable = Join-Path $InstallDirectory 'MallEnergyBilling.Web.exe'
$binaryPath = '"' + $executable + '" --urls http://localhost:5080'

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    }
    & sc.exe config $serviceName binPath= $binaryPath start= delayed-auto DisplayName= $displayName | Out-Null
}
else {
    New-Service -Name $serviceName -BinaryPathName $binaryPath -DisplayName $displayName `
        -Description 'Watch Dog EM web, Modbus polling, backup, and invoice services.' `
        -StartupType Automatic | Out-Null
    & sc.exe config $serviceName start= delayed-auto | Out-Null
}

# Restart after the first three unexpected failures and reset the failure count daily.
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
& sc.exe failureflag $serviceName 1 | Out-Null
Start-Service -Name $serviceName
