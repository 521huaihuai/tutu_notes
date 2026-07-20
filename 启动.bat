@echo off
chcp 65001 >nul
REM 启动定制化便利贴应用
set "PATH=C:\Program Files\dotnet;%PATH%"
set "APP_DIR=%~dp0CustomStickyNote"
set "EXE=%APP_DIR%\bin\Debug\net8.0-windows\CustomStickyNote.exe"

if not exist "%EXE%" (
    echo 
    dotnet build "%APP_DIR%\CustomStickyNote.csproj" -c Debug
)

start "" "%EXE%"
exit
