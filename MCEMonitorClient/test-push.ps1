$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "MCEMonitor_ClientPush", [System.IO.Pipes.PipeDirection]::In)
$pipe.Connect(5000)
$reader = New-Object System.IO.StreamReader($pipe)

Write-Host "Connecté au push. En attente d'événements..." -ForegroundColor Cyan

while ($true) {
    $line = $reader.ReadLine()
    if ($line -eq $null) { break }
    Write-Host "[EVENT] $line" -ForegroundColor Yellow
}