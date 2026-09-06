$serviceName = 'WatchDogEM'
$firewallRuleName = 'Watch Dog EM (TCP 5080)'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    }
    & sc.exe delete $serviceName | Out-Null
}
Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
