@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul

REM ============================================================
REM  CleanSolution.bat
REM  Supprime les dossiers bin et obj de la solution courante.
REM  Exclut explicitement tout ce qui est dans .git, .vs, .vsold
REM  et tout dossier de packages/redist.
REM ============================================================

set "ROOT=%~dp0"
REM Enlever le backslash final
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo.
echo === Nettoyage de : %ROOT% ===
echo.

REM --- Mode simulation ? ---------------------------------------
REM  Passez "dry" en argument pour voir ce qui serait supprime
REM  sans rien effacer :  CleanSolution.bat dry
set "DRY=0"
if /I "%~1"=="dry" set "DRY=1"
if "%DRY%"=="1" (
    echo [MODE SIMULATION] Aucun fichier ne sera supprime.
    echo.
)

set /a COUNT=0

REM --- Parcours recursif ---------------------------------------
for /d /r "%ROOT%" %%D in (bin obj) do (
    set "TARGET=%%~fD"

    REM --- Exclusions de securite -----------------------------
    echo !TARGET! | findstr /I /C:"\.git\"     >nul && (echo [SKIP] !TARGET!  ^(git^)& goto :next)
    echo !TARGET! | findstr /I /C:"\.git"      >nul && (echo [SKIP] !TARGET!  ^(git^)& goto :next)
    echo !TARGET! | findstr /I /C:"\.vs\"      >nul && (echo [SKIP] !TARGET!  ^(.vs^)& goto :next)
    echo !TARGET! | findstr /I /C:"\.vsold\"   >nul && (echo [SKIP] !TARGET!  ^(.vsold^)& goto :next)
    echo !TARGET! | findstr /I /C:"\packages\" >nul && (echo [SKIP] !TARGET!  ^(packages^)& goto :next)
    echo !TARGET! | findstr /I /C:"\redist\"   >nul && (echo [SKIP] !TARGET!  ^(redist^)& goto :next)
    echo !TARGET! | findstr /I /C:"\node_modules\" >nul && (echo [SKIP] !TARGET!  ^(node_modules^)& goto :next)

    REM --- Suppression ----------------------------------------
    if "%DRY%"=="1" (
        echo [DRY]  Supprimerait : !TARGET!
    ) else (
        echo [DEL]  !TARGET!
        rmdir /s /q "!TARGET!" 2>nul
    )
    set /a COUNT+=1

    :next
)

echo.
if "%DRY%"=="1" (
    echo === %COUNT% dossier^(s^) seraient supprimes. ===
) else (
    echo === %COUNT% dossier^(s^) supprime^(s^). ===
)
echo.

pause
endlocal