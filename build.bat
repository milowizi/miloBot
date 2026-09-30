@echo off
cd /d "%~dp0"
taskkill /im Milo.exe >nul 2>&1
timeout /t 1 /nobreak >nul
taskkill /im Milo.exe /f >nul 2>&1
echo Milo wordt gebouwd...
dotnet publish MiloBot.csproj -c Release -r win-x64 --self-contained false -o dist > build.log 2>&1
set BUILD_ERR=%errorlevel%
type build.log
if not "%BUILD_ERR%"=="0" goto fail
echo.
echo Klaar! Milo staat in dist\Milo.exe
start "" "dist\Milo.exe"
exit /b 0
:fail
echo.
echo Build mislukt, stuur build.log door.
pause
exit /b 1
