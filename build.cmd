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
rem A ordem dos arquivos abaixo é fixa de propósito: o compilador numera os membros na ordem em que lê os
rem arquivos, e assim o executável sai igual ao que sairia com o código todo num arquivo só.
rem Ao criar um arquivo novo em src, acrescente-o à lista.
set "FONTES=AssemblyInfo.cs"
set "FONTES=%FONTES% Audio\CoreAudio.cs"
set "FONTES=%FONTES% Audio\DeviceTopology.cs"
set "FONTES=%FONTES% Native.cs"
set "FONTES=%FONTES% Program.cs"
set "FONTES=%FONTES% Models.cs"
set "FONTES=%FONTES% Callbacks.cs"
set "FONTES=%FONTES% TravaVolume.cs"
pushd "%~dp0src"
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu /out:"%~dp0bin\TravaVolume.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll %FONTES%
set "RESULTADO=%errorlevel%"
popd
if not "%RESULTADO%"=="0" exit /b 1
echo Pronto: bin\TravaVolume.exe
