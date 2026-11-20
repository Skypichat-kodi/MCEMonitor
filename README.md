# MCEMonitor

**Outil de supervision et d'automatisation pour Media Server** — Windows, C# / .NET 8.

MCEMonitor surveille une machine (ou un réseau local) et alerte par email en cas de problème :
espace disque et santé des supports, charge CPU/RAM/GPU, lectures multimédias, tentatives
d'intrusion réseau. Chaque domaine est un **module** autonome, pilotable depuis une interface
WPF, une icône de notification et une interface web.

---

## Sommaire

1. [Modules](#1-modules)
2. [Architecture](#2-architecture)
3. [Prérequis](#3-prérequis)
4. [Structure du dépôt](#4-structure-du-dépôt)
5. [Compilation](#5-compilation)
6. [Installation](#6-installation)
7. [Emplacements sur le disque](#7-emplacements-sur-le-disque)
8. [Configuration](#8-configuration)
9. [Interfaces web](#9-interfaces-web)
10. [Clients Windows et Android](#10-clients-windows-et-android)
11. [Internationalisation](#11-internationalisation)
12. [Outils annexes](#12-outils-annexes)
13. [Décisions d'architecture](#13-décisions-darchitecture)
14. [Dépannage](#14-dépannage)
15. [Documentation et liens](#15-documentation-et-liens)
16. [État du projet](#16-état-du-projet)
17. [Licence](#17-licence)

---

## 1. Modules

| Module | Rôle |
|---|---|
| **SystemMonitor** | Ressources matérielles : CPU, RAM, GPU, réseau (charge, températures via LibreHardwareMonitor + PawnIO), écrans bleus et minidumps, principaux processus consommateurs. |
| **RomMonitor** | Supports physiques : HDD, SSD, NVMe. Remplissage, températures et santé SMART (`smartctl`). |
| **MediaMonitor** | Lectures multimédias en SMB/LAN : images, films, séries, musique (FFmpeg, TagLib, liens TMDB/Discogs), canaux TV et enregistrements DVBViewer. Sauvegardes et rapports PDF. |
| **NetMonitor** | Intrusions réseau : capture WinDivert, GeoIP, détection de beaconing (C2), scans de ports, connexions sortantes suspectes, blocage pare-feu, liste noire IPsum. |
| **WakeMonitor** | Diagnostic des réveils de la machine : WOL, minuteurs, périphériques USB, causes kernel. |
| **StopMonitor** | Journalisation des arrêts et démarrages (pannes, coupures). |
| **On/Off** | Planification des arrêts, veilles et redémarrages (tâches planifiées). |
| **MCEMonitorClient** | Client Windows déporté : surveille plusieurs serveurs MCE et affiche leurs notifications. |

Tous les modules envoient leurs rapports par email et peuvent exporter en PDF.

## 2. Architecture

Chaque module de monitoring est composé de **trois briques**, plus une interface web intégrée :

```
┌─────────────────────────────────────────────────────────────────┐
│  <Module>.Service.exe      tâche planifiée, tourne en session 0 │
│     • collecte (ETW, SMB, SMART, capture réseau…)               │
│     • serveur IPC (named pipe)  ←  <Module>.UI.exe (WPF)         │
│     • serveur web (HttpListener) ← navigateur / client Android   │
│     • envoi email + export PDF                                  │
└─────────────────────────────────────────────────────────────────┘
        ↑ tâche planifiée                ↑ tâche planifiée
   <Module>.UI.exe (WPF)            <Module>.Tray.exe (zone de notification)
```

- **Communication UI ↔ Service** : IPC par named pipe, jamais de HTTP.
- **Interface web** : `HttpListener` autonome (aucune dépendance ASP.NET), pages HTML
  rechargées depuis le disque à chaque démarrage du service.
- **Notifications** : le Tray interroge le service par IPC et affiche une popup + un son.
- **Autonomie** : les modules ne dépendent pas les uns des autres. Seul
  `MCEMonitor.Languages` est partagé entre les interfaces.

## 3. Prérequis

| | |
|---|---|
| Système | Windows 10, Windows 11 ou Windows Server (x64) |
| Runtime | **.NET 8 Desktop Runtime** (les binaires ne sont pas auto-suffisants) |
| RAM | 8 Go minimum (vérifié par l'installeur) |
| Disque | ~400 Mo |
| Droits | Administrateur (création des tâches planifiées et des règles pare-feu) |
| Optionnel | **PawnIO** pour les températures CPU/GPU (installé par l'installeur, `redist\PawnIO_setup.exe`) |

Pour **compiler** : SDK .NET 8 (`global.json` épingle 8.0.0 avec `rollForward: latestFeature`)
et, pour l'installeur, **Inno Setup 6** (`ISCC.exe`, chemin attendu par défaut :
`C:\Program Files (x86)\Inno Setup 6\ISCC.exe`).

## 4. Structure du dépôt

| Dossier | Contenu |
|---|---|
| `MCEMonitor/` | Application principale (WinForms + Krypton), pilotage de tous les modules, page Email, On/Off, À propos. |
| `SystemMonitor/` | Module matériel : `SystemMonitor.Service`, `.UI`, `.Tray`. |
| `RomMonitor/` | Module disques : `RomMonitor.Service`, `.UI`, `.Tray`. |
| `MediaMonitor/` | Module multimédia : `MediaMonitor.Core`, `.Service`, `.UI`, `.Tray`. |
| `NetMonitor/` | Module réseau : `NetMonitor.Service`, `.UI`, `.Tray`. |
| `StopMonitor/`, `WakeMonitor/` | Services console (arrêts/démarrages, diagnostic des réveils). |
| `MCEMonitorClient/` | Client Windows déporté : `Config`, `Service`, `Tray`. |
| `MCEMonitor.Languages/` | i18n : `LanguageManager`, `TrExtension` (XAML), `HTMLTranslator`, et les 4 fichiers JSON. |
| `Autotrad/` | Outil interne d'extraction et de traduction des chaînes (XAML, C#, HTML). |
| `AndroidClient/` | Client mobile MAUI + bibliothèque partagée. |
| `docs/` | Site GitHub Pages. |
| `Installer/` | Installeurs produits (hors dépôt Git). |

Fichiers notables à la racine : `build.bat` (chaîne de compilation), `BuildAndroid.bat`,
`MCEMonitorInstaller.iss` (Inno Setup), `audit-process.ps1` (contrôle de robustesse des
processus externes), `GitVersion.yml` (versionnement `vX.Y.Z`), `TODO.md` (travaux en cours).

## 5. Compilation

```bat
build.bat
```

Le script, à lancer **en administrateur** (il s'auto-élève) :

1. sauvegarde la configuration Autotrad, puis **vide** le dossier de sortie
   `MCEMonitor Ver 1.0\` ;
2. arrête tous les exécutables MCEMonitor pour libérer les fichiers ;
3. crée automatiquement un tag Git tous les **100 commits** depuis le dernier tag
   (`git describe` + `git push origin vX.Y.Z`) ;
4. exécute **21 `dotnet publish -c Release`** vers :
   - `MCEMonitor Ver 1.0\ProgramFiles` → binaires installés dans Program Files
     (UI, Tray, application principale) ;
   - `MCEMonitor Ver 1.0\ProgramData` → binaires installés dans ProgramData
     (services, langues, templates web) ;
   - `MCEMonitor Ver 1.0\Tools\Autotrad` → outil de traduction (single-file) ;
5. lance `audit-process.ps1` (vérifie la robustesse face aux processus externes) ;
6. propose de compiler l'installeur Inno Setup, en récupérant la version depuis
   `MCEMonitor.exe`.

```bat
BuildAndroid.bat
```

Compile le client Android (MAUI). La clé `mcemonitor.keystore` n'est pas versionnée.

## 6. Installation

L'installeur Inno Setup (`MCEMonitorInstaller.iss`) propose deux composants :
**core** (application, services, trays, UI) et **client** (client Windows déporté).

Points à connaître :

- l'installeur **arrête** les processus, supprime les tâches planifiées et le pilote
  WinDivert avant de copier les fichiers, puis attend la libération du pilote ;
- le pilote WinDivert (`WinDivert64.sys`) est déployé par du **code** : il n'est remplacé
  que s'il diffère et s'il n'est pas verrouillé par un chargement en cours ;
- les fichiers `*.web.config` sont protégés en lecture seule (`icacls`) ;
- la RAM est vérifiée (8 Go minimum) ;
- **les tâches planifiées ne sont pas créées par l'installeur** : c'est MCEMonitor qui les
  crée (`schtasks /Create`) lorsque tu actives un module ou son icône de notification.
  L'installeur ne fait que les supprimer à la désinstallation.

## 7. Emplacements sur le disque

| Chemin | Contenu |
|---|---|
| `C:\Program Files\MCEMonitor\` | `MCEMonitor.exe`, interfaces WPF, icônes de notification, `MCEMonitor.Languages.dll` |
| `C:\ProgramData\MCEMonitor\` | `*.Service.exe`, `Templates\` (pages web), `Languages\`, `Logs\`, `runtimes\` (WinDivert, ffmpeg), `Tools\` (smartctl), `Data\` (GeoIP) |
| `C:\ProgramData\MCEMonitor\Logs\` | `MediaMonitor.Service.log`, `NetMonitor.Service.log`, `SystemMonitor.UI.crash.log`, etc. |
| `C:\ProgramData\MCEMonitor\*.config` | configuration de chaque module |
| `C:\ProgramData\MCEMonitor\*.web.config` | configuration des serveurs web |
| `C:\ProgramData\MCEMonitor\language.config` | langue de l'application (ex. `fr-FR`) |

## 8. Configuration

Chaque module lit son fichier `Xxx.config` (clé=valeur, commentaires en `#`) au démarrage et
le recharge à chaud à chaque modification. Les valeurs par défaut sont dans le code.

Les interfaces web ont leur propre fichier `Xxx.web.config` : activation, port, identifiants,
et pour NetMonitor les paramètres OTP.

La langue est commune : `language.config` contient un code (`fr-FR`, `en-GB`, …) lu par
toutes les interfaces et tous les services. À défaut, le français est utilisé.

La configuration **SMTP est également commune** à tous les modules : un seul fichier
`email.config` sert à toutes les alertes et à l'envoi des OTP.

## 9. Interfaces web

Chaque service embarque un serveur HTTP autonome, désactivable, avec sa propre page :

| Module | Port par défaut | Authentification |
|---|---|---|
| MediaMonitor | 8081 | login + session (15 min) |
| SystemMonitor | 8083 | login + session (15 min) |
| RomMonitor | 8085 | login + session (15 min) |
| NetMonitor | 8086 | login + session + **OTP par email** (optionnel) |

> Les ports ci-dessus sont ceux du code ; le fichier `*.web.config` peut les changer.

- **Sessions** : cookie `HttpOnly`, `SameSite=Strict`, 15 minutes, qualifié par le port
  (deux instances sur le même hôte ne se marchent pas dessus).
- **Appareil de confiance** : option « Se souvenir de cet appareil » → 30 jours, mémorisé
  côté service (`trusted_devices.json`) et donc conservé après redémarrage.
- **OTP** (NetMonitor uniquement) : code à 6 chiffres envoyé par email, valable 5 minutes,
  usage unique, 3 tentatives.
- **Routes API** (`/api/summary`, `/api/detail`, `/peers`, …) : Basic Auth, sans session,
  pour les clients programmatiques.
- **Sécurité** : les actions destructives (vider un historique, purger, débloquer) ne sont
  acceptées qu'en **POST** ; un GET renvoie 404 (ou 405 pour MediaMonitor).
- Les pages sont servies avec `Cache-Control: no-store` : modifier un template puis
  redémarrer le service suffit, sans recompiler.

## 10. Clients Windows et Android

Le **client Windows** (`MCEMonitorClient`) se connecte aux serveurs déclarés dans sa
configuration et affiche leurs notifications. Il s'installe indépendamment du reste et se
compose lui aussi de trois briques : `Config` (saisie des serveurs), `Service` (interrogation
périodique) et `Tray` (notifications). La liste des serveurs est stockée dans
`C:\ProgramData\MCEMonitor\MCEMonitorClient.config`.

Le **client Android** (MAUI) fait de même sur mobile ; il consomme les routes API en lecture
seule. Voir `BuildAndroid.bat`.

## 11. Internationalisation

Quatre fichiers de langue dans `MCEMonitor.Languages\Languages\` :
`fr-FR.json`, `en-GB.json`, `de-DE.json`, `es-ES.json`.

Trois syntaxes coexistent, une par domaine :

| Domaine | Syntaxe | Résolution |
|---|---|---|
| Pages HTML | `{{tr:clé}}` | `TemplateEngine` |
| C# | `LanguageManager.Get("clé") ?? "clé"` | directe |
| XAML | `{loc:Tr 'clé'}` (avec `xmlns:loc`) | `TrExtension` |

- Les clés sont des **phrases françaises** ; à défaut de traduction, la clé s'affiche,
  donc le français reste lisible.
- En **XAML**, une clé ne peut pas contenir d'apostrophe (ni la syntaxe de balisage, ni le
  parseur d'Autotrad ne l'acceptent) : on utilise alors une **clé courte** `Net.Xxx`,
  obligatoirement présente dans `fr-FR.json` **et** `en-GB.json`.
- **Autotrad** (`Tools\Autotrad`) scanne le code (`.cs`, `.xaml`, `.html`, y compris les
  blocs `<script>`) pour extraire les clés et remplir les JSON via un traducteur API.
- `de-DE.json` et `es-ES.json` sont volontairement vides à ce stade.

## 12. Outils annexes

### Autotrad — scanner de traduction

Outil interne (WinForms, publié en single-file dans `Tools\Autotrad`) qui alimente les
fichiers de langue : il scanne le code source pour extraire les clés, puis traduit via
l'API **translateapi.ai** (une clé API est saisie dans l'outil et mémorisée).
Il travaille sur un dossier de langues donné, pour une langue cible donnée, avec un aperçu
avant écriture. Ses propres fichiers de langue sont dans `Autotrad\Languages\`.

### CleanSolution.bat

Supprime les dossiers `bin` et `obj` de toute la solution, en excluant `.git`, `.vs`,
`packages`, `redist` et `node_modules`. Accepte l'argument `dry` pour afficher ce qui
serait supprimé sans rien effacer.

### audit-process.ps1

Contrôle de robustesse face aux processus externes, exécuté automatiquement par `build.bat`
en fin de compilation. Un échec fait échouer le build.

## 13. Décisions d'architecture

Ces choix sont volontaires et structurants ; ils expliquent plusieurs détails du code.

- **Modules autonomes.** Chaque module est indépendant et a vocation à devenir un *add-on*
de MCEMonitor. Conséquence assumée : les styles et ressources WPF sont **dupliqués** dans
chaque fenêtre plutôt que mutualisés, pour éviter tout couplage entre modules.
- **Tâches planifiées plutôt que services Windows.** Les modules tournent via `schtasks`,
crées par l'application. Cela simplifie la désinstallation et évite un service noyau
supplémentaire.
- **HTTPS abandonné** pour les interfaces web : en réseau local privé, le certificat
auto-signé imposait une installation manuelle sur chaque navigateur pour un gain marginal.
La sécurité repose sur Basic Auth + session + OTP (NetMonitor) et sur le filtrage réseau.
- **Pages web chargées depuis le disque** : les templates sont lus dans
`ProgramData\MCEMonitor\Templates` et mis en cache en mémoire. Modifier un template impose
donc un redémarrage du service, mais aucune recompilation.
- **Aucun cache navigateur** : les pages sont servies en `no-store`, pour éviter d'exécuter
un JavaScript périmé après une mise à jour.

## 14. Dépannage

| Symptôme | Piste |
|---|---|
| Un module ne démarre pas | Vérifier sa tâche planifiée : `schtasks /Query /TN "MCEMonitor_<Module>Service"` |
| Page web inaccessible | Vérifier `WebEnabled` et `WebPort` dans `Xxx.web.config`, et la règle pare-feu du port |
| Page web qui « ne change pas » après un build | `Ctrl+F5` (les pages sont normalement en `no-store`), puis redémarrer le service |
| Capture réseau inactive | Pilote WinDivert bloqué (antivirus, `STOP_PENDING`) : voir le journal `NetMonitor.Service.log` |
| Températures absentes | PawnIO non installé |
| Données GeoIP absentes pendant 24 h | Quota de l'API atteint : le service reprend automatiquement (mention `[GEOIP] Quota épuisé` dans le journal) |

Les journaux sont dans `C:\ProgramData\MCEMonitor\Logs\`.

## 15. Documentation et liens

- Site du projet : <https://skypichat-kodi.github.io/MCEMonitor/>
- Sources du site : `docs/` (GitHub Pages)
- Suivi des travaux : `TODO.md`

## 16. État du projet

- Versionnement `vX.Y.Z`, tag automatique tous les 100 commits (`GitVersion.yml`).
- Le client Android est en pause.
- Le `TODO.md` recense les chantiers en cours et les points d'hygiène connus.

## 17. Licence

**MIT** — voir `LICENSE`. Le script d'installation Inno Setup est couvert séparément par
`LICENSE_inno.txt`.
