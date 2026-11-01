﻿﻿﻿# TODO MCEMonitor

TODO global du projet, organisé par thème.

---

## 🔧 En cours

_(rien)_

## 📋 À faire

### 🔴 Priorité haute — Qualité du build & encodage

- [ ] **Ré-encodage global du projet en UTF-8 (sans BOM)**
  - Mojibake systémique dans les 15 fichiers `.cs` (`thčme`, `déją`, `Rťseau`, `parallčle`…)
  - Littéraux affichés à l'utilisateur concernés : `MediaMonitorLiveForm`, `ThemeSelectorForm`, `FormWolMacManager`, `MailConfigPage`, `MainForm`
  - ⚠️ Impact critique sur les `.json` de `Languages/` : les clés en mojibake ne matchent pas les `LanguageManager.Get("…")` → fallback systématique
  - Procédure : VS → Outils → Options → Environnement → Documents → UTF-8 sans signature, puis Ctrl+S sur chaque fichier
  - Vérification post-correction : `Réseau`, `thème`, `déjà`, `°C` doivent s'afficher correctement en IntelliSense et à l'écran

- [ ] **Vérifier `MCEMonitor.Languages.csproj`**
  - Même migration SDK que `MCEMonitor.csproj` (`Microsoft.NET.Sdk.WindowsDesktop` → `Microsoft.NET.Sdk`)
  - Ajouter `<UseWPF>true</UseWPF>` (nécessaire pour `TrExtension` / `MarkupExtension`)
  - Vérifier l'absence de warnings NETSDK1137 sur ce projet

- [ ] **Tests de fumée post-refacto nullabilité** (15 min)
  - [ ] Démarrage normal (toutes les pages s'affichent)
  - [ ] Config email : sauvegarde + test SMTP + badge de statut
  - [ ] Arrêt programmé : sauvegarde + changement Veille ↔ Arrêt
  - [ ] Config vide : vider un fichier `.config`, redémarrer → fallback `new T()` OK
  - [ ] MediaMonitor Live : affichage `Réseau` / `°C` correct

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

### Session 2026-11-01 (suite) — Chasse aux warnings NULL + nettoyage build

**Nullabilité (55 warnings CS86xx éliminés sur 15 fichiers)**

- [x] **Champs WinForms / designer (CS8618)** → `= null!`
  - `MailConfigPage` (15 champs), `ConfirmDialog` (3), `ThemeSelectorForm` (3), `FormWolMacManager` (1), `Program` (3)
- [x] **Propriétés de configuration (CS8618)** → `= string.Empty`
  - `EmailSettings` : `Smtp`, `From`, `To`, `Password`
- [x] **Analyse de flux (CS8600 / CS8602 / CS8603 / CS8604 / CS8625)**
  - `LanguageManager.Get()` : `string` → `string?`
  - `TaskSchedulerHelper.GetShutdownTaskMode()` : `string` → `string?`
  - `ConfigManager.Load<T>()` : `?? new T()` si fichier vide/corrompu
  - `ProcessRunner` : `Encoding? encoding`, `Process? p`
  - `MainForm` : 2× `string?`, null-check explicite sur `GetShutdownTaskMode()`
  - `AppData.BasePath` : accès protégé avec `InvalidOperationException`
- [x] **Délégués `EventHandler` (CS8622)** → `object? sender`
  - `ToggleSwitch.Animate`, `MediaMonitorLiveForm.UpdateStatus`, `ThemeSelectorForm.ThemeList_SelectedIndexChanged`, `FormWolMacManager.textBoxMac_TextChanged`
- [x] **Null-forgiving localisés (`!`)**
  - `Path.GetDirectoryName(...)!` aux endroits où le compilateur ne peut pas prouver la non-nullité

**Warnings annexes (3 éliminés)**

- [x] **NETSDK1137** : `Microsoft.NET.Sdk.WindowsDesktop` → `Microsoft.NET.Sdk` + ajout `<UseWPF>true</UseWPF>` (requis pour `TrExtension` / `MarkupExtension`)
- [x] **CS1998 ×2** : retrait du mot-clé `async` sur `BtnSaveRomConfig_Click` et `BtnSaveSystemConfig_Click` (aucun `await` dans le corps, fire-and-forget assumé via `_ = ...Async()`)

**Changements de comportement volontaires (documentés dans le commit)**

- [x] `ConfigManager.Load<T>` retourne `new T()` au lieu de `null` si la config est vide/corrompue
- [x] `MainForm.UpdateShutdownTaskStatus` ne touche plus au combo si le mode n'est pas déterminable (préserve le choix utilisateur)
- [x] `EmailSettings` : propriétés initialisées à `""` au lieu de `null`

**Corrections d'affichage (mojibake)**

- [x] `MediaMonitorLiveForm` : `Rťseau` → `Réseau`, `įC` → `°C`
- [x] `ThemeSelectorForm`, `FormWolMacManager` : littéraux accentués corrigés

**Résultat** : build 100% propre (0 warning, 0 erreur).

### Session 2026-11-01 — Fix Processus Fantômes & Nettoyage Logs

- [x] Build/Installeur : l'installeur Inno Setup récupère automatiquement la version depuis l'EXE via le `build.bat`.
- [x] UI (À propos) : affichage de la version dynamique lue dans l'assembly (ex: `2.3.6-80`), synchronisée avec GitVersion.
- [x] `ProcessTrustService` : capture de `Win32Error` et propagation dans les résultats.
- [x] `IsSignedBy` : utilisation de `X509Certificate.CreateFromSignedFile` (au lieu de `WinVerifyTrust`).
- [x] `UntrustedProcessDetector` : filtrage par `Win32Error` (fini les processus "inconnus" sans chemin).
- [x] Gestion des race conditions (chemin vide + Win32Error=0).
- [x] NetMonitor : logs Firewall/GeoIP/blocage passés en `Debug`.
- [x] NetMonitor UI : suppression des logs WebView2 de succès.
- [x] RomMonitor : logs SMART anti-spam (seulement si Warning/Critical ou changement du nombre de disques).
- [x] RomMonitor : logs stderr smartctl, ForceTick et config email en `Debug`.

- [x] Vider `TrustedScannerPatterns` (philosophie « tout bot est maudit ») - *Résolu : Ajout de l'option `IgnoreSearchScanners` et édition de `ScannerPatterns` via l'UI WPF.*
- [x] Fix Processus en attente : capture de `Win32Error` dans `ProcessTrustService` et filtrage des PID morts/protégés dans `UntrustedProcessDetector` (suppression des processus "fantômes").
- [x] Blocage automatique granulaire : cases indépendantes pour Critical, Warning et Subnet dans l'UI. Les services Safe/Whitelisted sont exclus du blocage.
- [x] Moteur : refonte de `SeverityScorer` pour respecter la sévérité initiale du détecteur (ex: Blacklist = Critical) tout en appliquant la politique stricte.

### Session 2026-10-31 (Suite) — Contrôle des blocages & Tentatives

- [x] Web : bouton "Retirer de la liste" pour les processus en attente (route `/dismiss-pending` + JS).
- [x] UI & Web : ajout de la colonne Sévérité et du bouton de reclassement dans "Tentatives sur IP bloquée".
- [x] Service : jointure IPC pour exposer la sévérité des retries.
- [x] UI WPF : "Zone de danger" dans les Réglages avec bouton "Libérer toutes les IP bloquées" (purge globale du pare-feu).
- [x] Service : `ClearAllBlockedIps()` pour purger le pare-feu d'un coup sans toucher aux processus bloqués.
- [x] UI : remplacement du 🎨 par ⚙ partout pour la cohérence visuelle.

### Session 2026-10-31 — Scanners "Subnet", GeoIP & Fix UI Web

- [x] Détection : le moteur identifie les scanners via leur Org/Isp GeoIP (ex: Palo Alto, ONYPHE) même sans Reverse DNS
- [x] Modèles : ajout de `IsKnownScanner` sur `IntrusionEvent`, `BlockedIpInfo` et `AuditGroupedEntry`
- [x] UI WPF & Web : affichage d'un badge vert "Subnet" pour les scanners connus, sans altérer leur sévérité réelle (Warning) pour le filtrage/blocage
- [x] UI WPF : ajout de la case "Bloquer les scanners" + champ d'édition `ScannerPatterns` dans les Réglages
- [x] Migration : les anciennes IP bloquées récupèrent leur statut de scanner au démarrage (`LoadBlockedIps`)
- [x] Web : déplacement de la colonne « Compteur » après « Date » (Intrusions Actives)
- [x] Web : déplacement de la colonne « Tentatives » avant « IP » + badges bleus (Tentatives sur IP bloquée)
- [x] Web : badges verts pour le tableau « Alertes ignorées » (IP de confiance)
- [x] Web : fix de l'élargissement des lignes causé par les longues chaînes de type "Info" (Historique + Audit)
- [x] Web : fix du conflit CSS `.info` (panneau) vs `.badge.info` (badge bleu) → renommé en `.info-panel`
- [x] Web : centrage global des badges de sévérité dans leurs cellules via CSS (`td:has(.badge)`)

### Session 2026-10-30 (soir) — Refonte de la sévérité

- [x] `SeverityScorer` créé dans `NetMonitor.Service/Services/`
- [x] Taxonomie : Critical / Warning / Safe / Trusted / Whitelisted (5 niveaux)
- [x] Config : `CriticalPatterns`, `ScannerPatterns`, `SafePatterns`, `WhitelistPatterns` dans `NetMonitor.config`
- [x] Intégration dans `AddIntrusion`, `BlockIp`, `LoadBlockedIps`
- [x] Migration one-shot au démarrage (`SeverityVersion 0 → 2`)
- [x] `ManualSeverity` + `ManualSeveritySetAt` sur `BlockedIpInfo` (attributs du blocage)
- [x] `JsonFileOpts` avec `JsonStringEnumConverter` : tous les fichiers JSON persistés stockent les enums en string
- [x] Branchement `SetManualSeverity(ip, severity?)` côté service
- [x] IPC : commande `set-manual-severity <ip>|<severity|auto>`
- [x] UI WPF : `SeverityPickerWindow` (5 couleurs + Auto)
- [x] UI WPF : bouton 🎨 dans `BlockedIpsGrid` + cadenas 🔒 + tooltip avec date
- [x] UI WPF : `SeverityBadgeTemplate` étendu aux 5 niveaux
- [x] UI WPF : stubs `HasManualSeverity=false` sur les 4 autres rows
- [x] Web : CSS badges 6 classes + `.manual-lock` + `.severity-btn`
- [x] Web : helper `SeverityToCssClass` (7 valeurs dont legacy)
- [x] Web : route POST `/set-severity` + prompt natif avec options 1-5 et `a`
- [x] Web : bouton ⚙ + cadenas 🔒 sur les IPs bloquées manuellement
- [x] UI WPF : colonne Info (badge `i` AbuseIPDB) dans `BlockedIpsGrid`
- [x] `StatsBuilder` : distribution 7 niveaux avec `.Where(Count > 0)`
- [x] `StatsBuilder` : `WorstSeverity` prend maintenant en compte `Warning`
- [x] Fix badge `Info` : gris → bleu `#4CC2FF` (convention universelle)
- [x] Fix tableau Historique Web : `table-layout: fixed` + `width: 100%` + largeurs en %

### Session 2026-10-30 (matin)

- [x] CoverFinder (fallback folder.jpg / cover.jpg / poster.jpg)
- [x] Nettoyage debug `HardwareMonitorService`
- [x] Compactage du tableau « Activité par heure » (heures actives uniquement)
- [x] Badges compteurs : dégradés inversés (foncé = plus grave)
- [x] Tri interceptions par count décroissant
- [x] Boutons de nettoyage par tableau sur la page Web
- [x] Persistance GeoIP au blocage

### Sessions précédentes

- [x] Fix tableau « IP de confiance » (mismatch enum/string IPC)
- [x] Retry WinDivert + popup « Redémarrer le PC ? »
- [x] Refonte onglet Statistiques (2 sections, cartes active/bloquée)