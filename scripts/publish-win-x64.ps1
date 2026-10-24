$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet publish src/DotaHpAlert.App -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=false -o artifacts/win-x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
    Write-Host 'Run artifacts/win-x64/DotaHpAlert.App.exe on Windows 11 x64.'
} finally { Pop-Location }
