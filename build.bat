@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

REM Récupérer ESC pour les couleurs
for /F "delims=" %%A in ('echo prompt $E^| cmd') do set "ESC=%%A"

echo ============================================
echo       BUILD COMPLET DE MCEMONITOR
echo ============================================

REM --- Racine du projet ---
set ROOT=Z:\Compilations Programmes\Projet MCEMonitor

REM --- Dossiers de destination ---
set DEST=%ROOT%\MCEMonitor Ver 1.0
set DEST_PROGRAM=%DEST%\ProgramFiles
set DEST_APPDATA=%DEST%\ProgramData
set DEST_TOOLS=%DEST%\Tools\Autotrad

REM --- Sauvegarder les configs dev avant nettoyage ---
set TEMP_AUTOTRAD_CONFIG=%TEMP%\autotrad.config.json

if exist "%DEST_TOOLS%\autotrad.config.json" (
    copy "%DEST_TOOLS%\autotrad.config.json" "%TEMP_AUTOTRAD_CONFIG%" /Y >nul
    echo Config Autotrad sauvegardée temporairement.
)

REM --- Nettoyage complet ---
if exist "%DEST%" rmdir /s /q "%DEST%"
mkdir "%DEST%"
mkdir "%DEST_PROGRAM%"
mkdir "%DEST_APPDATA%"
mkdir "%DEST_TOOLS%"

REM --- Restaurer les configs dev ---
if exist "%TEMP_AUTOTRAD_CONFIG%" (
    copy "%TEMP_AUTOTRAD_CONFIG%" "%DEST_TOOLS%\autotrad.config.json" /Y >nul
    del "%TEMP_AUTOTRAD_CONFIG%" >nul
    echo Config Autotrad restaurée.
)

set ERROR=0

REM --- Arrêter les services en cours (pour éviter les verrous) ---
echo.
echo Arrêt des services en cours...
taskkill /IM MediaMonitor.Service.exe /F >nul 2>&1
taskkill /IM RomMonitor.Service.exe /F >nul 2>&1
taskkill /IM SystemMonitor.Service.exe /F >nul 2>&1

taskkill /IM MediaMonitor.UI.exe /F >nul 2>&1
taskkill /IM RomMonitor.UI.exe /F >nul 2>&1
taskkill /IM SystemMonitor.UI.exe /F >nul 2>&1

taskkill /IM MediaMonitor.Tray.exe /F >nul 2>&1
taskkill /IM RomMonitor.Tray.exe /F >nul 2>&1
taskkill /IM SystemMonitor.Tray.exe /F >nul 2>&1

taskkill /IM MCEMonitor.exe /F >nul 2>&1
taskkill /IM Autotrad.exe /F >nul 2>&1
echo %ESC%[32m[OK] TOUS LES EXECUTABLES SONT ARRETÉS%ESC%[0m

echo.
echo          PUBLISH DES PROJETS
echo ============================================

REM === ProgramFiles ===
call :publish "%ROOT%\MCEMonitor"                            "%DEST_PROGRAM%"
call :publish "%ROOT%\MediaMonitor\MediaMonitor.Core"        "%DEST_PROGRAM%"
call :publish "%ROOT%\MediaMonitor\MediaMonitor.UI"          "%DEST_PROGRAM%"
call :publish "%ROOT%\MediaMonitor\MediaMonitor.Tray"        "%DEST_PROGRAM%"
call :publish "%ROOT%\RomMonitor\RomMonitor.Tray"            "%DEST_PROGRAM%"
call :publish "%ROOT%\RomMonitor\RomMonitor.UI"              "%DEST_PROGRAM%"
call :publish "%ROOT%\SystemMonitor\SystemMonitor.UI"        "%DEST_PROGRAM%"

REM === ProgramData ===
call :publish "%ROOT%\StopMonitor"                           "%DEST_APPDATA%"
call :publish "%ROOT%\WakeMonitor"                           "%DEST_APPDATA%"
call :publish "%ROOT%\MediaMonitor\MediaMonitor.Service"     "%DEST_APPDATA%"
call :publish "%ROOT%\MCEMonitor.Languages"                  "%DEST_APPDATA%"
call :publish "%ROOT%\RomMonitor\RomMonitor.Service"         "%DEST_APPDATA%"
call :publish "%ROOT%\SystemMonitor\SystemMonitor.Service"   "%DEST_APPDATA%"

REM === Tools ===
call :publish_single "%ROOT%\Autotrad"                       "%DEST_TOOLS%"

echo.
echo ============================================

if %ERROR%==0 (
    echo %ESC%[32m[OK] AUCUNE ERREUR DE PUBLISH%ESC%[0m
    echo ============================================
    echo.
    set /p CHOICE="Voulez-vous compiler l'installeur Inno Setup ? (O/N) : "

    if /I "!CHOICE!"=="O" (
        echo.
        echo Vérification de la présence de ISCC.exe...

        set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

        if not exist "!ISCC!" (
            echo %ESC%[31m? ERREUR : ISCC.exe introuvable !%ESC%[0m
            echo Vérifiez l'installation de Inno Setup.
            start "" "%ROOT%\MCEMonitorInstaller.iss"
            goto endFinal
        )

        echo ISCC trouvé. Compilation en cours...
        "!ISCC!" "%ROOT%\MCEMonitorInstaller.iss"

        if errorlevel 1 (
            echo ============================================
            echo %ESC%[31m? Erreur lors de la compilation Inno Setup.%ESC%[0m
            echo ============================================
        ) else (
            echo ============================================
            echo %ESC%[32m[OK] Installeur compilé avec succès !%ESC%[0m
            echo ============================================

            set "INNO_OUT="
            for /f "tokens=1,* delims==" %%A in ('findstr /I "OutputDir" "%ROOT%\MCEMonitorInstaller.iss"') do (
                set "INNO_OUT=%%B"
            )
            set "INNO_OUT=!INNO_OUT:"=!"

            echo.
            echo Dossier de sortie détecté :
            echo !INNO_OUT!

            if exist "!INNO_OUT!" (
                start "" "!INNO_OUT!"
            ) else (
                echo ============================================
                echo %ESC%[31m? Le dossier de sortie n'existe pas !%ESC%[0m
                echo ============================================
            )
        )
    ) else (
        start "" "%ROOT%\MCEMonitorInstaller.iss"
        exit /b 0
    )
) else (
    echo %ESC%[31m? DES ERREURS ONT ETE DETECTEES !%ESC%[0m
    echo Consulte le log ci-dessus.
)

echo ============================================

:endFinal
pause
exit /b 0


REM ============================================================
REM  FONCTION : publish (multi-fichiers, classique)
REM  %1 = dossier du projet (contient le .csproj)
REM  %2 = dossier de destination
REM ============================================================
:publish
setlocal

set "PROJECT_DIR=%~1"
set "DEST_DIR=%~2"
set "CSPROJ="

REM Trouver le .csproj dans le dossier
for %%F in ("!PROJECT_DIR!\*.csproj") do set "CSPROJ=%%F"

if not defined CSPROJ (
    echo %ESC%[31m[ERREUR] Aucun .csproj dans !PROJECT_DIR!%ESC%[0m
    endlocal & set ERROR=1 & exit /b 0
)

echo.
echo ============================================
echo  PUBLISH : %~nx1
echo ============================================
echo Vers    : !DEST_DIR!

dotnet publish "!CSPROJ!" -c Release -o "!DEST_DIR!" --nologo -v q

if errorlevel 1 (
    echo %ESC%[31m[ERREUR] Publish échoué : !CSPROJ!%ESC%[0m
    endlocal & set ERROR=1 & exit /b 0
)

endlocal & exit /b 0


REM ============================================================
REM  FONCTION : publish_single (single-file, pour Autotrad)
REM  %1 = dossier du projet (contient le .csproj)
REM  %2 = dossier de destination
REM ============================================================
:publish_single
setlocal

set "PROJECT_DIR=%~1"
set "DEST_DIR=%~2"
set "CSPROJ="

REM Trouver le .csproj dans le dossier
for %%F in ("!PROJECT_DIR!\*.csproj") do set "CSPROJ=%%F"

if not defined CSPROJ (
    echo %ESC%[31m[ERREUR] Aucun .csproj dans !PROJECT_DIR!%ESC%[0m
    endlocal & set ERROR=1 & exit /b 0
)

echo.
echo ============================================
echo  PUBLISH (single-file) : %~nx1
echo ============================================
echo Vers    : !DEST_DIR!

dotnet publish "!CSPROJ!" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "!DEST_DIR!" --nologo -v q

if errorlevel 1 (
    echo %ESC%[31m[ERREUR] Publish échoué : !CSPROJ!%ESC%[0m
    endlocal & set ERROR=1 & exit /b 0
)

endlocal & exit /b 0