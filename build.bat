@echo off
rem Rebuilds ArchTweaker.exe using the C# compiler that ships with Windows (.NET Framework 4.x).
rem No Visual Studio or SDK needed.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (echo csc.exe not found - is .NET Framework 4 installed? & pause & exit /b 1)
cd /d "%~dp0"
if not exist out mkdir out
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:out\ArchTweaker.exe /win32manifest:app.manifest /win32icon:app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.ServiceProcess.dll /r:System.Core.dll /r:System.Xml.dll /r:Accessibility.dll /r:System.IO.Compression.dll ^
  /recurse:src\*.cs
if errorlevel 1 (echo. & echo BUILD FAILED & pause & exit /b 1)
echo.
echo Built: %~dp0out\ArchTweaker.exe
pause
