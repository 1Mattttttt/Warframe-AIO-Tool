@echo off
setlocal EnableDelayedExpansion
color C

:: ============================================================
::  #1Matttttttttt
::  Network Configuration Tool
::  Created by: #1Matttttttttt
:: ============================================================

for /f %%a in ('echo prompt $H ^| cmd') do set "CR=%%a"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo This script requires administrative privileges.
    echo Please run as administrator.
    pause
    exit
)

cls
echo ============================================
echo         #1Matttttttttt Network Config
echo ============================================
echo.
for /l %%i in (10,-1,1) do (
    cls
    echo ============================================
    echo         #1Matttttttttt Network Config
    echo ============================================
    echo.
    echo Starting in %%i seconds... DO NOT TOUCH ANYTHING AT ALL.
    echo PC WILL RESTART AFTER 3 MINUTES.
    timeout /t 1 >nul
)

cls
echo ============================================
echo         #1Matttttttttt Network Config
echo ============================================
echo.

call :RunStep "Flushing DNS..." "ipconfig /flushdns" "Successfully flushed DNS" 30
call :RunStep "Registering DNS..." "ipconfig /registerdns" "Successfully registered DNS" 30
call :RunStep "Releasing IP..." "ipconfig /release" "Successfully released IP" 30
call :RunStep "Renewing IP..." "ipconfig /renew" "Successfully renewed IP" 30
call :RunStep "Resetting Winsock..." "netsh winsock reset" "Successfully reset Winsock" 30
call :RunStep "Clearing ARP cache..." "arp -d" "Successfully cleared ARP cache" 5
call :RunStep "Deleting ARP table..." "netsh interface ip delete arpcache" "Successfully deleted ARP cache" 5

echo.
echo Restarting in 3 seconds...
timeout /t 3 >nul
shutdown /r /t 0
exit /b

:RunStep
cls
echo ============================================
echo         #1Matttttttttt Network Config
echo ============================================
echo.
echo %~1
%~2 >nul 2>&1
echo %~3

set /a sec=%~4
:Countdown
cls
echo ============================================
echo         #1Matttttttttt Network Config
echo ============================================
echo.
echo %~1
echo %~3
echo.
echo Making changes, please wait: !sec! seconds...
timeout /t 1 >nul
set /a sec-=1
if !sec! geq 0 goto Countdown
echo.
goto :eof