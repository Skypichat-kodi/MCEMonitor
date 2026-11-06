# TODO MCEMonitor

TODO global du projet, organisé par thème.

---

## 🔧 En cours

_(rien)_

## 📋 À faire

### 🔴 Sécurité — vérification unique

- [ ] **Confirmer que `mcemonitor.keystore` n'est pas versionné**
  - `git ls-files | Select-String "keystore"` → doit renvoyer 0
  - Si 1 résultat : `git rm --cached`, ajouter `*.keystore` au `.gitignore`, régénérer le keystore (compromis)
  - **Déjà fait selon votre retour — vérification one-shot pour être 100% sûr**

### 🟠 Réactif — déclenché par un événement

> Ces tâches ne sont pas urgentes en soi, mais **doivent être prêtes à être exécutées** dès que l'événement déclencheur se produit.

- [ ] **Test complet du workflow ToCheck** sur les 3 plateformes (WPF, Tray Windows, Android)
  - **Déclencheur** : première attaque Microsoft-like non vérifiée en production, OU décision volontaire de forcer le test
  - **Pré-requis** : `MicrosoftIpRangeEnabled=false` dans `NetMonitor.config`
  - **Procédure** :
    - Recharger la config (bouton UI ou restart service)
    - Déclencher une alerte Microsoft-like non vérifiée (ex : `ping -n 100 20.0.0.99`)
    - Vérifier WPF / Web / Tray Windows / Android (cf. détails dans la version précédente)
    - Cliquer `Safe ?` → migration vers audit vert
    - Vérifier classification des futures alertes sur la même IP
  - **Post-requis** : **remettre `MicrosoftIpRangeEnabled=true`**
  - **Coût** : 15-20 min
  - **Risque si non fait** : un bug ToCheck est découvert en production au pire moment

- [ ] **Test end-to-end de la suppression des règles orphelines**
  - **Déclencheur** : après une attaque massive (50+ IPs bloquées d'un coup), OU après un redémarrage du service qui n'a pas nettoyé les règles
  - **Procédure** : créer 2 règles `NetMonitor-Block-01-In/Out` via `netsh`, redémarrer le service, cliquer "Libérer toutes les IP bloquées", vérifier la suppression
  - **Coût** : 10 min

- [ ] **Test end-to-end de la préservation des JSON** à travers désinstallation/réinstallation
  - **Déclencheur** : avant une mise à jour majeure, OU après modification de `MCEMonitorInstaller.iss`
  - **Procédure** : désinstaller, vérifier que les 6 JSON + `NetMonitor.config` + 4 `*.web.config` survivent, réinstaller, vérifier rechargement
  - **Coût** : 20-30 min

### 🟡 Planifié

- [ ] **Vérifier les `ProjectReference` vers `MCEMonitor.Languages`** dans les 3 UI
  - `NetMonitor.UI`, `RomMonitor.UI`, `SystemMonitor.UI`
  - Vérifier `Ctrl+Shift+F` sur `LanguageManager` / `TrExtension` dans ces 3 projets
  - Si utilisés sans `<ProjectReference>` : ajouter la référence
  - **Priorité haute fonctionnelle** — impact direct sur les traductions

- [ ] **Documenter le workflow ToCheck** dans le README
  - Pipeline : IP détectée → Microsoft-like ? → JSON officiel ? → Safe / ToCheck
  - Matrice Trust (chemin/hash/publisher, 6 cas)
  - Comportement de `Safe ?` et `Bloquer` sur les alertes ToCheck

- [ ] **Persister `_manualCriticalOverrides`** dans `NetMonitorEngine`
  - Fichier `manual_critical_overrides.json` (même pattern que `manual_safe_ips.json`)
  - Bénéfice : survit aux redémarrages
  - Non critique (rare), mais cohérence avec `_manualSafeIps`

- [ ] **Canal Android `mcem_tocheck` dédié** (confort)
  - Son distinct (2 bips courts comme Windows Tray)
  - ⚠️ Nécessite réinstallation de l'APK
  - **Quand** : prochaine réinstallation APK prévue

### 🟢 Opportuniste

- [ ] **Nettoyer les dossiers vides**
  - `MCEMonitor\Controls\`
  - `MCEMonitor\Resources\Images\`
  - `NetMonitor\NetMonitor.Service\Api\`
  - `RomMonitor\RomMonitor.UI\Converters\`

- [ ] **Vérifier `MainWindow.xaml.txt`** dans `MediaMonitor.UI`
  - Fichier `.txt` orphelin → supprimer si non référencé

- [ ] **Vérifier les fichiers template MAUI**
  - `AndroidClient\MCEMonitorClient.Maui\Resources\Images\dotnet_bot.png`
  - `AndroidClient\MCEMonitorClient.Maui\Resources\Raw\AboutAssets.txt`
  - Supprimer si non référencés

- [ ] **Choisir entre `LICENSE` et `LICENSE.txt`**
  - Vérifier s'ils sont identiques
  - Garder `LICENSE` (convention), supprimer l'autre

- [ ] **Vérifier `MCEMonitor.png` à la racine**
  - Référencé dans `README.md` ? Si non : supprimer (doublon dans `WakeMonitor\Assets\`)

- [ ] **Auditer les ressources `MediaMonitor.Service\Resources\`**
  - `Icons\*.png` (media, rom, tv, webicon_*.png) — référencés dans les handlers ?
  - `Images\*` (banner-logo, default-cover, logo, backgrounds) — idem

- [ ] **Auditer les DLL de `ffmpeg\`**
  - Vérifier avec `dumpbin /dependents ffmpeg.exe` que toutes les DLL sont chargées
  - Supprimer celles qui ne le sont jamais

- [ ] **Re-tester 5-6 MP3 variés** après le fix vignettes (validation croisée)
  - APIC JPEG valide / APIC PNG / APIC corrompu / sans APIC + folder.jpg / sans rien / APIC + folder.jpg

- [ ] **Centraliser les ressources** dans `MCEMonitor.Shared` (gros chantier)
  - ~15 icônes × 10 projets, 5 `.ico` × 3 copies, 2 `.wav` × 3 projets
  - Optionnel — gain de maintenance à long terme

- [ ] **`Directory.Build.props` à la racine**
  - Factoriser `<TargetFramework>`, `<Nullable>`, `<ImplicitUsings>` pour les 23 projets
  - Optionnel — gain de lisibilité

---

## ✅ Terminé

### Session 2026-11-06 (suite 2) — Optimisation ffmpeg (-50 Mo)

**Suppression de la duplication de ffmpeg entre `ProgramFiles\` et `ProgramData\`.**

**Diagnostic**
- `FileAnalyzer` (seul consommateur de ffmpeg) est appelé **uniquement** par :
  - `MediaMonitor.Service\Ipc\IpcCommandHandler.cs` (ligne 421)
  - `MediaMonitor.Service\Web\Handlers\InfoHandler.cs` (lignes 43, 50, 60)
- Aucun appel dans `MediaMonitor.UI`, `MediaMonitor.Tray`, `MediaMonitor.Core` (hors déf.)
- ffmpeg était copié 2 fois (via `MediaMonitor.Core` dans `ProgramFiles\` et via `MediaMonitor.Service` dans `ProgramData\`)

**Correction (2 fichiers .csproj uniquement)**
- [x] `MediaMonitor.Core.csproj` : retiré `<Content Include="ffmpeg\**\*">`
- [x] `MediaMonitor.Service.csproj` : ajouté `<Content Include="..\MediaMonitor.Core\ffmpeg\**\*" Link="ffmpeg\...">`
- [x] **Aucun changement** dans `build.bat` (le publish transitif de `MediaMonitor.Core.dll` continue de fonctionner)
- [x] **Aucun changement** dans `FileAnalyzer.cs` (`AppContext.BaseDirectory` reste correct)

**Vérifications validées**
- [x] `ProgramData\ffmpeg\` existe
- [x] `ProgramFiles\ffmpeg\` n'existe plus
- [x] `ProgramFiles\MediaMonitor.Core.dll` existe toujours (publish transitif)
- [x] Build 0 warning, 0 erreur
- [x] Installeur **-50 Mo**
- [x] Miniatures UI (popups média) fonctionnent
- [x] Miniatures Web (tuiles média) fonctionnent
- [x] Popup DVBViewer (logo chaîne) fonctionne

**Résultat** : ~50 Mo économisés dans l'installeur et sur le disque utilisateur, sans aucune régression.

### Session 2026-11-06 (suite) — Nettoyage dépendances mortes & fix vignettes MP3

**Audit des packages NuGet, du code Interop, et correction du fallback de vignettes audio.**

#### Nettoyage `MediaMonitor.Core`

- [x] **Audit `Interop/WMF.cs`** → supprimé
  - Code mort (aucun `using MediaMonitor.Core.Interop`, aucun type `WMF`/`IMFSourceReader` appelé)
  - GUID `IMFSourceReader` invalide + signatures P/Invoke incorrectes → fichier jamais testé ni fonctionnel
- [x] **Audit `Microsoft-WindowsAPICodePack-Core` 1.1.5** → supprimé (aucun `using` dans toute la solution)
- [x] **Audit `Microsoft-WindowsAPICodePack-Shell` 1.1.5** → supprimé (aucun `using` dans toute la solution)
- [x] **Audit `System.Drawing.Common` 8.0.0** → supprimé (build OK sans lui → réellement inutilisé)
- [x] **Audit `System.Management` 8.0.0** → **conservé** (utilisé par `SmbSessions.cs` / `SmbOpenFiles.cs`)
- [x] **Fix commentaire trompeur** `<!-- Ancien package supprimé -->` dans `MediaMonitor.Core.csproj`
  - Le commentaire était au-dessus des `PackageReference`, il ne les commentait pas
- [x] **`MediaMonitor.Core.csproj` nettoyé** : 4 packages → 2 (`MailKit` + `System.Management`)
- [x] Dossier `Interop/` supprimé (vide après retrait de `WMF.cs`)
- [x] Build 0 warning, 0 erreur. Publish OK. Aucune DLL APICodePack/Drawing dans la sortie.

#### Fix vignettes MP3

- [x] **`CleanImageData()`** : détection du vrai début JPEG (`FF D8`) ou PNG (`89 50 4E 47`)
- [x] **`IsValidImage()`** : validation JPEG, PNG, GIF, BMP, WebP
- [x] **Fallback fiable** : si TagLibSharp plante ou renvoie une image invalide → `CoverFinder.FindExternalCover()`
- [x] Testé avec MP3 sans ID3 + folder.jpg, MP3 avec APIC corrompu, vidéo codec KO + poster.jpg
