@echo off
setlocal
set EXE=C:\Temp\opencode\FluentSblock-artifacts\bin\FluentSblock\debug\FluentSblock.exe
if not exist "%EXE%" (
  echo [FluentSblock] Debug exe non trovato: %EXE%
  echo Esegui prima: dotnet build src\FluentSblock\FluentSblock.csproj --artifacts-path C:/Temp/opencode/FluentSblock-artifacts -c Debug
  exit /b 1
)
net session >nul 2>&1
if %errorlevel%==0 (
  start "" "%EXE%" %*
) else (
  echo [FluentSblock] Richiesta elevazione UAC...
  if "%~1"=="" (
    powershell -NoProfile -Command "Start-Process '%EXE%' -Verb RunAs"
  ) else (
    powershell -NoProfile -Command "Start-Process '%EXE%' -ArgumentList '%*' -Verb RunAs"
  )
)
