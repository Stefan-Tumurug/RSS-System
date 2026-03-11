Import-Module IISAdministration
Stop-IISSite -Name "RemoteScreen_BackOffice" -Confirm:$false
Restart-WebAppPool (Get-Website -Name RemoteScreen_BackOffice).applicationPool