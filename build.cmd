@echo off
chcp 65001 >nul
rem Compila a Trava de volume com o compilador C# que já vem no Windows (.NET Framework 4.x).
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Compilador C# do .NET Framework 4 não encontrado.
  exit /b 1
)
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu /out:"%~dp0bin\TravaVolume.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%~dp0src\TravaVolume.cs"
if errorlevel 1 exit /b 1
echo Pronto: bin\TravaVolume.exe
