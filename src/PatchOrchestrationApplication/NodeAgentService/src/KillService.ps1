$id = Get-WmiObject -Class Win32_Service -Filter "Name LIKE 'POSNodeSvc'" | Select-Object -ExpandProperty ProcessId
if($id -ne $null)
{
   $process = Get-Process -Id $id
   if($process.Id -ne $null)
   {
      taskkill /F /PID $process.Id
   }
}

