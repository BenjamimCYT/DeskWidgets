@echo off
REM Requer o .NET 10 SDK: https://dotnet.microsoft.com/download
cd /d "%~dp0"
echo Compilando DeskWidgets (autocontido, roda sem instalar o .NET)...
dotnet publish DeskWidgets.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o publish
if errorlevel 1 (
  echo.
  echo A compilacao falhou. Copie as linhas de erro acima e me envie.
  pause
  exit /b 1
)
echo.
echo Pronto: publish\DeskWidgets.exe
start "" explorer publish
pause
