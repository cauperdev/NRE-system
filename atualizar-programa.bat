@echo off
rem ==========================================================================
rem  Recompila a pasta "programa" (NRE.exe) depois de alterar o codigo-fonte.
rem  Precisa do .NET 10 SDK instalado (https://dotnet.microsoft.com/download).
rem  Nao precisa fechar o sistema: recompila em "programa-novo", faz backup do banco, para o
rem  programa e troca as pastas. A janela do iniciar.bat religa o sistema sozinha (F5 na pagina).
rem ==========================================================================
title NRE - Atualizar programa
cd /d "%~dp0"
echo Recompilando o programa... (pode levar alguns minutos; o sistema continua ligado)
if exist "programa-novo" rmdir /s /q "programa-novo"
dotnet publish NRE.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o programa-novo
if errorlevel 1 (
    echo.
    echo   Deu erro ao recompilar. Confira se o .NET 10 SDK esta instalado. O sistema ligado nao mudou.
    pause
    exit /b 1
)
echo Trocando para a versao nova (o sistema fica fora do ar por alguns segundos)...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$raiz = (Get-Location).Path;" ^
  "$data = Get-Date -Format 'yyyyMMdd-HHmm';" ^
  "foreach ($e in 'db','db-wal','db-shm') { if (Test-Path \"dados\nre.$e\") { Copy-Item \"dados\nre.$e\" \"dados\nre-backup-antes-atualizar-$data.$e\" } };" ^
  "Remove-Item 'programa-antigo' -Recurse -Force -ErrorAction SilentlyContinue;" ^
  "for ($i = 0; $i -lt 10; $i++) {" ^
  "  Get-Process NRE -ErrorAction SilentlyContinue | Where-Object { $_.Path -like \"$raiz\programa\*\" } | Stop-Process -Force;" ^
  "  Start-Sleep -Milliseconds 700;" ^
  "  try { if (Test-Path 'programa') { Rename-Item 'programa' 'programa-antigo' -ErrorAction Stop }; Rename-Item 'programa-novo' 'programa' -ErrorAction Stop; break } catch { }" ^
  "};" ^
  "if (Test-Path 'programa-novo') { Write-Host '  Nao consegui trocar as pastas: feche o sistema e rode de novo.'; exit 1 };" ^
  "Remove-Item 'programa-antigo' -Recurse -Force -ErrorAction SilentlyContinue;" ^
  "Write-Host '  Pronto! O sistema volta sozinho em poucos segundos: atualize a pagina (F5).'"
pause
