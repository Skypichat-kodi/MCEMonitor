@echo off
title Build Autotrad

echo ============================================
echo   Compilation Autotrad en mode Release
echo ============================================
echo.

REM 1. Nettoyage
dotnet clean "Autotrad\Autotrad.csproj" -c Release

REM 2. Publication en single-file (léger, sans runtime)
dotnet publish "Autotrad\Autotrad.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

echo.
echo ============================================
echo   Copie de l'exécutable dans le dossier Tools
echo ============================================
echo.

REM 3. Dossier source (où dotnet publish génère l'exe)
set SOURCE=Autotrad\bin\Release\net8.0\win-x64\publish

REM 4. Dossier destination
set DEST="Z:\Compilations Programmes\Projet MCEMonitor\MCEMonitor Ver 1.0\Tools\Autotrad"

REM 5. Création du dossier si nécessaire
if not exist %DEST% (
    echo Création du dossier %DEST%
    mkdir %DEST%
)

REM 6. Copie de l'exécutable
copy "%SOURCE%\Autotrad.exe" %DEST% /Y

echo.
echo ============================================
echo   Compilation terminée !
echo   Fichier copié dans :
echo   %DEST%
echo ============================================
echo.

pause
