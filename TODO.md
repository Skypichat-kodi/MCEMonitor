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

### 🟠 Réactif — déclenché par un événement

> Ces tâches ne sont pas urgentes en soi, mais **doivent être prêtes à être exécutées** dès que l'événement déclencheur se produit.

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

- [ ] **Spinner de chargement sur les 3 autres services**
  - NetMonitor l'a (imperceptible en temps normal, visible si serveur lent)
  - À ajouter sur SystemMonitor, RomMonitor, MediaMonitor si lenteurs constatées
  - Non prioritaire : ces pages répondent vite, le spinner serait invisible
  - **Priorité** : 🟢 opportuniste

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
  - Extraire le header, `RefreshView`, cycle de vie dans une classe de base
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

## ✅ Terminé récent

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