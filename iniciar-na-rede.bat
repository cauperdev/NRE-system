@echo off
rem ==========================================================================
rem  Liga o NRE PARA A REDE: abre neste computador e tambem no celular / outros
rem  computadores da mesma rede (https://IP-DESTE-COMPUTADOR:5464).
rem  Na primeira vez o Firewall do Windows pergunta se pode liberar a rede: isso pede
rem  permissao de administrador UMA vez. Sem essa permissao, use o "iniciar.bat".
rem ==========================================================================
title NRE (rede)
cd /d "%~dp0"
echo.
echo   NRE  (aberto para a rede)
echo   ---------------------------------------------------------------
echo   Neste computador: http://localhost:5110
echo   No celular (mesma rede): https://IP-DESTE-COMPUTADOR:5464
echo   Enderecos IPv4 deste computador:
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do for /f "tokens=*" %%b in ("%%a") do echo      https://%%b:5464
echo.
echo   Deixe esta janela aberta. Para desligar o sistema, feche-a.
echo   ---------------------------------------------------------------
echo.
start "" cmd /c "timeout /t 6 /nobreak >nul & start http://localhost:5110"
:ligar
if not exist "programa\NRE.exe" if exist "programa-novo\NRE.exe" (timeout /t 2 /nobreak >nul & goto ligar)
if exist "programa\NRE.exe" (
    "programa\NRE.exe" --contentRoot "%~dp0." --urls http://0.0.0.0:5110
) else (
    rem Compila em silencio: os avisos do compilador vao para um log, mostrado so se der erro.
    echo   Compilando o sistema - na primeira vez demora um pouco...
    dotnet build -nologo -v q > "%TEMP%\nre-system-compilacao.log" 2>&1
    if errorlevel 1 (
        type "%TEMP%\nre-system-compilacao.log"
        echo.
        echo   Deu erro ao compilar. Confira se o .NET 10 SDK esta instalado.
    ) else (
        dotnet run --no-build --no-launch-profile --urls http://0.0.0.0:5110
    )
)
echo.
echo   O sistema parou. Ligando de novo em 3 segundos... (para desligar de vez, feche esta janela)
timeout /t 3 /nobreak >nul
goto ligar
