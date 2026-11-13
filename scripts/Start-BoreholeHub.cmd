@echo off
REM BoreholeHub 1.0.2 - Start mit SQL Server "." (Windows-Anmeldung), Datenbank BoreholeHub
REM Fuer SQLite statt SQL Server: die naechste Zeile auf "set Database__Provider=Sqlite" aendern.
cd /d "%~dp0"
set Database__Provider=SqlServer
set ASPNETCORE_URLS=http://localhost:5080
start "" http://localhost:5080
BoreholeHub.Web.exe
