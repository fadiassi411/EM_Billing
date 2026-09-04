$serviceName = 'WatchDogEM'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service) { exit 0 }
if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
}
& sc.exe delete $serviceName | Out-Null
