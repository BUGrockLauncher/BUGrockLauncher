@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set ICON=..\BUGrockLauncher\Assets\bugrock.ico
set OUT=..\BUGrock_Launcher_Bin\BUGrock_Check.exe

echo Compiling BUGrock_Check.exe...
"%CSC%" /target:winexe /win32icon:"%ICON%" /out:"%OUT%" Program.cs

if %ERRORLEVEL% EQU 0 (
    echo Compilation successful! Created %OUT%
) else (
    echo Compilation failed with code %ERRORLEVEL%
)
