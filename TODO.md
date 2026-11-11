# TODO MCEMonitor

TODO global du projet, organisé par thème.

---

## 🔧 En cours

_(rien)_

## 📋 À faire

### 🔴 Sécurité — Audit et durcissement

- [ ] **Auditer les `MiniHttpServer` / `WebServer` des 4 modules**
  - **Contexte** : l'UI Android doit être limitée à la consultation
  - **Déjà fait** :
    - SystemMonitor : ✅ audité (le `/clear-history` en GET destructif reste mineur, sauvegardé par PDF)
    - NetMonitor : ✅ audité (les actions sont bien en POST, l'Android n'y touche pas) + OTP sur les actions web (2026-11-11)
  - **À faire** :
    - RomMonitor : vérifier `/clear-history`, `/peers`, autres routes
    - MediaMonitor : vérifier `/purge`, `/clear`, `/download`, `/logo`, `/report`
  - **Test** : `curl -u user:pass http://serveur:PORT/` ne doit exposer aucune opération critique en GET
  - **Coût** : 30 min
  - **Priorité** : 🔴 haute (vérification, pas de code à écrire si tout est bon)

- [ ] **Passer les routes destructives de GET à POST**
  - **Routes concernées** :
    - NetMonitor : `/clear-active`, `/clear-history`, `/clear-audit`, `/clear-blocked-retries`
    - SystemMonitor : `/clear-history`
    - RomMonitor : `/clear-history` (à vérifier)
    - MediaMonitor : `/purge`, `/clear` (à vérifier)
  - **Risque actuel** : CSRF possible si l'utilisateur a un cookie OTP valide et qu'un attaquant lui fait cliquer sur un lien (`<img src="http://netmon:14003/clear-history">` par exemple)
  - **Solution** : passer en POST + bouton `<form method="post">` dans les pages HTML. L'OTP protège déjà contre le brute-force, mais pas contre le CSRF.
  - **Coût** : 1-2 h (4 modules)
  - **Priorité** : 🟡 moyenne (OTP atténue le risque)

### 🟡 Planifié

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

- [ ] **Ajouter les exclusions Defender** sur la machine W10 Q9550
  - `Add-MpPreference -ExclusionPath "C:\ProgramData\MCEMonitor"`
  - `Add-MpPreference -ExclusionProcess "NetMonitor.Service.exe"`
  - `Add-MpPreference -ExclusionExtension ".sys"`
  - → Évite à SmartScreen de retenir le handle de `WinDivert64.sys` au démarrage (démarrage 2-3× plus rapide)

---

## ✅ Terminé récent

### Session 2026-11-11 — OTP par email sur NetMonitor

**Authentification à deux facteurs (Basic Auth + code OTP email) pour les actions mutatives web de NetMonitor.**

#### Nouveaux fichiers

- [x] **`OtpService.cs`** : génération 6 chiffres (`RandomNumberGenerator`), envoi SMTP via `EmailSender.SendAsync`, validation `FixedTimeEquals`, 3 tentatives max, expiration 5 min, single-use, purge.
- [x] **`OtpSessionStore.cs`** : sessions 15 min en mémoire (`ConcurrentDictionary`), invalidation à la demande.
- [x] **`OtpPages.cs`** : pages HTML `/otp` (saisie) et `/otp/verify` (erreur), style dark cohérent avec NetMonitor UI.

#### Modifications

- [x] **`MiniHttpServer.cs`** :
  - Routes publiques `/otp`, `/otp/verify`, `/logout` (avant Basic Auth).
  - Garde OTP sur toutes les routes non exemptées (pages HTML + actions mutatives).
  - `otpExempt` : `/ping`, `/api/summary`, `/api/detail`, `/api/stats`, `/peers`, `/resources/*`, `/data/*`, `/favicon.ico` (clients API non bloqués).
  - Fallback localhost + IP LAN du serveur (si SMTP down).
  - Bouton `/logout` : page « Déconnecté » + redirection auto vers `/`.
- [x] **`NetMonitorSettings.cs`** : +3 propriétés `OtpEnabled` / `OtpSessionMinutes` / `OtpCodeLength` (Load/SaveWeb/Reload).
- [x] **`NetMonitor.web.config`** : +3 lignes (défaut `OtpEnabled=false`).
- [x] **`PeerStatusService.cs`** : bouton « 🚪 Déconnexion » dans la peerbar (toutes les pages).
- [x] **`MainWindow.xaml`** : `ToggleOtp` dans la section Serveur Web (remplace « Ouvrir dans le navigateur »).
- [x] **`MainWindow.xaml.cs`** : handlers `ToggleOtp_Checked` / `ToggleOtp_Unchecked` + `ApplyOtpConfig()`.

#### Comportement

- `OtpEnabled=false` → Basic Auth seul (inchangé).
- `OtpEnabled=true` → Basic Auth + OTP email sur toutes les pages web et actions mutatives.
- Les clients API (`PollingEngine`, Android, `PeerStatusService`) ne sont **pas** impactés (routes exemptées).
- L'UI locale NetMonitor (`NetMonitor.UI.exe`) passe par IPC, pas HTTP → pas d'OTP (accès physique + UAC).
- Fallback localhost / IP LAN : bypass OTP en cas de SMTP down.

#### Décision actée — HTTPS abandonné

- [x] **HTTPS — ABANDONNÉ** (décision 2026-11-11)
  - **Contexte** : LAN privé. Le certificat auto-signé impose une installation manuelle sur chaque navigateur (Chrome + Firefox séparément) pour un gain marginal en LAN.
  - **Alternatives retenues** : Basic Auth + OTP email + contrôle d'accès réseau (pare-feu, pas d'exposition Internet).
  - **Code reverté** : `HttpsSetupForm`, `CertificateHelper`, `WebUseHttps`/`WebCertThumbprint` supprimés. Le `MiniHttpServer` de NetMonitor est revenu à `http://` en dur.
  - **Si besoin futur** : certificat Let's Encrypt (nécessite un domaine public) ou AC interne (AD CS) — hors périmètre.

#### Tests validés

- [x] Navigation privée → Basic Auth → OTP → page NetMonitor → actions protégées.
- [x] `curl /api/summary` → 200 OK sans OTP (clients API non bloqués).
- [x] Bouton Déconnexion → page « Déconnecté » → OTP redemandé.
- [x] Fallback localhost / IP LAN : bypass OTP si SMTP down.
- [x] Toggle OTP dans l'UI NetMonitor : ON/OFF → `NetMonitor.web.config` mis à jour → service rechargé à chaud.

### Session 2026-11-10 — Vérifications, correctifs Android, nettoyages

**Session de validation complète : tests réactifs, correctifs Android, nettoyages opportunistes, et audit de code.**

#### Correctifs Android

- [x] **`requestCode` unique par notification** dans `AndroidNotificationService.cs`
  - **Cause** : `PendingIntent.GetActivity(context, 0, ...)` avec `requestCode = 0` fixe → Android considère toutes les notifications comme le même `PendingIntent` (`filterEquals` ignore les extras)
  - **Conséquence** : seule la dernière notification écrasait les précédentes → clic sans effet sur les plus anciennes
  - **Correction** : `int notifId = _nextId++;` puis `PendingIntent.GetActivity(context, notifId, ...)` et `manager.Notify(notifId, ...)`
  - Corrigé dans `ShowAlert` et `ShowMedia`
- [x] **Lecture du champ `service` dans `PollingEngine.PollOneAsync`**
  - **Cause** : `result.ServiceType` était initialisé depuis `ServerEntry.ServiceType`, mais jamais relu depuis le JSON `/api/summary`. Si le serveur avait été ajouté avant la détection automatique (cas MediaMonitor), `ServiceType` restait vide → pas de route résolue → clic sans effet
  - **Correction** : lecture de `"service"` depuis le JSON, prioritaire sur `ServerEntry.ServiceType`
  - Doublon accidentel de la lecture `status` supprimé (CS0128)
- [x] **Affichage de la version GitVersion dans `MainPage`**
  - **Cause** : `AppInfo.Current.VersionString` lit `ApplicationDisplayVersion` statique du `.csproj` (valeur `1.0`), pas la version calculée par GitVersion
  - **Correction** : lecture de `AssemblyInformationalVersionAttribute` (injecté par GitVersion.MsBuild), pattern identique à `MCEMonitor` (WPF)
  - Coupure des métadonnées après `+` (ex : `1.2.3+5.Branch.main` → `1.2.3`)
  - Label `lblAppVersion` ajouté en haut à droite de `MainPage.xaml`

#### Tests réactifs (déclencheurs validés)

- [x] **Test end-to-end de la suppression des règles orphelines**
  - Procédure validée : créer 2 règles `NetMonitor-Block-01-In/Out` via `netsh`, redémarrer le service, cliquer "Libérer toutes les IP bloquées", vérifier la suppression
- [x] **Test end-to-end de la préservation des JSON** à travers désinstallation/réinstallation
  - Les 6 JSON + `NetMonitor.config` + 4 `*.web.config` survivent à la désinstallation/réinstallation
- [x] **Test notifications Android** sur les 4 services
  - Les 4 services ouvrent leur page de détail respective (SystemMonitor, RomMonitor, NetMonitor, MediaMonitor)
  - Testé avec notifications récentes et anciennes (après correctif `requestCode`)

#### Planifiés validés

- [x] **Spinner de chargement** : NetMonitor l'a, imperceptible en temps normal (serveur rapide), visible si lent
- [x] **`ProjectReference` vers `MCEMonitor.Languages`** dans les 3 UI : vérifié, présent
- [x] **Workflow ToCheck documenté** dans le README
- [x] **`_manualCriticalOverrides` persisté** dans `NetMonitorEngine` (`manual_critical_overrides.json`)
- [x] **Canal Android `mcem_tocheck` dédié** : en place avec son distinct
- [x] **MediaMonitor — tags ID3** dans `/api/detail` : `Artist` / `Album` / `Title` remplis

#### Nettoyages opportunistes

- [x] **Dossiers vides supprimés** : `MCEMonitor\Controls\`, `MCEMonitor\Resources\Images\`, `NetMonitor\NetMonitor.Service\Api\`, `RomMonitor\RomMonitor.UI\Converters\`
- [x] **`MainWindow.xaml.txt`** dans `MediaMonitor.UI` : supprimé (fichier orphelin)
- [x] **Fichiers template MAUI** : `dotnet_bot.png` et `AboutAssets.txt` supprimés
- [x] **`LICENSE` vs `LICENSE.txt`** : `LICENSE.txt` supprimé, `LICENSE` conservé
- [x] **`MCEMonitor.png`** à la racine : supprimé (doublon dans `WakeMonitor\Assets\`)
- [x] **Ressources `MediaMonitor.Service\Resources\`** : auditées
- [x] **DLL de `ffmpeg\`** : auditées via `dumpbin /dependents`, inutiles supprimées
- [x] **5-6 MP3 variés** re-testés après fix vignettes (APIC JPEG / PNG / corrompu / folder.jpg / sans rien)

**Bilan de la session** : les 4 services Android sont pleinement fonctionnels (consultation + navigation depuis notifications récentes et anciennes), la version GitVersion s'affiche dans l'UI, les tests réactifs sont validés, et le projet est nettoyé des fichiers orphelins.

### Session 2026-11-09 — Optimisations NetMonitor + validation ToCheck

**Suppression des timers de rafraîchissement automatique, filtres 24 h, limites d'affichage, spinner de chargement, et validation du workflow ToCheck sur les 3 plateformes.**

- [x] **Timers supprimés sur les 4 services** : rafraîchissement uniquement à l'ouverture + pull-to-refresh
- [x] **NetMonitor — IP bloquées** : filtre 24 h + limite aux 10 plus récentes + compteur « + N autres »
- [x] **NetMonitor — Retries** : limite à 5, triés par nombre de tentatives décroissant
- [x] **NetMonitor — Whitelist** : limite à 10 + compteur « + N autres »
- [x] **NetMonitor — Header** : `X active(s) · Y bloquée(s) / 24 h · Z au total`
- [x] **NetMonitor — Spinner** : `ActivityIndicator` centré pendant le chargement initial
- [x] **Bilan** : ouverture de `NetMonitorDetailPage` « beaucoup plus rapide », plus de lag
- [x] **Workflow ToCheck validé** sur les 3 plateformes (WPF, Tray Windows, Android)
  - Alerte Microsoft-like non vérifiée → ToCheck
  - `Safe ?` → migration vers audit vert
  - Classification des futures alertes sur la même IP correcte
  - **Post-requis confirmé** : `MicrosoftIpRangeEnabled` remis à `true` dans `NetMonitor.config`

### Session 2026-11-08 — Client Android : 4 services en consultation

**Pages de détail Android pour les 4 services, navigation depuis la liste et les notifications, `SecureStorage`.**

- [x] **`SecureStorage`** pour les identifiants (remplace JSON en clair)
- [x] **`android:allowBackup="false"`**
- [x] **Cache mémoire `ConfigService`** (gain notable avec 14 serveurs)
- [x] **SystemMonitor** : `ApiDetail` + route `/api/detail` + `SystemMonitorDetailPage` (CPU, RAM, GPU, réseaux, BSOD)
- [x] **RomMonitor** : `ApiDetail` + route `/api/detail` + `RomMonitorDetailPage` (volumes, SMART)
- [x] **NetMonitor** : `ApiDetail` + route `/api/detail` + `NetMonitorDetailPage` (intrusions, IP bloquées, retries, whitelist, stats)
- [x] **MediaMonitor** : `ApiDetail` + route `/api/detail` + `MediaMonitorDetailPage` (live, dernière image, historique filtré, stats)
- [x] **Menu contextuel** sur tap : Consulter / Modifier / Supprimer (popup `CommunityToolkit.Maui`)
- [x] **Navigation depuis notification** (les 4 services ouvrent leur page de détail)
- [x] **`ServerDetailRouter`** extensible par `ServiceType`
- [x] **`MainActivity.OnNewIntent`** + `HandleNotificationIntent` pour la navigation
- [x] **Bugs corrigés** : `ShowAlert`/`ShowMedia` pointaient vers l'URL web, `TabbedPage` incompatible MAUI Shell, `StaticResource` non déclaré, namespaces incorrects, `using System.Linq` manquant

### Session 2026-11-08 — GeoIP : cache, quota, refresh + fix ScannerPatterns

**Arrêt des re-résolutions GeoIP inutiles, pause 24 h sur 429, rotation 50 IPs / 12 h, `ScannerPatterns` stable.**

- [x] **`SeedCache`** / **`InvalidateCache`** / **`IsQuotaExceeded`** dans `GeoIpService`
- [x] **Pause 24 h sur HTTP 429** (`_quotaExceededUntil`)
- [x] **Backfill au démarrage** depuis `blocked_ips.json` et `alert_history.json`
- [x] **`RefreshOldGeoIps()`** : 50 IPs / 12 h, priorité aux jamais résolues
- [x] **`Throttle`** réduit (2 au lieu de 5)
- [x] **`ScannerPatterns`** : garde-fou dans `Load`, `Reload`, `Save`, plus constante `DefaultScannerPatterns`
- [x] **`SaveBlockedIps()`** : écriture atomique (plus de race condition)
- [x] **Bilan** : consommation GeoIP divisée par ~10, plus de 429 en rafale

### Vérifications one-shot

- [x] **`mcemonitor.keystore` n'est pas versionné** (vérifié 2026-11-09)

---

## ✅ Terminé antérieur (résumé)

- **2026-11-06** : nettoyage `MediaMonitor.Core` (4 packages → 2, ~50 Mo économisés), fix vignettes MP3, fix WinDivert 1058, protection `*.web.config` en lecture seule