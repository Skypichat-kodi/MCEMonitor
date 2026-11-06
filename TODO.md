# TODO MCEMonitor

TODO global du projet, organisé par thème.

---

## 🔧 En cours

_(rien)_

## 📋 À faire

### 🔴 Priorité haute — Validation ToCheck end-to-end

- [ ] **Test complet du workflow ToCheck** sur les 3 plateformes (WPF, Tray Windows, Android)
  - Mode debug : passer `MicrosoftIpRangeEnabled=false` dans `NetMonitor.config`
  - Recharger la config (bouton UI ou restart service)
  - Déclencher une alerte Microsoft-like non vérifiée (ex : `ping -n 100 20.0.0.99`)
  - Vérifier :
    - **WPF** : ligne violette dans tableau 1 + bouton `Safe ?` cliquable
    - **Web** : badge `.tocheck` + bouton `Safe ?` + colonnes Pays/FAI visibles
    - **Tray Windows** : Toast `[À VERIF]` + préambule `« Safe ? »` + 2 bips courts
    - **Android** : notification `"Alerte à vérifier (N)"` + texte `[À VERIF]`
  - Cliquer `Safe ?` (WPF ou Web) → la ligne migre dans l'audit vert
  - Vérifier que les futures alertes sur la même IP sont classées `Safe` (via `_manualSafeIps`)
  - **Remettre `MicrosoftIpRangeEnabled=true`** après le test

- [ ] **Documenter le workflow ToCheck** dans le README du projet
  - Expliquer le pipeline : IP détectée → Microsoft-like ? → JSON officiel ? → Safe / ToCheck
  - Expliquer les 6 cas de la matrice Trust (chemin/hash/publisher)
  - Expliquer le comportement de `Safe ?` et `Bloquer` sur les alertes ToCheck

### 🟡 Priorité moyenne — ToCheck suite (optionnel)

- [ ] **Canal Android `mcem_tocheck` avec son dédié** (confort)
  - Actuellement : ToCheck emprunte le canal `mcem_warning` → son `warning.wav`
  - Créer un canal dédié → son distinct (2 bips courts comme Windows Tray)
  - ⚠️ Nécessite **réinstallation de l'APK** (les canaux sont figés à la création sur Android 8+)
  - Bénéfice : distinction auditive ToCheck vs Warning
  - Coût : 10 min + réinstallation

- [ ] **Persister `_manualCriticalOverrides`** dans `NetMonitorEngine`
  - Actuellement en mémoire uniquement → perdu au redémarrage du service
  - Créer un fichier `manual_critical_overrides.json` (même pattern que `manual_safe_ips.json`)
  - Bénéfice : les overrides manuels survivent aux redémarrages
  - Non critique (rare), mais cohérence avec `_manualSafeIps`

- [ ] **Test end-to-end de la préservation des JSON** à travers désinstallation/réinstallation
  - Désinstaller avec l'option "supprimer les logs"
  - Vérifier que `blocked_ips.json` + `alert_history.json` + `audit_history.json` + `manual_safe_ips.json` + `trusted_processes.json` + `NetMonitor.config` survivent
  - Réinstaller et vérifier que le service recharge les IPs (`Chargement N IP bloquées`)
  - Vérifier que les règles de pare-feu sont reconstruites avec les bonnes IPs

- [ ] **Test end-to-end de la suppression des règles orphelines**
  - Créer 2 règles `NetMonitor-Block-01-In` / `Out` manuellement via `netsh`
  - Redémarrer le service
  - Cliquer "Libérer toutes les IP bloquées"
  - Vérifier que les règles sont supprimées (même avec beaucoup de règles Windows)

### 🟡 Priorité moyenne — Bug latent `GetShutdownTaskTime`

- [ ] **`TaskSchedulerHelper.GetShutdownTaskTime()` — parsing de `schtasks` cassé**
  - Le `Split(':')` sur `"Heure de début : 22:00:00"` produit `["Heure de début ", "00", "00"]` → `parts[^1]` = `"00"` → parse invalide
  - Le `StartsWith("Heure de d")` ne matche pas sur un Windows anglais (`"Start Time"`)
  - Contournement actuel documenté en commentaire (« On reste tolérant ») → à remplacer par un parsing robuste
  - Piste : parser par regex `Heure de début\s*:\s*(\d{1,2}):(\d{2})` (FR) + `Start Time\s*:\s*(\d{1,2}):(\d{2})` (EN)

### 🟡 Priorité moyenne — Chantiers identifiés

- [ ] **`MediaMonitorLiveForm` — fire-and-forget timer**
  - `_timer.Tick += UpdateStatus` lance une lecture `_media.GetLiveStatus()` à chaque seconde sans protection si `_media` est disposé
  - Ajouter un `_closingInProgress` / check `IsDisposed` (pattern déjà utilisé dans `MainForm`)

- [ ] **`ConfigManager.Save<T>` — créer le dossier si absent**
  - `File.WriteAllText(path, …)` échoue en `DirectoryNotFoundException` si `AppData.BasePath` n'existe pas
  - Actuellement garanti par `AppData.Initialize()` en amont, mais fragile si l'ordre change
  - Fix 1 ligne : `Directory.CreateDirectory(AppData.BasePath);` avant l'écriture

### 🟢 Priorité basse — MediaMonitor vignettes album

- [x] CoverFinder : folder.jpg, cover.jpg, poster.jpg, {nom_fichier}.jpg, {nom_dossier}.jpg
- [ ] Tester avec MP3 sans ID3 + folder.jpg
- [ ] Tester avec vidéo codec KO + poster.jpg
- [ ] (optionnel) Vérification taille minimale cover (< 100×100 px)
- [ ] (optionnel) Support WebP

## ✅ Terminé

### Session 2026-11-06 — Refactoring configs Web (séparation + uniformisation)

**Séparation des paramètres Web de RomMonitor et SystemMonitor dans des fichiers `*.web.config` dédiés. Prépare la protection en lecture seule des configs Web dans une prochaine étape.**

**RomMonitor + SystemMonitor — séparation `*.web.config`**
- [x] `RomMonitorSettings.cs` : refonte complète
  - `ConfigPath` = `RomMonitor.config` (config principale)
  - `WebConfigPath` = `RomMonitor.web.config` (config Web)
  - `Load()` : lit les 2 fichiers, migration one-shot si besoin
  - `Save()` : n'écrit plus que les paramètres non-Web
  - `SaveWeb()` : nouvelle méthode, écrit uniquement `*.web.config`
  - `Reload()` : recharge les 2 fichiers
  - `MigrateWebConfigIfNeeded()` : extrait les clés Web du `.config` existant vers `.web.config` et nettoie le `.config` (one-shot, idempotent)
- [x] `SystemMonitorSettings.cs` : idem (symétrique)
- [x] `Program.cs` (RomMonitor + SystemMonitor) : +1 log de debug du port Web
- [x] `IpcCommandHandler.cs` (RomMonitor + SystemMonitor) : `_settings.Save()` → `_settings.SaveWeb()` dans `HandleSetWebConfig`
- [x] Correction `CoreLog.Info/Warn` → `CoreLog.Write` dans les 2 `Settings.cs` (ces méthodes n'existent pas dans RomMonitor/SystemMonitor)

**Uniformisation de la casse des `*.web.config`**
- [x] `MediaMonitor.Web.config` → `MediaMonitor.web.config` dans le code
- [x] `NetMonitor.Web.config` → `NetMonitor.web.config` dans le code
- [x] Aucune migration nécessaire (Windows NTFS insensible à la casse)
- [x] Aucun renommage physique des fichiers sur disque
- [x] `MCEMonitorInstaller.iss` mis à jour (`ConfigFiles` + `[Files]`)

**Installeur Inno Setup**
- [x] `ConfigFiles` étendu pour préserver les `*.web.config` à travers désinstallation/réinstallation :
  - `+MediaMonitor.web.config`
  - `+NetMonitor.web.config`
  - `+RomMonitor.web.config`
  - `+SystemMonitor.web.config`
- [x] `[Files]` : ajout de `RomMonitor.web.config` et `SystemMonitor.web.config` en `onlyifdoesntexist`
- [x] Correction : `AppVersion` en double dans `[Setup]` (erreur `already specified`)
- [x] Correction : `ConfigFiles` avec 2 bugs de ponctuation (virgule manquante + double virgule)
- [x] Ajout de la garde `#error MyAppVersion n'est pas défini` (protège contre `iscc.exe` lancé directement)

**Migration testée** :
- Config existante avec `WebPort=8085` → migré en 1 démarrage
- Log : `[MIGRATION] OK : 4 clé(s) Web déplacée(s)`
- Config principale nettoyée (plus de clés Web)
- Web config contient les 4 clés avec valeurs préservées

**Résultat** : build 0 warning, 0 erreur. Tous les services ont maintenant une structure cohérente `{ServiceName}.config` + `{ServiceName}.web.config`.

### Session 2026-11-06 — Robustesse Firewall + Installateur + JSON

**Trois corrections pour la robustesse du service et la préservation des données.**

**Firewall**
- [x] `DeleteAllBlockRules()` réécrite pour utiliser PowerShell (`Get-NetFirewallRule -DisplayName 'NetMonitor-Block-*'`) au lieu de parser `netsh advfirewall show rule name=all`
  - Cause : `netsh` tronque sa sortie à 4096 caractères, ce qui fait rater les règles `NetMonitor-Block-*` quand il y a beaucoup de règles Windows (antivirus, GPO, Hyper-V…)
  - Résultat : les règles orphelines persistaient après "Libérer toutes les IP bloquées"
- [x] Fallback `DeleteAllBlockRulesNetsh()` conservé si PowerShell indisponible
- [x] Méthode rendue `public` pour être appelée explicitement dans `ClearAllBlockedIps()` avant `SyncFirewallRules()`
- [x] Fix compilation `CS0117` : `DeleteAllBlockRulesPublic()` → `DeleteAllBlockRules()` dans `NetMonitorEngine.ClearAllBlockedIps`

**Création des JSON vides au démarrage**
- [x] `LoadBlockedIps()` / `LoadHistory()` / `LoadAudit()` / `LoadManualSafeIps()` appellent leur `Save*()` correspondant si le fichier est absent
  - Matérialise l'état "0 IP bloquée / 0 alerte" immédiatement après le démarrage
  - Fichiers créés : `blocked_ips.json` (`[]`), `alert_history.json` (`[]`), `audit_history.json` (`[]`), `manual_safe_ips.json` (`{}`)

**Installateur Inno Setup**
- [x] `ConfigFiles` étendu pour préserver les données NetMonitor à travers désinstallation/réinstallation :
  - `+NetMonitor.config` (était absent !)
  - `+NetMonitor\blocked_ips.json`
  - `+NetMonitor\alert_history.json`
  - `+NetMonitor\audit_history.json`
  - `+NetMonitor\manual_safe_ips.json`
  - `+NetMonitor\trusted_processes.json`
  - `+NetMonitor\blocked_ip_retries.json`
  - `+NetMonitor\blacklist.txt`
  - `+NetMonitor\blacklist_auto.txt`
- [x] `ForceDirectories(ExtractFileDir(DestFile))` ajouté dans les 2 boucles (sauvegarde ET restauration) : indispensable car `CopyFile` ne crée pas les dossiers parents
- [x] Corrige la perte des IPs bloquées et du `NetMonitor.config` à chaque désinstallation
- [x] `ms-ip-ranges.json` volontairement NON préservé (4.5 Mo, re-téléchargé au démarrage)

**Résultat** : build 0 warning, 0 erreur (hors NU1701 déjà ignoré).

### Session 2026-11-04 (suite) — Stats : 4 niveaux de lecture + Top FAI

**Architecture des Statistiques clarifiée en 4 couches distinctes.**

- [x] **4 niveaux de lecture** séparés sémantiquement :
  - Carte 1 mode auto : activité des **24h glissantes** (vue temporelle)
  - Carte 1 mode manuel : activité **réelle** (IPs actives en attente)
  - Carte 2 : vue **"depuis le début"** (toutes IPs bloquées du pare-feu)
  - Onglet Interceptions : **récidives** (botnets persistants)
- [x] **`GlobalStats.AutoBlockMode`** propagé de `NetMonitorEngine` → `StatsBuilder` → IPC → UI/Web
- [x] **`StatsBuilder.BuildAsync`** : filtre `BlockedAt >= now - 24h` en mode auto
- [x] **Top IPs → Top FAI par pays** sur les 3 tableaux concernés :
  - Nouvelle classe `IspStats` (Isp + Country + CountryCode + Count)
  - Groupement par couple `(Isp, Country)` sur `_blockedIps`
  - Carte 1 manuel : "Top FAI par pays" (colonne Alertes)
  - Carte 1 auto : "Top FAI par pays (24h)" (colonne IPs bloquées)
  - Carte 2 : "Top FAI par pays (depuis le début)" (colonne IPs bloquées)
- [x] **Titres adaptatifs UI** : 8 `x:Name` sur les TextBlock de la carte 1 + bloc `if (stats.AutoBlockMode)` dans `RefreshStatsAsync`
- [x] **Titres adaptatifs Web** : 10 variables `{{ActiveSectionTitle}}`, `{{ActiveTotalLabel}}`… dans `WebHandler`
- [x] **Charts `chartHours` et `chartDays`** : `type: 'bar'` → `type: 'line'` avec `tension: 0.4` + `fill: true` (courbes lissées)
- [x] **Confirmé** : les charts utilisent les **alertes** (vue réseau), pas les IPs bloquées
- [x] **Confirmé** : la carte 2 utilise **toutes** les IPs bloquées du pare-feu
- [x] **Hydra Communications Ltd** identifié comme FAI #1 des attaques → validation du système

### Session 2026-11-04 — `ProcessTrustService` auto-update (Steam/NVIDIA)

**Refonte de la logique de confiance pour éliminer les faux positifs sur binaires auto-updatés.**

- [x] **Auto-update du hash après MAJ légitime** — chemin identique + hash changé + signature éditeur OK → mise à jour silencieuse du hash en base au lieu de `Spoofed`
- [x] **Gestion du changement de répertoire** — chemin différent + nom whitelisté + éditeur compatible → `Unknown` (contrôle manuel) au lieu de `Spoofed`
- [x] **`AddToWhitelist` : update au lieu de doublon** — si même nom + même éditeur, mise à jour du chemin/hash au lieu de créer une 2ᵉ entrée
- [x] **Audit auto-trust** — fichier `auto_trust_audit.log` séparé
- [x] **Fix warning `CS8600`** — `string? entryPubName` dans le CAS B
- [x] **Réalignement complet du fichier** — indentation cohérente, méthodes utilitaires regroupées
- [x] Résultat : build **0 warning**

### Session 2026-11-03 — ToCheck + vérification Microsoft IP ranges

**Introduction d'un niveau `ToCheck` entre `Safe` et `Warning` pour les IPs Microsoft-like absentes du JSON officiel. Workflow de décision manuelle `Safe ?` / `Bloquer`.**

**Backend**
- [x] `Severity.ToCheck = 4` ajouté entre `Info` et `Warning`
- [x] `MicrosoftIpRangeService` — téléchargement quotidien du JSON Azure/O365
- [x] `FirewallHelper` : séparation `MicrosoftNetworks` / `OtherCriticalNetworks` + `IsMicrosoftLike()`
- [x] `NetMonitorEngine.AddIntrusion` : pipeline 3 étapes
- [x] `NetMonitorEngine.MarkSafe()` : migration `ToCheck` → `Safe` + persistance `manual_safe_ips.json`
- [x] `NetMonitorEngine.BlockIp(forceManual)` : bypass `IsCriticalIp` sur action humaine
- [x] `IpcCommandHandler` : commande `mark-safe` + `forceManual: true` sur `block-ip`
- [x] `MiniHttpServer` : route `/mark-safe`

**UI WPF**
- [x] `SeverityBadgeTemplate` : `DataTrigger ToCheck` violet `#A855F7`
- [x] Bouton `Safe ?` dans `ActiveIntrusionsGrid`
- [x] Suppression du bouton ⚙ du tableau « Alertes ignorées »
- [x] Colonnes Pays/FAI sur `ActiveIntrusionsGrid`, `AlertsHistoryGrid`, `AuditGrid`
- [x] `SeverityRank` étendu (`ToCheck = 2`)
- [x] `MarkSafeAsync` remplace `SetAuditSeverityAsync`

**Web**
- [x] CSS `.badge.tocheck` + `.safe-btn`
- [x] JS `markSafe(ip)` + suppression `changeAuditSeverity`
- [x] Bouton `Safe ?` dans le tableau 1
- [x] Colonnes Pays/FAI sur tableaux 1 / 3 / 4

**Clients Windows (Tray)**
- [x] `PushAlert.NewProblems` ajouté
- [x] Branche `NewProblems` dans `ShowNotification`
- [x] Priorité `tocheck.png` dans `GetAlertIcon`
- [x] Préambule `[À VERIF]` + guidance `« Safe ? »`
- [x] `PlayToCheck()` : 2 bips courts

**Clients Android**
- [x] `ShowAlert` : titre `"Alerte à vérifier (N)"`
- [x] `BuildAlertMessage` : préambule + préfixe `[À VERIF]`
- [x] `GetAlertIconName` : priorité `tocheck`
- [x] Alignement avec Windows sur `NewProblems`, `ResolvedProblems`, hystérésis offline, recovery threshold

**Icônes**
- [x] `tocheck.png` créé (Tray Windows + Android)

### Session 2026-11-03 — Cosmétiques UI/Web

- [x] Pays + FAI sur « Tentatives sur IP bloquée »
- [x] Historique Web : détails sur 1 ligne, largeurs Sévérité/Détecteur ajustées
- [x] IP bloquées Web : limite 50 → 500 lignes
- [x] 5 `colspan` erronés corrigés
- [x] Désalignements `<th>` vs `<td>` corrigés

### Session 2026-11-01 (suite) — Chasse aux warnings NULL + nettoyage build

- [x] **Ré-encodage global du projet en UTF-8 (sans BOM)** — 15 fichiers `.cs` corrigés
- [x] **Vérifier `MCEMonitor.Languages.csproj`** — migration SDK + `<UseWPF>true</UseWPF>`
- [x] **Tests de fumée post-refacto nullabilité** — 5 tests validés
- [x] **55 warnings CS86xx éliminés** sur 15 fichiers
- [x] **NETSDK1137** : `Microsoft.NET.Sdk.WindowsDesktop` → `Microsoft.NET.Sdk`
- [x] **CS1998 ×2** : retrait `async` inutile
- [x] **Corrections de mojibake** : `Réseau`, `°C`, accents

**Résultat** : build 100% propre (0 warning, 0 erreur).

### Session 2026-11-01 — Fix Processus Fantômes & Nettoyage Logs

- [x] Build/Installeur : version dynamique via GitVersion
- [x] UI (À propos) : version dynamique
- [x] `ProcessTrustService` : capture `Win32Error`
- [x] `IsSignedBy` : `X509Certificate.CreateFromSignedFile`
- [x] `UntrustedProcessDetector` : filtrage par `Win32Error`
- [x] Gestion race conditions
- [x] Logs NetMonitor/RomMonitor en `Debug`
- [x] `IgnoreSearchScanners` + `ScannerPatterns` éditables
- [x] Blocage automatique granulaire
- [x] `SeverityScorer` refonte

### Session 2026-10-31 (Suite) — Contrôle des blocages & Tentatives

- [x] Web : bouton "Retirer de la liste" pending
- [x] UI & Web : colonne Sévérité + bouton reclassement dans Tentatives
- [x] Service : jointure IPC pour retries
- [x] UI WPF : Zone de danger + "Libérer toutes les IP bloquées"
- [x] Service : `ClearAllBlockedIps()`
- [x] UI : 🎨 → ⚙

### Session 2026-10-31 — Scanners "Subnet", GeoIP & Fix UI Web

- [x] Détection scanners via Org/Isp GeoIP
- [x] `IsKnownScanner` sur `IntrusionEvent`, `BlockedIpInfo`, `AuditGroupedEntry`
- [x] Badge vert "Subnet" (UI WPF + Web)
- [x] Case "Bloquer les scanners" + `ScannerPatterns`
- [x] Migration `LoadBlockedIps`
- [x] Web : colonnes réorganisées (Compteur, Tentatives)
- [x] Web : badges verts audit
- [x] Web : fix largeur lignes
- [x] Web : fix conflit `.info` / `.badge.info`
- [x] Web : centrage badges via `td:has(.badge)`

### Session 2026-10-30 (soir) — Refonte de la sévérité

- [x] `SeverityScorer` créé
- [x] Taxonomie 5 niveaux
- [x] Config patterns (Critical/Scanner/Safe/Whitelist)
- [x] Intégration `AddIntrusion`, `BlockIp`, `LoadBlockedIps`
- [x] Migration one-shot `SeverityVersion 0 → 2`
- [x] `ManualSeverity` + `ManualSeveritySetAt`
- [x] `JsonFileOpts` avec `JsonStringEnumConverter`
- [x] `SetManualSeverity(ip, severity?)`
- [x] IPC `set-manual-severity`
- [x] `SeverityPickerWindow`
- [x] Bouton 🎨 + cadenas 🔒 + tooltip
- [x] `SeverityBadgeTemplate` 5 niveaux
- [x] Stubs `HasManualSeverity=false`
- [x] CSS badges + `.manual-lock` + `.severity-btn`
- [x] `SeverityToCssClass` (7 valeurs)
- [x] Route POST `/set-severity` + prompt natif
- [x] Bouton ⚙ + cadenas
- [x] Colonne Info (badge `i` AbuseIPDB)
- [x] `StatsBuilder` distribution 7 niveaux
- [x] `WorstSeverity` prend en compte `Warning`
- [x] Fix badge `Info` gris → bleu
- [x] Fix Historique Web (`table-layout: fixed`)

### Session 2026-10-30 (matin)

- [x] CoverFinder
- [x] Nettoyage debug `HardwareMonitorService`
- [x] Compactage « Activité par heure »
- [x] Badges compteurs dégradés
- [x] Tri interceptions par count
- [x] Boutons nettoyage par tableau
- [x] Persistance GeoIP au blocage

### Sessions précédentes

- [x] Fix tableau « IP de confiance »
- [x] Retry WinDivert + popup
- [x] Refonte Statistiques (2 sections)