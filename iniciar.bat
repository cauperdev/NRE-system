@echo off
rem ==========================================================================
rem  Liga o NRE SO PARA ESTE COMPUTADOR (http://localhost:5110).
rem  Nao pede permissao de administrador nem do Firewall.
rem  Para abrir tambem no celular, use o "iniciar-na-rede.bat".
rem  - Usa o programa pronto da pasta "programa" (nao precisa instalar nada).
rem  - Sem a pasta "programa", roda pelo codigo-fonte (precisa do .NET 10 SDK).
rem  - Dados: pasta "dados" (nre.db = banco, anexos = arquivos das NRE, fotos = logos das areas).
rem  Pode ficar ligado junto com o MS (o MS usa a porta 5100, o NRE a 5110).
rem  Deixe esta janela aberta enquanto usa o sistema. Fechar a janela desliga o sistema.
rem ==========================================================================
title NRE
cd /d "%~dp0"
echo.
echo   NRE
echo   ---------------------------------------------------------------
echo   Endereco: http://localhost:5110   (so neste computador)
echo   Para abrir no celular, use o "iniciar-na-rede.bat".
echo.
echo   Deixe esta janela aberta. Para desligar o sistema, feche-a.
echo   ---------------------------------------------------------------
echo.
start "" cmd /c "timeout /t 6 /nobreak >nul & start http://localhost:5110"
:ligar
if not exist "programa\NRE.exe" if exist "programa-novo\NRE.exe" (timeout /t 2 /nobreak >nul & goto ligar)
if exist "programa\NRE.exe" (
    "programa\NRE.exe" --contentRoot "%~dp0." --urls http://localhost:5110
) else (
    rem Compila em silencio: os avisos do compilador vao para um log, mostrado so se der erro.
    echo   Compilando o sistema - na primeira vez demora um pouco...
    dotnet build -nologo -v q > "%TEMP%\nre-system-compilacao.log" 2>&1
    if errorlevel 1 (
        type "%TEMP%\nre-system-compilacao.log"
        echo.
        echo   Deu erro ao compilar. Confira se o .NET 10 SDK esta instalado.
    ) else (
        dotnet run --no-build --no-launch-profile --urls http://localhost:5110
    )
)
echo.
echo   O sistema parou. Ligando de novo em 3 segundos... (para desligar de vez, feche esta janela)
timeout /t 3 /nobreak >nul
goto ligar
