$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Remove Visual Studio/manual backup source copies that would otherwise create
# duplicate TypeSenseOverlay types in an SDK-style project.
Get-ChildItem -Path $root -Recurse -File -Filter '* - Copy.cs' |
    Remove-Item -Force
Get-ChildItem -Path $root -Recurse -File -Filter '* - Copy*.cs' |
    Remove-Item -Force

Write-Host 'Grey Board source-copy cleanup complete.' -ForegroundColor Green
Write-Host 'Now run: dotnet clean; dotnet build'
