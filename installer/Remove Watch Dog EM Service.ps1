$serviceName = 'WatchDogEM'
$firewallRuleName = 'Watchdog Energy Management (TCP 5080)'
$legacyFirewallRuleName = 'Watch Dog EM (TCP 5080)'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    }
    & sc.exe delete $serviceName | Out-Null
}
Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Get-NetFirewallRule -DisplayName $legacyFirewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
