$root = "C:\Users\kk\Documents\Software\Mail2SNMP Server"
$zip = Join-Path $root "Mail2SNMP-Server.zip"

# Clean bin/obj first
Get-ChildItem $root -Recurse -Directory -Include 'bin','obj' | ForEach-Object {
    Write-Host "Removing $_"
    Remove-Item $_ -Recurse -Force
}

# Create ZIP without bin/obj
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $root 'src'),(Join-Path $root 'tests'),(Join-Path $root 'Mail2SNMP.sln'),(Join-Path $root '.claude'),(Join-Path $root 'tools') -DestinationPath $zip -Force

$size = (Get-Item $zip).Length / 1MB
Write-Host ("ZIP created: {0:N1} MB" -f $size)
