@echo off
setlocal enabledelayedexpansion

REM Récupérer ESC pour les couleurs
for /F "delims=" %%A in ('echo prompt $E^| cmd') do set "ESC=%%A"

echo ============================================
echo       BUILD COMPLET DE MCEMONITOR

REM --- Racine du projet ---
set ROOT=Z:\Compilations Programmes\Projet MCEMonitor

REM --- Dossier de sortie ---
set OUT="%ROOT%\MCEMonitor Ver 1.0"

REM Nettoyage du dossier avant copie
if exist %OUT% rmdir /s /q %OUT%
mkdir %OUT%

set ERROR=0

echo          COMPILATION DES PROJETS
echo ============================================

call :clean "%ROOT%\MCEMonitor"
call :build "%ROOT%\MCEMonitor\MCEMonitor.csproj"

call :clean "%ROOT%\MediaMonitor\MediaMonitor.Core"
call :build "%ROOT%\MediaMonitor\MediaMonitor.Core\MediaMonitor.Core.csproj"

call :clean "%ROOT%\MediaMonitor\MediaMonitor.Service"
call :build "%ROOT%\MediaMonitor\MediaMonitor.Service\MediaMonitor.Service.csproj"

call :clean "%ROOT%\MediaMonitor\MediaMonitor.UI"
call :build "%ROOT%\MediaMonitor\MediaMonitor.UI\MediaMonitor.UI.csproj"

call :clean "%ROOT%\MediaMonitor\MediaMonitor.Tray"
call :build "%ROOT%\MediaMonitor\MediaMonitor.Tray\MediaMonitor.Tray.csproj"

call :clean "%ROOT%\StopMonitor"
call :build "%ROOT%\StopMonitor\StopMonitor.csproj"

call :clean "%ROOT%\WakeMonitor"
call :build "%ROOT%\WakeMonitor\WakeMonitor.csproj"

echo ============================================
echo      RECHERCHE DES NOM DE DOSSIERS
echo ============================================

setlocal enabledelayedexpansion

REM Dossier final
set "DEST=MCEMonitor Ver 1.0"
set "DEST_PROGRAM=%DEST%\ProgramFiles"
set "DEST_APPDATA=%DEST%\ProgramData"

mkdir "%DEST_PROGRAM%"
mkdir "%DEST_APPDATA%"

REM Fichier de sortie
set "output=CheminDetecte.txt"

REM Supprimer l'ancien fichier s'il existe
if exist "%output%" del "%output%"

REM Créer le dossier final
if exist "%DEST%" rmdir /s /q "%DEST%"
mkdir "%DEST%"

REM Appeler la fonction pour chaque chemin
call :ProcessOne "MCEMonitor\bin\Release" "%output%" "%DEST_PROGRAM%"
call :ProcessOne "MediaMonitor\MediaMonitor.Core\bin\Release" "%output%" "%DEST_PROGRAM%"
call :ProcessOne "MediaMonitor\MediaMonitor.UI\bin\Release" "%output%" "%DEST_PROGRAM%"
call :ProcessOne "MediaMonitor\MediaMonitor.Tray\bin\Release" "%output%" "%DEST_PROGRAM%"

call :ProcessOne "StopMonitor\bin\Release" "%output%" "%DEST_APPDATA%"
call :ProcessOne "WakeMonitor\bin\Release" "%output%" "%DEST_APPDATA%"
call :ProcessOne "MediaMonitor\MediaMonitor.Service\bin\Release" "%output%" "%DEST_APPDATA%"

goto :finalMessage

:ProcessOne
REM %1 = base, %2 = fichier de sortie, %3 = dossier de destination
set "base=%~1"
set "output=%~2"
set "dest=%~3"
set "NextFolder="

echo.
echo ======================================
echo          COPIE DES FICHIERS
echo ======================================
echo Analyse de : %base%

for /d %%D in ("%base%\*") do (
    set "NextFolder=%%~nxD"
    goto found
)

:found
if not defined NextFolder (
    echo Aucun dossier dans %base%
    echo Aucun dossier dans %base%>> "%output%"
    exit /b 0
)

echo Le Dossier est : !NextFolder!

set "FullPath=%base%\!NextFolder!"

echo Chemin complet : !FullPath!
echo !FullPath!>> "%output%"

echo.
echo Copie vers : %dest%
xcopy "!FullPath!\*" "%dest%" /E /I /Y >nul

exit /b 0

:build
setlocal

REM Récupérer ESC
for /F "delims=" %%A in ('echo prompt $E^| cmd') do set "ESC=%%A"

echo.
echo ============================================
echo              FONCTION : BUILD
echo ============================================
echo Compilation : %1

dotnet build %1 -c Release

if errorlevel 1 (
    echo %ESC%[31mERREUR : La compilation a echoue pour :%ESC%[0m
    echo %1
    endlocal & set ERROR=1 & exit /b 0
)

endlocal & exit /b 0

:finalMessage
echo.
echo ============================================

if %ERROR%==0 (
    echo %ESC%[32m? AUCUNE ERREUR DE COMPILATION%ESC%[0m
    echo ============================================
    echo.
    set /p CHOICE="Voulez-vous compiler l'installeur Inno Setup ? (O/N) : "

    if /I "%CHOICE%"=="O" (
        echo.
        echo Vérification de la présence de ISCC.exe...

        REM --- Chemin par défaut d'Inno Setup ---
        set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

        if not exist "%ISCC%" (
            echo %ESC%[31m? ERREUR : ISCC.exe introuvable !%ESC%[0m
            echo Vérifiez l'installation de Inno Setup.
            echo.
            echo Ouverture du script Inno Setup pour édition...
            start "" "%ROOT%\MCEMonitorInstaller.iss"
            goto endFinal
        )

        echo ISCC trouvé. Compilation en cours...
        "%ISCC%" "%ROOT%\MCEMonitorInstaller.iss"

        if errorlevel 1 (
            echo %ESC%[31m? Erreur lors de la compilation Inno Setup.%ESC%[0m
        ) else (
            echo %ESC%[32m? Installeur compilé avec succès !%ESC%[0m

            REM --- Récupération du OutputDir dans le .iss ---
            set "INNO_OUT="

            for /f "tokens=1,* delims==" %%A in ('findstr /I "OutputDir" "%ROOT%\MCEMonitorInstaller.iss"') do (
                set "INNO_OUT=%%B"
            )

            REM Nettoyage des guillemets éventuels
            set "INNO_OUT=%INNO_OUT:"=%"

            echo.
            echo Dossier de sortie détecté :
            echo %INNO_OUT%

            if exist "%INNO_OUT%" (
                echo Ouverture du dossier de l'installeur...
                start "" "%INNO_OUT%"
            ) else (
                echo %ESC%[31m? Le dossier de sortie n'existe pas !%ESC%[0m
            )
        )

    ) else (
        echo.
        echo Ouverture du script Inno Setup...
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

