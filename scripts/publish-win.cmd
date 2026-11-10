@echo off
REM Erstellt das Windows-Paket BoreholeHub-1.0.0-win-x64 (self-contained, .NET 8 SDK noetig).
REM Aufruf aus dem Repository-Ordner: scripts\publish-win.cmd
setlocal
set VERSION=1.0.0
set OUT=publish\BoreholeHub-%VERSION%-win-x64
dotnet publish src\BoreholeHub.Web -c Release -r win-x64 --self-contained true -o "%OUT%" || exit /b 1
copy /y scripts\Start-BoreholeHub.cmd "%OUT%\" >nul
echo Fertig: %OUT%
