# TODO MCEMonitor

TODO global du projet, organisé par thème.

---

## 🔧 En cours

_(rien)_

## 📋 À faire

### 🔴 Sécurité — Audit et durcissement

- [x] **Auditer les `MiniHttpServer` / `WebServer` des 4 modules** *(fait le 2026-11-17)*
  - **Contexte** : l'UI Android doit être limitée à la consultation
  - **Résultat** : les 8 routes destructives des 4 modules sont désormais en POST (voir la session du 2026-11-17)
  - **Test runtime** : NetMonitor ✅, SystemMonitor ✅, RomMonitor ✅, MediaMonitor ⚠ non testé (aucune sauvegarde disponible au moment du test ; contrôle par revue de code uniquement)
  - **Restent en GET, volontairement** : `/download`, `/export-pdf` (génération, aucune destruction), `/logout`, et toutes les routes de lecture/API
  - **Nuance importante** : le CSRF inter-site est déjà bloqué par le cookie `SameSite=Strict` ; la correction est de la défense en profondeur (préchargement navigateur, F5 sur une URL mémorisée, futur assouplissement de `SameSite`)

- [x] **Vérifier les routes d'export/rapport** *(analysé le 2026-11-17 — aucune modification nécessaire)*
  - `/export-pdf` (NetMonitor) : génère le PDF **en mémoire** (`MemoryStream` + `document.Save`), aucune écriture disque, puis le renvoie en `attachment`. Route authentifiée. Le `GET` est **le bon choix** : c'est un téléchargement déclenché par un simple lien `<a href="/export-pdf">`, qu'un `POST` casserait (il faudrait du JavaScript et un blob).
  - `/report` (MediaMonitor) : construit un rapport HTML à partir de l'historique, en lecture seule. Route protégée par session (elle n'est pas dans la liste `isApiRoute`, donc elle passe par le contrôle de session). Aucun appelant dans le dépôt — route conservée pour un usage manuel.
  - **Correction d'une affirmation précédente** : cet item indiquait qu'ils « écrivaient un fichier temporaire ». C'est faux : tout se fait en mémoire.

### 🟡 Planifié

- [x] **Vague C — Restaurer les règles par défaut sur la page Web** *(livré le 2026-11-17)*
  - Route `/restore-default-patterns` (POST) ajoutée dans `MiniHttpServer.cs`
  - Section « Zone de danger » + bouton `↺ Restaurer les règles à l'origine` + confirmation nommant les 5 listes dans `NetPage.html`
  - Séquence alignée sur l'IPC : `RestoreDefaultPatterns` → `ReloadConfig` → `ReevaluateAllBlockedIps`
  - **Reste à valider** : clic réel sur le bouton en web (compilé et déployé, mais pas encore exercé)

- [ ] **MediaMonitor — affichage "Depuis quand"**
  - Le champ `Timestamp` est présent mais pas utilisé dans l'UI Android
  - Pourrait afficher "en cours depuis X min" sur les items live
  - Optionnel — confort visuel
  - **Priorité** : 🟢 opportuniste

- [ ] **Tests automatisés de non-régression sécurité**
  - Vérifier qu'aucun `Password` n'apparaît en clair dans les logs Android (`adb logcat | Select-String "Password"`)
  - Vérifier que `ServerConfigStore.Save` ne persiste plus que des champs non sensibles
  - **Coût** : 30 min

---

## ✅ Terminé récent

### Session 2026-11-17 — NetMonitor : page web, Tray, installeur

**Corrections de la session — page web, Tray, installeur.**

#### Page web — cause racine : lecture des formulaires

- [x] **`MiniHttpServer.ReadFormValue()`** : consommait `Request.InputStream` à chaque appel → toute route lisant **deux** champs recevait une chaîne vide pour le second. Symptôme : « Forcer la sévérité » répondait « le service a refusé la modification », avec un journal vide. Le corps est désormais lu une fois et mis en cache par requête (`ConditionalWeakTable`), libéré avec la requête. Corrige aussi `/toggle-whitelist-entry`.

#### Page web — alignements

- [x] **« Tentatives sur IP bloquée »** : `<th>` de la colonne radio manquant (14 en-têtes pour 15 cellules) → tout était décalé d'un cran à partir de la 2ᵉ colonne
- [x] **« Processus en attente d'examen »** : badge Origine déplacé après Sévérité (cohérence avec les 5 autres tableaux) + `colspan` 13 → 14 sur la ligne vide

#### Page web — fonctions manquantes

- [x] **Route `/add-pattern`** : n'existait pas (le JS postait vers une 404, le bouton portait « (futur) »). Alignée sur l'IPC `add-pattern-b64` : `AddPattern` → `ReloadConfig` → `ReclassifyBlockedIpsByPattern`
- [x] **Route `/set-origin`** + bouton **🎯 Forcer l'origine** sur les tableaux « IP bloquées » et « Tentatives », avec modale (6 origines + Auto)
- [x] Mentions « (futur) » retirées des libellés de boutons et du titre de la modale règle

#### Page web — cache navigateur

- [x] **`SendHtml` / `LoginPages.WriteHtml`** : aucun en-tête de cache → le navigateur servait du JavaScript périmé après chaque compilation. Ajout de `Cache-Control: no-store, no-cache, must-revalidate`, `Pragma`, `Expires`

#### Page web — diagnostic

- [x] Trace `[WEB] {méthode} {chemin} de {IP}` pour toute requête non-GET dans le journal du service (conservée)

#### Tray

- [x] **« Quitter » ne fermait pas l'UI** : `Exit()` ne tuait que les UI lancées depuis le Tray (`_spawnedUiPids`). Une UI ouverte par le menu Démarrer, par `MCEMonitor.exe` ou par une session précédente du Tray survivait. Aligné sur RomMonitor / SystemMonitor / MediaMonitor : `Kill` de toutes les instances par nom. Champ `_spawnedUiPids` supprimé.

#### Appareils de confiance (cookie 30 jours)

- [x] **`SessionStore`** : la liste des appareils de confiance était en mémoire → un arrêt du service obligeait à refaire login + OTP. Persistée dans `%ProgramData%\MCEMonitor\NetMonitor\trusted_devices.json`, chargée une fois au premier accès, écrite de façon atomique (`.tmp` + `File.Move`) après chaque mutation. Les sessions de 15 min et les codes OTP de 5 min restent en mémoire (choix assumé).

#### Installeur

- [x] **Pilote WinDivert** : `WinDivert.dll` + `WinDivert64.sys` exclus de la copie récursive `ProgramData` et déployés par `DeployWinDivert()` (`[Code]`). Comparaison taille/version, test de verrou par renommage (un pilote chargé — état `STOP_PENDING` — ne peut pas être renommé), remplacement des deux fichiers ensemble, restauration en cas d'échec. Plus d'invite « Réessayer / Ignorer / Annuler ».
  - **Reste non testé** : le chemin de remplacement réel (pilote de version différente **et** verrouillé) — à valider lors d'une future montée de version de WinDivert.

#### Sécurité — routes destructives en POST

- [x] **NetMonitor** : `/clear-active`, `/clear-history`, `/clear-audit`, `/clear-blocked-retries` exigent désormais POST ; la page utilise `postAndReload()` au lieu de `window.location.href`. Un GET direct renvoie 404.
- [x] **SystemMonitor** : `/clear-history` en POST ; bouton converti en `fetch POST`. Un GET direct renvoie 404.
- [x] **RomMonitor** : `/clear-history` en POST ; bouton converti en `fetch POST`. Un GET direct renvoie 404.
- [x] **MediaMonitor** : `/purge` et `/clear` passent par un garde-fou `RequirePost()` (son `Router.Map` ne filtre pas les méthodes) ; le lien `<a href="/purge">` devient un `<form method="post">`. Un GET direct renvoie **405**.
  - ⚠ `/clear` n'a aucun appelant connu dans le dépôt : si un outil externe l'appelait en GET, il recevra un 405.
  - **Non testé en runtime** (aucune sauvegarde disponible) — validé par revue de code uniquement.
- **Note d'analyse** : le CSRF inter-site est déjà neutralisé par `SameSite=Strict` sur le cookie de session. Cette correction est de la défense en profondeur (préchargement navigateur, F5 sur URL mémorisée, futur assouplissement de `SameSite`).

#### Ressources non libérées

- [x] **`ThreatIntelService`** : implémente désormais `IDisposable` (timer, `FileSystemWatcher`, `HttpClient`). `NetMonitorEngine.Stop()` enchaîne `Stop()`, `Dispose()` puis remet à `null`, comme pour `GoogleIpRangeService`.
- **`TrustedScannerService` et `MicrosoftIpRangeService` : items retirés sans correction.** Leur seul champ libérable est un `SemaphoreSlim`, qui ne détient aucune ressource système ici (`AvailableWaitHandle` n'est jamais utilisé). Le libérer n'apporterait rien et provoquerait une `ObjectDisposedException` dans les `finally { ...Release(); }` si une vérification DNS ou un téléchargement était en vol au moment du `Stop`.
  - **Décision 2026-11-17 : ne rien changer.** Si un jour ce code évolue (usage de `AvailableWaitHandle`, ou ajout d'un vrai handle), réévaluer.

#### Nettoyage / vérifications

- [x] **Fichiers `.old`** : vérifié le 2026-11-17, aucun ne subsiste dans l'arborescence → item retiré de la liste des tâches
- [x] **Item « exclusions Defender » retiré** : le blocage de `WinDivert64.sys` a été résolu dans l'installeur (déploiement contrôlé du pilote, plus d'écrasement d'un fichier chargé). Les exclusions ne sont plus nécessaires.

#### Décision — bruit Mapsui (volontairement non corrigé)

- **`NetMonitor.UI.crash.log`** : `System.ObjectDisposedException: The CancellationTokenSource has been disposed.` levée par `Mapsui.Fetcher.DataFetcher` → bug connu de Mapsui 5.1.0 (issue amont #3373), inoffensif : `App.xaml.cs` appelle déjà `SetObserved()`. Le correctif amont n'existe que dans Mapsui 5.2+, qui ne cible plus que .NET 9 pour WPF.
  - **Décision 2026-11-17 : on ne touche à rien** tant que le projet reste en .NET 8. À reconsidérer lors d'une migration .NET 9.

### Session 2026-11-15 — Corrections massives Service + UI + Tray + Web

**Session marathon : ~50 corrections, 2 refontes UX, 1 feature (Org + Reverse DNS).**

#### Service — 14 bugs critiques corrigés

- [x] **`NetMonitorEngine.Start()`** : double `EmailDigestScheduler` (fuite Timer + double email) → un seul scheduler
- [x] **`NetMonitorEngine.Stop()`** : double `Dispose` sur `_evictTimer` → nettoyé
- [x] **`NetMonitorEngine.LoadBlockedIps()`** : enrichissement `IsKnownScanner` sur liste vide → corrigé (utilise `list` au lieu de `_blockedIps`)
- [x] **`NetMonitorEngine.GetStatsAsync()`** : lazy init `_geoIp` en doublon → supprimé (le constructeur garantit non-null)
- [x] **`NetMonitorSettings.Load()` + `Reload()`** : `TrustedScannerPatterns` mal mappé (sur `IgnoredProcesses` et `CriticalPatterns`) → mappé sur `ScannerPatterns`
- [x] **`IpcCommandHandler.HandleAddPattern()`** : double `AddPattern` + double `ReloadConfig` → appel unique
- [x] **`NetMonitorEngine.ReloadConfig()`** : ne propageait pas les changements à `_trustedScanners`, `_msRanges`, `_googleRanges` → hot-swap ajouté
- [x] **`TrustedScannerService`** : capture le tableau `ScannerPatterns` au constructeur → `UpdatePatterns()` ajoutée
- [x] **`ProcessTrustService.IsSignedBy()`** : ne valide pas la chaîne de certification (faille sécurité) → validation `WinVerifyTrust` via `TryGetAuthenticodePublisher`
- [x] **`WebHandler.BuildNetPage()`** : colonnes désalignées dans tableau 1 (alertes sans RemoteIp) → corrigé
- [x] **`LoginService.ValidateOtp()`** : `Attempts++` non atomique (race brute-force) → `lock` global
- [x] **`LoginService.PurgeExpired()`** : jamais appelé → timer 5 min dans `Program.cs`
- [x] **`EmailDigestScheduler.Enqueue()`** : déduplication sans fenêtre temporelle + O(n²) → `ConcurrentDictionary` avec fenêtre glissante 5 min
- [x] **`FirewallHelper.RemoveRule()`** : jamais appelé quand `WebEnabled=false` → `else` ajouté dans `RestartWebServer`

#### Service — 2 améliorations

- [x] **`GeoIpService : IDisposable`** : libère `HttpClient` + `SemaphoreSlim` (fuite sockets fermée)
- [x] **Arrêt propre du service** : `AppDomain.ProcessExit` + timeouts réduits `EtwConnectionWatcher` (7s → 2s)

#### Service — Feature Org + Reverse DNS

- [x] **`IntrusionEvent`** : + `Org` + `ReverseDns`
- [x] **`AuditGroupedEntry`** : + `Org` + `ReverseDns`
- [x] **`NetMonitorEngine`** : `GetAlerts`, `GetActiveIntrusions`, `GetAuditHistoryGrouped` enrichissent `Org` et `ReverseDns`
- [x] **`IpcCommandHandler`** : 5 handlers projettent `Org` + `ReverseDns`
- [x] **`ApiDetail`** : 3 DTOs avec `Org` + `ReverseDns`
- [x] **`MiniHttpServer /api/detail`** : 3 blocs enrichis (jointure `BlockedIpInfo` pour les retries)
- [x] **`NetMonitorEngine.FinalizeBlockedIpGeoAsync`** : `includeReverseDns: true` → les PTR sont maintenant résolus

#### Service — Sécurité IPC base64

- [x] **IPC `*-b64`** : 5 nouvelles commandes avec payload JSON base64
  - `block-ip-b64`, `set-web-config-b64`, `add-pattern-b64`, `set-manual-severity-b64`, `toggle-whitelist-entry-b64`
  - Résout l'injection via `|` et `&` dans les valeurs libres (messages, mots de passe)
  - Commandes legacy conservées pour rétrocompatibilité

#### UI WPF — 9 bugs corrigés

- [x] **`HandleGetConfig`** (Service) : 3 tableaux manquants (`safePatterns`, `criticalPatterns`, `whitelistPatterns`) → patterns effacés au Save
- [x] **`MergeConfigFile()`** (nouvelle méthode) : préserve les settings non modifiés par l'UI (GeoIp*, IgnoredProcesses, EnableProcessTrust, SuspiciousRemotePorts…)
- [x] **`BlacklistLastUpdate`** : parsing robuste (plus de réinitialisation à UtcNow)
- [x] **`StatsChartsPanel.Update()`** : granularité réinitialisée à chaque refresh → fallback supprimé
- [x] **`_refreshTimer.Tick`** : refresh bloqué si sélection active → période de grâce 2 min
- [x] **Web settings** dans `NetMonitor.config` au lieu de `web.config` → retirées
- [x] **`SeverityPickerWindow`** : + Bot/Crawler/ToCheck (cohérence enum)
- [x] **`_notifiedProblems`** : TTL 24h + borne 200 entrées
- [x] **`UiModels.cs` + `SeverityToBrushConverter.cs`** : dead code → renommés `.old`

#### UI WPF — Vague B (Org + Reverse DNS)

- [x] **`NetMonitorIpcClient`** : `NetAlert`, `BlockedIpRetryDto`, `AuditGroupedDto` exposent `org` et `reverseDns`
- [x] **`MainWindow.xaml.cs`** : 5 classes Row enrichies (`AlertRow`, `BlockedIpRow`, `GroupedAlertRow`, `AuditGroupedRow`, `BlockedIpRetryRow`) avec `Org` + `ReverseDns` + propriétés calculées
- [x] **`MainWindow.xaml`** : 5 grilles mises à jour :
  - `ActiveIntrusionsGrid` : + Pays + FAI (ASN) + Organisation + ToolTip PTR
  - `BlockedIpsGrid` : FAI → FAI (ASN), + Organisation + colonne Reverse DNS
  - `AlertsHistoryGrid` : FAI → FAI (ASN), + Organisation + ToolTip PTR
  - `AuditGrid` : FAI → FAI (ASN), + Organisation + ToolTip PTR
  - `BlockedRetriesGrid` : FAI → FAI (ASN), + Organisation + colonne Reverse DNS
- [x] **`MainWindow.xaml.cs`** : désélection par clic sur zone vide + touche Échap (toutes les grilles)
- [x] **`MainWindow.xaml.cs`** : alerte manquante sur `AddPatternFromBlockedIp_Click` sans sélection

#### UI WPF — Refonte boutons (aligné sur WPF)

- [x] **Retiré les boutons inline** ⚙ et 🏷️ des DataGrids
- [x] **Ajouté les boutons globaux** dans les barres d'action :
  - `BlockedIpsGrid` : ⚙ Forcer la sévérité / 🏷️ Créer une règle (futur)
  - `BlockedRetriesGrid` : idem + suppression des colonnes inline
- [x] **`PatternPickerWindow`** : titre "Créer une règle de classification (futur)" + bandeau d'avertissement + bouton rouge "Créer la règle" + "✖ Annuler"
- [x] **`SeverityPickerWindow`** : titre "Forcer la sévérité (cette IP uniquement)" + texte explicatif + correction du chevauchement Grid
- [x] **Nouveau bouton "↺ Restaurer les règles à l'origine"** dans la Zone de danger
- [x] **Route `restore-default-patterns`** (IPC) + `RestoreDefaultPatterns()` dans `NetMonitorSettings` + `ReevaluateAllBlockedIps()` appelé après

#### Tray — 10 bugs corrigés

- [x] **`countPrefix`** calculé mais jamais affiché → `[xN]` visible dans le popup
- [x] **Ranking severities** : Bot/Crawler/Trusted/Safe ne sont plus des menaces (ni côté Tray ni côté Service)
- [x] **Icon handle leak** : `DestroyIcon` ajouté après `Bitmap.GetHicon`
- [x] **Timers non disposés** à l'arrêt → `Stop + Dispose` pour les 3 timers
- [x] **`Exit()`** tuait toutes les UI (même celles lancées manuellement) → `_spawnedUiPids`
- [x] **`Exit()`** tuait le Service après 2.8s (cleanup prend ~4.5s) → attente portée à 5s + logs
- [x] **Commentaire watchdog** trompeur (60s affiché, 20s réel)
- [x] **`Models/Alert.cs`** : dead code → renommé `.old`
- [x] **`LoadSound`** : fuite `MemoryStream` documentée (volontaire)
- [x] **`GetProblems`/`GetWorstSeverity`** : TODO pour timeout IPC

#### Web — Refonte UX modales

- [x] **`WebHandler.cs`** : 5 tableaux enrichis (Org, FAI (ASN), Reverse DNS) + correction colspan
- [x] **`WebHandler.cs`** : retiré les boutons inline ⚙ et 🏷️ des lignes
- [x] **`WebHandler.cs`** : ajouté le radio de sélection au tableau 5 (Interceptions)
- [x] **`WebHandler.cs`** : data-attributes `data-isp/org/rdns` sur les `<tr>` (pour alimenter les modales)
- [x] **`NetPage.html`** : boutons globaux dans les barres d'action
- [x] **`NetPage.html`** : modale "Forcer la sévérité" (grille de 8 cartes colorées + Auto)
- [x] **`NetPage.html`** : modale "Créer une règle" (formulaire complet + aperçu + bandeau)
- [x] **`NetPage.html`** : CSS dédié (backdrop-filter, animation)
- [x] **`NetPage.html`** : fermeture ESC + clic sur fond assombri

#### Langues — 3 nouvelles clés × 4 fichiers

- [x] **`fr-FR.json`** : FAI (ASN), Organisation, Reverse DNS
- [x] **`en-GB.json`** : ISP (ASN), Organization, Reverse DNS
- [x] **`de-DE.json`** : ISP (ASN), Organisation, Reverse DNS
- [x] **`es-ES.json`** : ISP (ASN), Organización, Reverse DNS

#### Bugs de sévérité (Session 2026-11-15)

- [x] **`SetManualSeverity`** : retour `auto` utilise `GetBaseSeverityForDetector()` au lieu de `Severity.Warning` hardcodé → Blacklist revient en Critical
- [x] **`WorstSeverity`** : ignore Bot/Crawler/Trusted/Safe/Whitelisted → un scan Shodan ne fait plus passer le tray en orange
- [x] **SafePatterns** : retiré `amazonaws.com`, `akamai.net`, `apple.com` (faux "Safe" sur IPs blacklistées hébergées chez AWS/Akamai/Apple)
- [x] **CrawlerPatterns** : ajouté `infrawat.ch` + `hydra` (scanners agressifs, pas du malware avéré)
- [x] **CriticalPatterns** : retiré `infrawat.ch` (déplacé vers Crawler)
- [x] **`RestoreDefaultPatterns()`** : aligné sur le constructeur
- [x] **`HandleRestoreDefaultPatterns()`** : appelle `ReevaluateAllBlockedIps()` après restauration

#### Code mort supprimé

- [x] **Service** : `Alert.cs`, `AlertHistory.cs`, `AlertManager.cs` renommés `.old`
- [x] **Service** : 6 méthodes publiques mortes supprimées (`FirewallHelper.IsAlreadyBlocked`, `ServiceIpcServer.Stop`, `TrustedScannerService.Clear`, `GeoIpService.Clear`, `ProcessResolver.ClearCache`, `SeverityScorer.ExtractRootDomain`, `GoogleIpRangeService.IsCommonCrawlerIp`, `ViewRenderer.ClearCache`)
- [x] **UI** : `Models/UiModels.cs` + `Converters/SeverityToBrushConverter.cs` renommés `.old`
- [x] **Tray** : `Models/Alert.cs` renommé `.old`
- [x] **4 méthodes `Inspect()`** mortes supprimées dans `PortScanDetector`, `SuspiciousOutboundDetector`, `BeaconingDetector`, `UntrustedProcessDetector`

#### Résultats mesurés en production

- ✅ Les 3 IPs AWS blacklistées passent de "Safe" à "Critical"
- ✅ Les 11 IPs Hydra/Infrawat passent de "Critical" à "Crawler"
- ✅ 14 IPs réévaluées correctement à la restauration
- ✅ Le tray reste vert sur un scan Shodan (vague 4 corrigée)
- ✅ Les badges "Scanner" remplacent "Subnet"
- ✅ Les tooltips des badges ne sont plus vides

### Session 2026-11-11 — Login custom sur les 4 modules

**Remplacement du popup Basic Auth du navigateur par une page de login custom sur les 4 modules. NetMonitor a en plus l'OTP par email.**

#### Système d'authentification

- [x] **`SessionStore.cs`** dans les 4 modules : sessions 15 min en mémoire (`ConcurrentDictionary`), cookie `netmon_session` / `sysmon_session` / `rommon_session` / `mm_session` (HttpOnly, SameSite=Strict).
- [x] **`LoginPages.cs`** dans les 4 modules : page `/login` custom (style dark cohérent).
- [x] **`LoginService.cs`** dans les 4 modules : `CheckCredentials` (comparaison à temps constant via `CryptographicOperations.FixedTimeEquals`).

#### NetMonitor — login unifié + OTP

- [x] **Formulaire adaptatif** : `step=1` (credentials) → vérification → `step=2` (credentials + OTP) si IP distante.
- [x] **Fallback localhost / IP LAN** : bypass OTP, session directe.
- [x] **`LoginService.SendOtpIfNeededAsync`** : génère code 6 chiffres (`RandomNumberGenerator`), envoie SMTP via `EmailSender.SendAsync`.
- [x] **`LoginService.ValidateOtp`** : `FixedTimeEquals`, 3 tentatives max, expiration 5 min, single-use.
- [x] **`NetMonitorSettings.cs`** : +3 propriétés `OtpEnabled` / `OtpSessionMinutes` / `OtpCodeLength`.
- [x] **`NetMonitor.web.config`** : +3 lignes (défaut `OtpEnabled=false`).
- [x] **`MainWindow.xaml`** : `ToggleOtp` dans la section Serveur Web.
- [x] **`MainWindow.xaml.cs`** : handlers `ToggleOtp_Checked` / `ToggleOtp_Unchecked` + `ApplyOtpConfig()`.

#### SystemMonitor, RomMonitor, MediaMonitor — login simple (sans OTP)

- [x] **Patch `MiniHttpServer.cs`** (SystemMonitor, RomMonitor) et **`WebServer.cs`** (MediaMonitor) :
  - Routes publiques : `/login` (GET + POST), `/logout`.
  - Garde session sur les pages HTML.
  - Basic Auth conservé sur les routes API (`/api/*`, `/peers`, `/ping`, et pour MediaMonitor : `/history`, `/live`, `/status`, `/lastimage`).
  - Ressources statiques (`/resources/*`, `/favicon.ico`) sans auth.
- [x] **Pas d'OTP** sur ces 3 modules (lecture seule, pas d'action mutative critique).

#### Bouton Déconnexion

- [x] **`PeerStatusService.cs`** dans les 4 modules : bouton « 🚪 Déconnexion » dans la peerbar (toutes les pages HTML).

#### Fichiers supprimés (remplacés par la refonte)

- [x] `OtpService.cs`, `OtpPages.cs`, `OtpSessionStore.cs` (NetMonitor) → remplacés par `LoginService.cs`, `LoginPages.cs`, `SessionStore.cs`.

#### Comportement final

- **NetMonitor** : `OtpEnabled=false` → login simple (2 champs). `OtpEnabled=true` → login 3 champs (credentials puis OTP) sauf localhost/IP LAN.
- **SystemMonitor, RomMonitor, MediaMonitor** : login simple (2 champs) partout, pas d'OTP.
- **Clients API** (PollingEngine, Android, PeerStatusService) : Basic Auth seul sur les routes API, pas de session.
- **UI locale** (NetMonitor.UI, SystemMonitor.UI, etc.) : passent par IPC, pas par HTTP → pas impactées.

#### Tests validés

- [x] Login / logout sur les 4 modules depuis une autre machine du LAN (navigation privée).
- [x] `curl -u user:pass /api/summary` → 200 OK.
- [x] `curl /api/summary` (sans auth) → 401.
- [x] `curl -u user:pass /peers` → JSON.
- [x] Bouton Déconnexion → redirection `/login`, session supprimée.
- [x] Toggle OTP NetMonitor : ON/OFF → `NetMonitor.web.config` mis à jour → service rechargé à chaud.
- [x] Fallback localhost / IP LAN : bypass OTP.

#### Décision actée — HTTPS abandonné

- [x] **HTTPS — ABANDONNÉ** (décision 2026-11-11)
  - **Contexte** : LAN privé. Le certificat auto-signé impose une installation manuelle sur chaque navigateur (Chrome + Firefox séparément) pour un gain marginal en LAN.
  - **Alternatives retenues** : Basic Auth + OTP email + contrôle d'accès réseau (pare-feu, pas d'exposition Internet).
  - **Code reverté** : `HttpsSetupForm`, `CertificateHelper`, `WebUseHttps`/`WebCertThumbprint` supprimés. Les `MiniHttpServer` / `WebServer` sont revenus à `http://` en dur.
  - **Si besoin futur** : certificat Let's Encrypt (nécessite un domaine public) ou AC interne (AD CS) — hors périmètre.

### Session 2026-11-10 — Vérifications, correctifs Android, nettoyages

*(contenu inchangé — voir sections Correctifs Android, Tests réactifs, Planifiés validés, Nettoyages opportunistes)*

### Session 2026-11-09 — Optimisations NetMonitor + validation ToCheck

*(contenu inchangé)*

### Session 2026-11-08 — Client Android : 4 services en consultation

*(contenu inchangé)*

### Session 2026-11-08 — GeoIP : cache, quota, refresh + fix ScannerPatterns

*(contenu inchangé)*

### Vérifications one-shot

- [x] **`mcemonitor.keystore` n'est pas versionné** (vérifié 2026-11-09)

---

## ✅ Terminé antérieur (résumé)

- **2026-11-06** : nettoyage `MediaMonitor.Core` (4 packages → 2, ~50 Mo économisés), fix vignettes MP3, fix WinDivert 1058, protection `*.web.config` en lecture seule