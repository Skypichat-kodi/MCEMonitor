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
    - SystemMonitor : ✅ audité (`/clear-history` en GET destructif reste mineur, sauvegardé par PDF)
    - RomMonitor : ✅ audité (`/clear-history` en GET destructif)
    - NetMonitor : ✅ audité (actions en POST) + OTP sur les actions web
    - MediaMonitor : ✅ audité partiellement (`/purge`, `/clear` en GET destructif à vérifier)
  - **Test** : `curl -u user:pass http://serveur:PORT/` ne doit exposer aucune opération critique en GET
  - **Priorité** : 🔴 haute (vérification, pas de code à écrire si tout est bon)

- [ ] **Passer les routes destructives de GET à POST**
  - **Routes concernées** :
    - NetMonitor : `/clear-active`, `/clear-history`, `/clear-audit`, `/clear-blocked-retries`
    - SystemMonitor : `/clear-history`
    - RomMonitor : `/clear-history`
    - MediaMonitor : `/purge`, `/clear`
  - **Risque actuel** : CSRF possible si l'utilisateur a une session valide et qu'un attaquant lui fait cliquer sur un lien (`<img src="http://netmon:14003/clear-history">`)
  - **Solution** : passer en POST + bouton `<form method="post">` dans les pages HTML. Le login protège contre le brute-force, mais pas contre le CSRF.
  - **Coût** : 1-2 h (4 modules)
  - **Priorité** : 🟡 moyenne (login atténue le risque)

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