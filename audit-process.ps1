# ============================================================
#  Audit de robustesse des appels aux processus externes
#  - Standard(Output|Error).ReadToEnd() synchrone (deadlock)
#  - WaitForExit() sans timeout (blocage infini)
#  - UseShellExecute = false + Verb = "runas" (UAC silencieux)
#
#  Code retour :
#    0 = OK
#    1 = au moins une violation détectée
# ============================================================

$ErrorActionPreference = 'Stop'

$root    = "Z:\Compilations Programmes\Projet MCEMonitor"
$exclude = '\\(bin|obj|\.vs|packages|node_modules|MCEMonitor Ver 1\.0|\.git)\\'

$files = Get-ChildItem $root -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
         Where-Object { $_.FullName -notmatch $exclude }

if (-not $files) {
    Write-Host "[INFO] Aucun fichier .cs trouve." -ForegroundColor Yellow
    exit 0
}

Write-Host "Audit de $($files.Count) fichiers .cs..." -ForegroundColor Cyan

$violations = 0

function Report-Violation($title, $hits) {
    if (-not $hits) { return 0 }

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host " VIOLATION : $title" -ForegroundColor Red
    Write-Host "============================================================" -ForegroundColor Red

    foreach ($h in $hits) {
        Write-Host ("  {0}:{1}" -f $h.Path, $h.LineNumber) -ForegroundColor Yellow
        Write-Host ("    " + $h.Line.Trim()) -ForegroundColor Gray
    }

    return $hits.Count
}

# ------------------------------------------------------------
#  Regle 1 : ReadToEnd() synchrone sur stdout/stderr
#  (deadlock potentiel : le buffer du pipe se remplit,
#   le process enfant se bloque, ReadToEnd bloque a son tour
#   et WaitForExit(ms) n'est jamais atteint)
# ------------------------------------------------------------
$h1 = $files | Select-String -Pattern 'Standard(Output|Error)\.ReadToEnd\s*\(\s*\)' |
      Where-Object { $_.Line -notmatch '^\s*//' }
$violations += Report-Violation "ReadToEnd() synchrone (deadlock potentiel)" $h1

# ------------------------------------------------------------
#  Regle 2 : WaitForExit() sans argument (blocage infini)
#  Exception : apres BeginOutputReadLine(), un second WaitForExit()
#  sans argument est LEGITIME et OBLIGATOIRE pour vider le buffer
#  d'events OutputDataReceived. On l'exclut via le contexte.
# ------------------------------------------------------------
$h2raw = $files | Select-String -Pattern '\b\w+\.WaitForExit\s*\(\s*\)' -Context 20,0 |
         Where-Object { $_.Line -notmatch '^\s*//' }

$h2 = @()
foreach ($hit in $h2raw) {
    $ctx = ($hit.Context.PreContext -join "`n")
    if ($ctx -notmatch 'BeginOutputReadLine') {
        $h2 += $hit
    }
}
$violations += Report-Violation "WaitForExit() sans argument (hors BeginOutputReadLine legitime)" $h2

# ------------------------------------------------------------
#  Regle 3 : UseShellExecute = false + Verb = "runas"
#  dans le meme bloc new ProcessStartInfo { ... }
#  (UAC silencieusement ignore : le process n'est PAS eleve)
# ------------------------------------------------------------
$h3 = @()
foreach ($f in $files) {
    $content = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }

    # Regex Singleline : . matche aussi les sauts de ligne
    $matches = [regex]::Matches(
        $content,
        '(?s)new\s+ProcessStartInfo\s*\{[^}]*UseShellExecute\s*=\s*false[^}]*Verb\s*=\s*"runas"'
    )

    if ($matches.Count -gt 0) {
        $lineNum = ($content.Substring(0, $matches[0].Index) -split "`n").Count
        $h3 += [pscustomobject]@{
            Path       = $f.FullName
            LineNumber = $lineNum
            Line       = "UseShellExecute = false + Verb = `"runas`" dans le meme ProcessStartInfo"
        }
    }
}
$violations += Report-Violation "UAC casse (UseShellExecute=false + Verb=runas)" $h3

# ------------------------------------------------------------
#  Bilan
# ------------------------------------------------------------
Write-Host ""
if ($violations -eq 0) {
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " AUDIT : OK - aucune violation detectee" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    exit 0
} else {
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host " AUDIT : $violations violation(s) detectee(s)" -ForegroundColor Red
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Corrigez les fichiers ci-dessus avant de relancer le build." -ForegroundColor Yellow
    Write-Host "Motifs interdits :" -ForegroundColor Yellow
    Write-Host "  - StandardOutput.ReadToEnd() / StandardError.ReadToEnd()  ->  ReadToEndAsync()" -ForegroundColor Gray
    Write-Host "  - WaitForExit()                                           ->  WaitForExit(ms)" -ForegroundColor Gray
    Write-Host "  - UseShellExecute=false + Verb=`"runas`"                    ->  UseShellExecute=true" -ForegroundColor Gray
    exit 1
}