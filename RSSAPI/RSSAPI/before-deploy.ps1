Import-Module IISAdministration
Stop-IISSite -Name "RemoteScreen_Api" -Confirm:$false
Restart-WebAppPool (Get-Website -Name RemoteScreen_Api).applicationPool