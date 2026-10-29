@echo off
setlocal
set EXE=C:\Temp\opencode\FluentSblock-artifacts\bin\FluentSblock\debug\FluentSblock.exe
if not exist "%EXE%" (
  echo [FluentSblock] Debug exe not found: %EXE%
  echo Build first: dotnet build src\FluentSblock\FluentSblock.csproj --artifacts-path C:/Temp/opencode/FluentSblock-artifacts -c Debug
  exit /b 1
)
start "" "%EXE%" %*
