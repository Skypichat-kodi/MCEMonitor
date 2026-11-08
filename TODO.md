# TODO MCEMonitor

TODO global du projet, organisé par thème.

---

## 🔧 En cours

_(rien)_

## 📋 À faire

### 🔴 Sécurité — Audit et durcissement

- [ ] **Forcer HTTPS sur tous les endpoints API**
  - **État actuel** : `http://` + Basic Auth = identifiants en clair sur le réseau
  - **Risque** : interception LAN / WiFi public / VLAN compromis
  - **Solution** : TLS sur les 4 serveurs (certificat auto-signé + pinning côté Android, ou certificat Let's Encrypt si domaine)
  - **Impact multi-modules** : SystemMonitor, RomMonitor, NetMonitor, MediaMonitor
  - **Post-requis** : repasser `android:usesCleartextTraffic` à `false` dans `AndroidManifest.xml`
  - **Coût** : 4-6 h (chantier transverse)
  - **Priorité** : 🔴 haute (avant diffusion publique)

- [ ] **Auditer les `MiniHttpServer` / `WebServer` des 4 modules**
  - **Contexte** : l'UI Android doit être limitée à la consultation
  - **Déjà fait** :
    - SystemMonitor : ✅ audité (le `/clear-history` en GET destructif reste mineur, sauvegardé par PDF)
    - NetMonitor : ✅ audité (les actions sont bien en POST, l'Android n'y touche pas)
  - **À faire** :
    - RomMonitor : vérifier `/clear-history`, `/peers`, autres routes
    - MediaMonitor : vérifier `/purge`, `/clear`, `/download`, `/logo`, `/report`
  - **Test** : `curl -u user:pass http://serveur:PORT/` ne doit exposer aucune opération critique en GET
  - **Coût** : 30 min
  - **Priorité** : 🔴 haute (vérification, pas de code à écrire si tout est bon)

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
    - Vérifier WPF / Web / Tray Windows / Android
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

- [ ] **Test notifications Android** sur les 4 services
  - **Déclencheur** : avant diffusion publique de l'APK
  - **Procédure** : déclencher une alerte de chaque type (SystemMonitor CPU, RomMonitor disque, NetMonitor intrusion, MediaMonitor lecture), cliquer sur chaque notification → vérifier navigation vers la bonne page de détail
  - **Coût** : 15 min

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

- [ ] **MediaMonitor — tags ID3 dans `/api/detail`**
  - `MediaItemFactory.BuildItem` (SMB) ne lit pas les tags ID3
  - `Artist` / `Album` / `Title` vides actuellement dans le JSON
  - À ajouter si l'UI Android en a besoin (analyse `FileAnalyzer` côté serveur)
  - **Priorité** : 🟡 moyenne (confort, pas critique)

- [ ] **Refactoring : `ServerDetailPageBase` commune**
  - 4 pages quasi-identiques (SystemMonitor, RomMonitor, NetMonitor, MediaMonitor)
  - Extraire le header, `RefreshView`, cycle de vie, timer dans une classe de base
  - Optionnel — gain de maintenance à long terme
  - **Priorité** : 🟢 opportuniste

- [ ] **MediaMonitor — affichage "Depuis quand"**
  - Le champ `Timestamp` est présent mais pas utilisé dans l'UI Android
  - Pourrait afficher "en cours depuis X min" sur les items live
  - Optionnel — confort visuel
  - **Priorité** : 🟢 opportuniste

- [ ] **Tests automatisés de non-régression sécurité**
  - Vérifier qu'aucun `Password` n'apparaît en clair dans les logs Android (`adb logcat | Select-String "Password"`)
  - Vérifier que `ServerConfigStore.Save` ne persiste plus que des champs non sensibles
  - **Coût** : 30 min

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

- [ ] **Ajouter les exclusions Defender** sur la machine W10 Q9550
  - `Add-MpPreference -ExclusionPath "C:\ProgramData\MCEMonitor"`
  - `Add-MpPreference -ExclusionProcess "NetMonitor.Service.exe"`
  - `Add-MpPreference -ExclusionExtension ".sys"`
  - → Évite à SmartScreen de retenir le handle de `WinDivert64.sys` au démarrage (démarrage 2-3× plus rapide)

- [ ] **Centraliser les ressources** dans `MCEMonitor.Shared` (gros chantier, optionnel)
  - ~15 icônes × 10 projets, 5 `.ico` × 3 copies, 2 `.wav` × 3 projets
  - Optionnel — gain de maintenance à long terme

- [ ] **`Directory.Build.props` à la racine**
  - Factoriser `<TargetFramework>`, `<Nullable>`, `<ImplicitUsings>` pour les 23 projets
  - Optionnel — gain de lisibilité

---

## ✅ Terminé

### Session 2026-11-08 — Client Android : 4 services en consultation

**Ajout de pages de détail Android pour les 4 services, avec navigation depuis la liste et depuis les notifications. Menu contextuel stylé, filtre d'historique MediaMonitor, sécurité `SecureStorage`.**

#### Sécurité Android

- [x] **`SecureStorage` pour les identifiants** : remplace le JSON en clair
  - Clés `mcem_server_{id}_username` / `_password` (préfixe `mcem_` pour éviter les collisions futures)
  - Migration one-shot : les anciens JSON avec identifiants en clair sont migrés automatiquement au premier lancement
  - Nettoyage des identifiants orphelins (serveurs supprimés) via `Preferences` `mcem_known_server_ids`
- [x] **`android:allowBackup="false"`** dans `AndroidManifest.xml`
- [x] **Cache mémoire dans `ConfigService`** : évite de relire `SecureStorage` à chaque retour au premier plan
  - Gain notable avec 14 serveurs configurés
- [x] **`android:usesCleartextTraffic="true"`** laissé à `true` (à repasser à `false` avec HTTPS)

#### SystemMonitor — API détail + page Android

- [x] **`ApiDetail.cs`** côté serveur : DTO complet (CPU, RAM, GPU, réseaux, historique, BSOD, alertes)
- [x] **Route `/api/detail`** dans `MiniHttpServer`
- [x] **`SystemDetailDto`** + `DetailClient.FetchSystemDetailAsync`
- [x] **`SystemMonitorDetailViewModel`** (timer 5 s + pull-to-refresh)
- [x] **`SystemMonitorDetailPage`** : header, CPU (barre + cœurs), RAM, GPU, réseaux, alertes, BSOD
- [x] Navigation depuis `MainPage` et depuis les notifications

#### RomMonitor — API détail + page Android

- [x] **`ApiDetail.cs`** côté serveur : DTO disques (volumes + SMART) + problems
- [x] **Route `/api/detail`** dans `MiniHttpServer`
- [x] **`RomDetailDto`** + `DetailClient.FetchRomDetailAsync`
- [x] **`RomMonitorDetailViewModel`** (timer 10 s + pull-to-refresh)
- [x] **`RomMonitorDetailPage`** : header, problèmes actifs, volumes, disques SMART
  - Barres colorées (< 5 % rouge, < 15 % orange)
  - Conversion heures en j/années pour lisibilité
- [x] **`IsStringNotEmptyConverter`** (déclaré localement dans la page — évite le crash `StaticResource` non résolu)

#### NetMonitor — API détail + page Android

- [x] **`ApiDetail.cs`** côté serveur : DTO complet (active, blocked, retries, pending, whitelist, problems, stats)
- [x] **Route `/api/detail`** dans `MiniHttpServer` (lecture seule, aucune action d'écriture)
- [x] **`NetDetailDto`** + `DetailClient.FetchNetDetailAsync`
- [x] **`NetMonitorDetailViewModel`** (timer 15 s + pull-to-refresh)
- [x] **`NetMonitorDetailPage`** (ContentPage unique, pas de `TabbedPage`)
  - Sections : header, problèmes, intrusions actives, IP bloquées, retries, stats, pending, whitelist
  - **Décision** : `TabbedPage` incompatible avec .NET MAUI Shell → remplacé par ContentPage + ScrollView
- [x] Whitelist exposée dans `/api/detail` (17 entrées)

#### MediaMonitor — API détail + page Android

- [x] **`ApiDetail.cs`** côté serveur : DTO live + history + lastImage + stats
- [x] **Route `/api/detail`** dans `WebServer.cs` (pas de `MiniHttpServer` — architecture différente)
  - `history` limité à 50 items récents (le backup reste sur PC)
  - `stats` calculées sur l'historique complet
- [x] **`MediaDetailDto`** + `DetailClient.FetchMediaDetailAsync`
- [x] **`MediaMonitorDetailViewModel`** (timer 10 s + pull-to-refresh + filtre par type)
- [x] **`MediaMonitorDetailPage`**
  - Header, En cours, Dernière image, Historique filtré (6 boutons : Tous / Audio / Série / Vidéo / Image / TV-REC), Stats
  - **Décision** : pas de section Backup (peut être énorme, hors scope mobile)

#### UX MainPage

- [x] **Menu contextuel au tap** : Consulter / Modifier / Supprimer
- [x] **Popup custom `ServerActionsPopup`** (CommunityToolkit.Maui)
  - Boutons colorés (bleu / gris / rouge)
  - « Consulter » grisé si serveur hors ligne
- [x] **Vérification d'accessibilité** avant ouverture de la page de détail (`IsServerReachableAsync`)
- [x] Boutons Modifier/Supprimer retirés de la barre du bas
- [x] Navigation vers le détail au clic sur une notification (remplace l'ouverture du navigateur)

#### Navigation

- [x] **`ServerDetailRouter`** (extensible par `ServiceType`) : `systemmonitor` / `rommonitor` / `netmonitor` / `mediamonitor`
- [x] **`AppShell.xaml.cs`** : 4 routes enregistrées
- [x] **`MainActivity.HandleNotificationIntent`** : navigation depuis une notification (OnCreate + OnNewIntent)
- [x] **`PendingIntent.CancelCurrent`** dans `ShowAlert` (évite le cache `PendingIntent` qui gardait l'ancienne URL web)

#### Bugs corrigés en cours de route

- [x] `ShowAlert` utilisait encore `Intent.ActionView` vers l'URL web → remplacé par `Intent` vers `MainActivity` avec extras
- [x] `MainActivity` ne lisait jamais les extras de notification → ajout `OnNewIntent` + `HandleNotificationIntent`
- [x] `TabbedPage` incompatible avec MAUI Shell → remplacé par ContentPage unique
- [x] `StaticResource IsStringNotEmptyConverter` non déclaré → crash à l'ouverture de `RomMonitorDetailPage` → déclaré localement dans la page
- [x] `using System.Linq` manquant dans `WebServer.cs` (MediaMonitor) → erreurs `OrderByDescending`/`Where`
- [x] Namespaces incorrects (`Pages.NetMonitorDetailPage` au lieu de `Pages.NetMonitor.NetMonitorDetailPage`) → corrigés après déplacement dans `Pages/`

**Résultat global** : les 4 services sont consultables depuis l'app Android. Navigation depuis la liste et depuis les notifications. Identifiants sécurisés via `SecureStorage`. 14 serveurs configurés chargés rapidement grâce au cache.

### Session 2026-11-08 — GeoIP : cache, quota, refresh + fix ScannerPatterns

**Arrêt des re-résolutions inutiles de GeoIP (quota ipwho.is partagé par IP publique), pause 24h sur 429, rotation de refresh 50 IPs / 12h, et correction du bug qui écrasait `ScannerPatterns=` dans le config à chaque sauvegarde.**

#### GeoIP — cache et quota

- [x] **`GeoIpService.SeedCache(ip, GeoInfo)`** : amorce le cache mémoire depuis `blocked_ips.json` / `alert_history.json` au démarrage
  - Plus aucune requête API pour les IPs déjà enrichies
  - Log au démarrage : `[GEOIP] Cache amorcé : XXX IP(s) depuis blocked_ips.json`
- [x] **`GeoIpService.InvalidateCache(ip)`** : force un re-fetch (utilisé par le refresh périodique)
- [x] **`GeoIpService.IsQuotaExceeded`** : flag public exposant la pause courante
- [x] **`FetchGeoAsync`** : sur HTTP 429, positionne `_quotaExceededUntil` à `UtcNow + 24h` minimum
  - Log en heure locale avec date : `Quota épuisé — pause jusqu'à 2026-11-09 15:20`
- [x] **`ResolveAsync`** : court-circuit si quota épuisé (retourne `GeoInfo` transitoire avec `ResolvedAt = MinValue`, non caché)
- [x] **Throttle** : `SemaphoreSlim(5,5)` → `SemaphoreSlim(2,2)` (réduit le rate-limit)
- [x] **Cache non empoisonné** : `FetchGeoAsync` retourne `null` sur échec (au lieu d'un `GeoInfo` vide), `ResolveAsync` ne cache plus les échecs

#### GeoIP — backfill et refresh périodique

- [x] **`LoadBlockedIps()`** : amorce le cache depuis `blocked_ips.json` après chargement
- [x] **`LoadHistory()`** : amorce le cache depuis `alert_history.json` après restauration
- [x] **`FinalizeBlockedIpGeoAsync`** :
  - Court-circuit `IsQuotaExceeded` en tête de méthode
  - Court-circuit `IsQuotaExceeded` entre chaque tentative de la boucle (break immédiat au lieu de 3 retries)
  - Logs explicites par tentative (HTTP status, refus API, timeout)
- [x] **`RefreshOldGeoIps()`** (nouveau, toutes les 12h via `CleanupAutoBlockedIps`) :
  - Priorité 1 : rattraper les IPs jamais résolues (`GeoResolvedAt == null`)
  - Priorité 2 : rafraîchir les **50** IPs les plus anciennes (rotation par curseur `_lastGeoRefreshCursor`)
  - Délai 1s entre chaque appel, abandon si quota épuisé en cours de lot
  - Tour complet → curseur réinitialisé
- [x] **Backfill au démarrage** : `MaxDegreeOfParallelism = 1` + délai initial 15s + throttle 500ms entre IPs
- [x] **Bilan final** : `[GEOIP-REFRESH] Lot terminé : XX OK, XX KO, XX reportée(s)`

#### Fix `ScannerPatterns` écrasé par du vide

- [x] **`NetMonitorSettings.Load()`** : si la clé existe mais est vide, garde la valeur par défaut (la liste complète)
- [x] **`NetMonitorSettings.Reload()`** : idem, avec fusion des labels `"TrustedScannerPatterns"` / `"ScannerPatterns"` (évite le doublon `CS0152`)
- [x] **`NetMonitorSettings.Save()`** : garde-fou `JoinOrFallback(ScannerPatterns, DefaultScannerPatterns)` → écrit toujours la liste par défaut si vide
- [x] **`MainWindow.LoadSettings()`** (UI) : si `cfg.scannerPatterns` est vide, pré-remplit `txtScannerPatterns` avec la liste par défaut
- [x] **`MainWindow.btnSaveSettings_Click`** (UI) : garde-fou, n'envoie jamais une liste vide au service
- [x] **Constante `DefaultScannerPatterns`** ajoutée dans `NetMonitorSettings`

#### Fix race condition `blocked_ips.json`

- [x] **`SaveBlockedIps()`** : verrou `_saveBlockedLock` + écriture atomique (`tmp` + `File.Move(overwrite:true)` + retry 50ms)
  - Plus de `[WARN] SaveBlockedIps : The process cannot access the file...`

#### Comportement validé

- [x] Démarrage : `0 appel GeoIP` pour les IPs déjà connues (`Cache amorcé : XXX IP(s)`)
- [x] `Backfill au démarrage : 0 IP(s)` quand tout est déjà rempli
- [x] Refresh 50 IPs / 12h : `50 OK, 0 KO` sans aucun 429
- [x] Quota épuisé : pause 24h, aucune requête réseau, reprise automatique au bout de 24h ou au reboot (le reboot semble réinitialiser la fenêtre de rate-limit ipwho.is)
- [x] `Scanners whitelistés : shadowserver.org, censys.io, ...` après sauvegarde UI (cercle vicieux cassé)
- [x] Aucune régression sur les 2 machines de test (Q9550/W10 et Ryzen 7/32 Go)

**Résultat global** : consommation GeoIP divisée par ~10, plus aucun 429 en rafale, `ScannerPatterns` stable à travers les sauvegardes et redémarrages.

### Session 2026-11-06 — Nettoyage global + fix WinDivert 1058

**Audit des packages NuGet, du code Interop, correction du fallback de vignettes audio, et fix d'un bug WinDivert sur W10.**

#### Nettoyage `MediaMonitor.Core`

- [x] **Audit `Interop/WMF.cs`** → supprimé
  - Code mort (aucun `using MediaMonitor.Core.Interop`, aucun type `WMF`/`IMFSourceReader` appelé)
  - GUID `IMFSourceReader` invalide (`70AE66F2-C8B1-...` au lieu de `70AE66F2-C809-...`)
  - Signatures P/Invoke incorrectes (`MFCreateSourceReaderFromURL`, `GetUINT64`)
- [x] **Audit `Microsoft-WindowsAPICodePack-Core` 1.1.5** → supprimé (aucun `using`)
- [x] **Audit `Microsoft-WindowsAPICodePack-Shell` 1.1.5** → supprimé (aucun `using`)
- [x] **Audit `System.Drawing.Common` 8.0.0** → supprimé (build OK sans lui)
- [x] **Audit `System.Management` 8.0.0** → **conservé** (utilisé par `SmbSessions.cs` / `SmbOpenFiles.cs`)
- [x] **Fix commentaire trompeur** `<!-- Ancien package supprimé -->` dans `MediaMonitor.Core.csproj`
- [x] **`MediaMonitor.Core.csproj` nettoyé** : 4 packages → 2 (`MailKit` + `System.Management`)
- [x] Dossier `Interop/` supprimé (vide après retrait de `WMF.cs`)
- [x] Build 0 warning, 0 erreur. Publish OK. Aucune DLL APICodePack/Drawing dans la sortie.

#### Fix vignettes MP3 (`FileAnalyzer.cs`)

- [x] **`CleanImageData()`** : détection du vrai début JPEG (`FF D8`) ou PNG (`89 50 4E 47`) dans les ID3 APIC mal extraits
- [x] **`IsValidImage()`** : validation JPEG, PNG, GIF, BMP, WebP
- [x] **Fallback fiable** : si TagLibSharp plante ou renvoie une image invalide → `CoverFinder.FindExternalCover()`
- [x] Testé avec MP3 sans ID3 + folder.jpg, MP3 avec APIC corrompu, vidéo codec KO + poster.jpg

#### Optimisation ffmpeg (-50 Mo)

- [x] **Diagnostic** : `FileAnalyzer` (seul consommateur de ffmpeg) est appelé **uniquement** par :
  - `MediaMonitor.Service\Ipc\IpcCommandHandler.cs` (ligne 421)
  - `MediaMonitor.Service\Web\Handlers\InfoHandler.cs` (lignes 43, 50, 60)
  - Aucun appel dans `MediaMonitor.UI`, `MediaMonitor.Tray`, `MediaMonitor.Core`
- [x] **`MediaMonitor.Core.csproj`** : retiré `<Content Include="ffmpeg\**\*">`
- [x] **`MediaMonitor.Service.csproj`** : ajouté `<Content Include="..\MediaMonitor.Core\ffmpeg\**\*" Link="ffmpeg\...">`
- [x] **`MCEMonitorInstaller.iss`** : ajouté `[InstallDelete]` pour `{autopf}\MCEMonitor\ffmpeg`
- [x] **Aucun changement** dans `build.bat` (publish transitif OK)
- [x] **Aucun changement** dans `FileAnalyzer.cs` (`AppContext.BaseDirectory` correct)
- [x] **Vérifications validées** :
  - `ProgramData\ffmpeg\` existe (utilisé par le service)
  - `ProgramFiles\ffmpeg\` n'existe plus
  - `ProgramFiles\MediaMonitor.Core.dll` existe toujours
  - Test par renommage `ProgramFiles\ffmpeg` → `ffmpeg_OLD` : tout fonctionne
  - Installeur **-50 Mo**

#### Fix WinDivert 1058 (W10)

- [x] **Diagnostic** : clé orpheline `HKLM\SYSTEM\CurrentControlSet\Services\WinDivert` avec `Start=0x4` (DISABLED) laissée par `sc delete` non finalisé
- [x] **Cause** : handle retenu par **SmartScreen** sur `WinDivert64.sys` (machine Q9550 lente)
- [x] **Nettoyage manuel** sur W10 : `reg delete "HKLM\SYSTEM\CurrentControlSet\Services\WinDivert" /f`
- [x] **`NetMonitor.Service\Program.cs`** : `EnsureWinDivertClean()` appelé avant `_engine.Start()`
  - Détecte la clé orpheline via `Registry.LocalMachine.OpenSubKey`
  - `sc delete` + `Thread.Sleep(1500)`
  - Suppression directe via `DeleteSubKeyTree` si la clé persiste
- [x] **`MCEMonitorInstaller.iss`** : `WaitForWinDivertCleanup(10000)` dans `[Code]` avec `RegKeyExists` (boucle 500 ms)
- [x] **`sc query WinDivert` vide** et `Start=0x4` → comportement **normal** de WinDivert au repos
- [x] **Vérifications validées** :
  - Plus d'erreur 1058 dans les logs
  - `WinDivert : capture démarrée avec le filtre 'ip'`
  - `Session ETW démarrée (NetworkTCPIP)`
  - `ImagePath` = `C:\ProgramData\MCEMonitor\runtimes\win-x64\native\WinDivert64.sys`
  - Fonctionne **sans reboot** sur Q9550/W10 comme sur Ryzen 7/32 Go

**Résultat global** : ~50 Mo économisés, 1 fichier de code mort supprimé, 3 packages obsolètes retirés, 3 bugs corrigés (vignettes MP3, WinDivert 1058, dépendances mortes), 0 régression sur 2 machines.

### Session 2026-11-06 — Protection `*.web.config` en lecture seule