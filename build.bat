@echo off
setlocal enabledelayedexpansion

echo ============================================
echo       BUILD COMPLET DE MCEMONITOR

REM --- Racine du projet ---
set ROOT=Z:\Compilations Programmes\Projet MCEMonitor

REM --- Dossier de sortie ---
set OUT="%ROOT%\MCEMonitor Ver 1.0"

REM Netoyage du dossier avant copie
if exist %OUT% rmdir /s /q %OUT%
mkdir %OUT%

set ERROR=0

echo          COMPILATION DES PROJETS
echo ============================================

call :build "%ROOT%\MCEMonitor\MCEMonitor.csproj"
call :build "%ROOT%\MediaMonitor\MediaMonitor.Core\MediaMonitor.Core.csproj"
call :build "%ROOT%\MediaMonitor\MediaMonitor.Service\MediaMonitor.Service.csproj"
call :build "%ROOT%\MediaMonitor\MediaMonitor.UI\MediaMonitor.UI.csproj"
call :build "%ROOT%\MediaMonitor\MediaMonitor.Tray\MediaMonitor.Tray.csproj"
call :build "%ROOT%\StopMonitor\StopMonitor.csproj"
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


goto :end

REM Copier les dossiers détectés
call :CopyFromTxt

echo.
echo Terminé.

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

echo.
echo ============================================
echo        COPIE DU DOSSIER RELEASE
echo ============================================

:CopyFromTxt
echo.

for /f "usebackq delims=" %%P in ("%output%") do (
    echo Copie de : %%P
    xcopy "%%P\*" "%DEST%" /E /I /Y >nul
)

exit /b 0

:build
setlocal

REM Récupérer le caractère ESC
for /F "delims=" %%A in ('echo prompt $E^| cmd') do set "ESC=%%A"
echo.
echo.
echo ============================================
echo              FONCTION : BUILD
echo ============================================
echo Compilation : %1

dotnet build %1 -c Release

if errorlevel 1 (
    echo %ESC%[31mERREUR : La compilation a echoue pour :%ESC%[0m
    echo %1
    set ERROR=1
)


setlocal

REM Récupérer le caractère ESC
for /F "delims=" %%A in ('echo prompt $E^| cmd') do set "ESC=%%A"

echo ========== RESULTAT DE LA BUILD ============

if %ERROR%==0 (
    echo %ESC%[32m       BUILD TERMINE AVEC SUCCES !%ESC%[0m
    echo Les fichiers sont dans :
    echo %OUT%
) else (
    echo DES ERREURS ONT ETE RENCONTREES.
)

exit /b 0

:end
echo.
pause
exit /b 0




