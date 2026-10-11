@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul

REM ============================================================
REM  CleanSolution.bat
REM  Supprime les dossiers bin et obj de la solution courante.
REM  Exclut .git, .vs, .vsold, packages, redist, node_modules.
REM  Usage : CleanSolution.bat [dry]
REM ============================================================

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo.
echo === Nettoyage de : %ROOT% ===
echo.

set "DRY=0"
if /I "%~1"=="dry" set "DRY=1"
if "%DRY%"=="1" (
    echo [MODE SIMULATION] Aucun fichier ne sera supprime.
    echo.
)

set /a COUNT=0

for /f "delims=" %%D in ('dir /s /b /ad "%ROOT%\bin" "%ROOT%\obj" 2^>nul') do (
    call :TryDelete "%%D"
)

echo.
if "%DRY%"=="1" (
    echo === %COUNT% dossier^(s^) seraient supprimes. ===
) else (
    echo === %COUNT% dossier^(s^) supprimes. ===
)
echo.

pause
endlocal
exit /b 0

REM ============================================================
REM  :TryDelete "chemin"
REM ============================================================
:TryDelete
set "TARGET=%~1"

REM --- Exclusions de securite ---
echo !TARGET! | findstr /I /C:".git\"         >nul && (echo [SKIP] !TARGET!  ^(git^)&         goto :eof)
echo !TARGET! | findstr /I /C:".vs\"          >nul && (echo [SKIP] !TARGET!  ^(.vs^)&          goto :eof)
echo !TARGET! | findstr /I /C:".vsold\"       >nul && (echo [SKIP] !TARGET!  ^(.vsold^)&       goto :eof)
echo !TARGET! | findstr /I /C:"\packages\"    >nul && (echo [SKIP] !TARGET!  ^(packages^)&      goto :eof)
echo !TARGET! | findstr /I /C:"\redist\"      >nul && (echo [SKIP] !TARGET!  ^(redist^)&        goto :eof)
echo !TARGET! | findstr /I /C:"\node_modules\" >nul && (echo [SKIP] !TARGET!  ^(node_modules^)& goto :eof)

REM --- Suppression ---
if "%DRY%"=="1" (
    echo [DRY]  Supprimerait : !TARGET!
) else (
    echo [DEL]  !TARGET!
    rmdir /s /q "!TARGET!" 2>nul
)

set /a COUNT+=1
goto :eof