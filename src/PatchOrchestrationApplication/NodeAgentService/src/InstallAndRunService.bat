@echo off
setlocal enabledelayedexpansion

set rootDir=%SystemDrive%
set oldrootDir=D:
set currentDir=%cd%
set oldApplicationDir=%oldrootDir%\PatchOrchestrationApplication
set applicationDir=%rootDir%\PatchOrchestrationApplication
set workingDir=%applicationDir%\NodeAgentNTService
set logsDir=%applicationDir%\logs
set serviceExe=%workingDir%\NodeAgentNTService.exe
set serviceName=POSNodeSvc

echo %workingDir%
echo %currentDir%

IF NOT EXIST %applicationDir% (
mkdir %applicationDir%
)

IF NOT EXIST %workingDir% (
mkdir %workingDir%
)

IF NOT EXIST %logsDir% (
mkdir %logsDir%
)

logman -ets FabricTraces | findstr /i "24afa313-0d3b-4c7c-b485-1047fd964b60" > nul
if %errorlevel% == 0 (
    echo "POA provider for coordinator service found in FabricTraces"
    logman stop PatchOrchestrationServiceTraces
    echo "Stopped trace session PatchOrchestrationServiceTraces"
    logman delete PatchOrchestrationServiceTraces
    echo "Deleting local traces"
    rmdir /s /q %logsDir%
) else (
    echo "POA provider not found in FabricTraces. Starting local trace session PatchOrchestrationServiceTraces"
    REM Create tracing session for dumping logs locally
    logman stop PatchOrchestrationServiceTraces
    echo "Stopped trace session PatchOrchestrationServiceTraces"
    logman delete PatchOrchestrationServiceTraces
    logman create trace PatchOrchestrationServiceTraces -pf logmansessions.cfg -o %logsDir%\PatchOrchestrationServiceTraces.etl -v mmddhhmm -bs 64 -max 100
    logman start PatchOrchestrationServiceTraces
    if !errorlevel! neq 0 exit /b !errorlevel!
)

REM Grant access to logs dir so that NodeAgentService can do cleanup
icacls %logsDir% /grant "Network Service":(OI)(CI)F /T
if %errorlevel% neq 0 exit /b %errorlevel%

REM Enable below line if we're not able to copy Settings.xml from NodeAgentService
REM icacls %workingDir% /grant "Network Service":(OI)(CI)F /T

REM Stop the service and uninstall the current version
sc stop %serviceName%

REM POSNodeSvc stucks sometimes while stopping. This will unblock that.
taskkill /IM "NodeAgentNTService.exe" /F
sc delete %serviceName%

REM Cleanup the %workingDir% for all predecided folders.

copy "%currentDir%\*.*" "%workingDir%"

if EXIST "%oldApplicationDir%\NodeAgentNTService\Data" (
    ECHO "Coming here"
    if NOT EXIST "%applicationDir%\NodeAgentNTService\Data" (
         mkdir "%applicationDir%\NodeAgentNTService\Data"
    )   
    copy %oldApplicationDir%\NodeAgentNTService\Data %applicationDir%\NodeAgentNTService\Data
    rmdir "%oldApplicationDir%"
)
echo "Installing %serviceName%"
sc create %serviceName% binPath= "%serviceExe% %Fabric_NodeName%  %Fabric_ApplicationName%" start= delayed-auto
sc failure %serviceName% reset= 14400 actions= restart/10000
if %errorlevel% neq 0 exit /b %errorlevel%

echo "trying to start %serviceName%"
sc start %serviceName%
if %errorlevel% neq 0 exit /b %errorlevel%