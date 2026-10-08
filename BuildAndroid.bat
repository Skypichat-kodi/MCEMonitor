@echo off
setlocal enabledelayedexpansion

REM ============================================
REM  Build APK MCEMonitorClient (Android Release)
REM ============================================

REM --- Configuration ---
set PROJECT_DIR=Z:\Compilations Programmes\Projet MCEMonitor\AndroidClient\MCEMonitorClient.Maui
set DEST_DIR=Z:\Compilations Programmes\Projet MCEMonitor\MCEMonitor Ver 1.0\Android APK
set KEYSTORE=mcemonitor.keystore
set ALIAS=mcemonitor

REM --- Mot de passe du keystore (a remplacer) ---
set /p KEYPASS=Mot de passe keystore :

REM ============================================
REM  Verifications
REM ============================================
if not exist "%PROJECT_DIR%" (
    echo [ERREUR] Dossier projet introuvable :
    echo   %PROJECT_DIR%
    pause
    exit /b 1
)

if not exist "%PROJECT_DIR%\%KEYSTORE%" (
    echo [ERREUR] Keystore introuvable :
    echo   %PROJECT_DIR%\%KEYSTORE%
    echo.
    echo Cree-le avec :
    echo   keytool -genkeypair -v -keystore %KEYSTORE% -alias %ALIAS% -keyalg RSA -keysize 2048 -validity 10000
    pause
    exit /b 1
)

if "%KEYPASS%"=="TON_MOT_DE_PASSE_ICI" (
    echo [ERREUR] Remplace KEYPASS par ton vrai mot de passe keystore dans ce .bat
    pause
    exit /b 1
)

REM ============================================
REM  Compilation
REM ============================================
echo.
echo ============================================
echo   Compilation APK Release Android
echo   Projet : MCEMonitorClient.Maui
echo ============================================
echo.

cd /d "%PROJECT_DIR%"

dotnet publish -f net8.0-android -c Release ^
    -p:AndroidKeyStore=true ^
    -p:AndroidSigningKeyStore=%KEYSTORE% ^
    -p:AndroidSigningKeyAlias=%ALIAS% ^
    -p:AndroidSigningKeyPass=%KEYPASS% ^
    -p:AndroidSigningStorePass=%KEYPASS% ^
    -p:AndroidPackageFormat=apk

if errorlevel 1 (
    echo.
    echo ============================================
    echo   [ERREUR] La compilation a echoue
    echo ============================================
    pause
    exit /b 1
)

REM ============================================
REM  Copie de l'APK
REM ============================================
set APK_SRC=%PROJECT_DIR%\bin\Release\net8.0-android\publish\com.mcemonitor.client-Signed.apk

if not exist "%APK_SRC%" (
    echo.
    echo [ERREUR] APK non trouve :
    echo   %APK_SRC%
    pause
    exit /b 1
)

if not exist "%DEST_DIR%" (
    echo Creation du dossier destination...
    mkdir "%DEST_DIR%"
)

copy /Y "%APK_SRC%" "%DEST_DIR%\MCEMonitorClient.apk" >nul

if errorlevel 1 (
    echo [ERREUR] Copie de l'APK echouee.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   BUILD TERMINE
echo.
echo   APK : %DEST_DIR%\MCEMonitorClient.apk
echo ============================================
echo.

pause
endlocal