# TODO MCEMonitor

TODO global du projet, organisé par thème.

---

## 🧭 Règle de méthode

### Parité des migrations d'interface

Une migration (WinForms → WPF, refonte d'un onglet, remplacement d'un contrôle) ne peut pas être
déclarée terminée sans un **tableau de parité** établi par recherche dans le code — jamais de mémoire.

À vérifier pour chaque fonction de l'ancienne interface :

- appels IPC aux services (`...IpcClient.XxxAsync`) ;
- opérations sur les tâches planifiées (`TaskSchedulerHelper.*`) ;
- fichiers de configuration lus et écrits ;
- services démarrés, arrêtés, rechargés à chaud ;
- envois de mail, exports, confirmations.

Relevé de l'ancienne interface :

```bash
grep -o "[A-Za-z]*IpcClient\.[A-Za-z]*" MCEMonitor.old/MainForm.cs | sort -u
grep -o "TaskSchedulerHelper\.[A-Za-z]*" MCEMonitor.old/MainForm.cs | sort -u
```

Puis vérifier chaque résultat dans la nouvelle interface. Audit du 2026-11-22 : 23 opérations sur
tâches retrouvées, 2 appels IPC et 1 lecture du mode de la tâche perdus — corrigés.

Deuxième audit, même jour, après deux régressions signalées en usage :

```bash
grep -o '"[A-Za-z0-9._]*\.exe"' MCEMonitor.old/MainForm.cs | sort -u
grep -oE "private +(async +)?void +[A-Za-z0-9_]+" MCEMonitor.old/MainForm.cs | sort -u
```

À vérifier également, en plus des appels IPC et des tâches :

- **les `Process.Start`** — lancement des Tray, des interfaces de modules, des dossiers, des pages web ;
- **les tâches `ONLOGON`** des Tray, distinctes des tâches de service (`ONSTART`) ;
- **les gestionnaires d'événements** (boutons, cases, minuteries), un par un, avec leur action attendue.

Constat du 2026-11-22 : les boutons « Ouvrir … » ne lançaient plus les Tray et les boutons
« Automatique » ne recréaient plus leurs tâches `ONLOGON`. Ces deux points ne ressortaient pas de la
première requête, qui ne cherchait que les classes `*IpcClient` et `TaskSchedulerHelper`.

### Chaînes traduisibles

Toute chaîne visible ajoutée ou modifiée dans une interface — `{loc:Tr '…'}` dans le XAML,
`LanguageManager.Get("…")` dans le C# — doit exister dans **`fr-FR.json` et `en-GB.json`**.

Le `LanguageManager` retombe silencieusement sur la clé : un oubli ne casse rien et n'écrit aucune
erreur, il affiche simplement du français à un utilisateur anglophone. Le défaut est donc invisible
sans contrôle explicite.

Contrôle à faire sur chaque fichier d'interface modifié :

```bash
for f in MCEMonitor.UI/*.xaml; do
  grep -ho "loc:Tr '[^']*'" "$f" | sed "s/loc:Tr '//; s/'$//" | sort -u
done | while IFS= read -r c; do
  grep -qF "\"$c\"" MCEMonitor.Languages/Languages/fr-FR.json || echo "fr-FR manque : $c"
  grep -qF "\"$c\"" MCEMonitor.Languages/Languages/en-GB.json || echo "en-GB manque : $c"
done
```

Le contrôle est muet quand tout est traduit. Il signale normalement six lignes, qui ne sont **pas** des
oublis : `MediaMonitor`, `NetMonitor`, `RomMonitor`, `StopMonitor`, `SystemMonitor` et `WakeMonitor`
sont des noms propres de modules, affichés tels quels dans les deux langues et volontairement absents
des fichiers de traduction. Tout autre nom signalé est un vrai oubli.

Les clés sont des phrases françaises : les insérer dans les deux fichiers dans l'ordre alphabétique du
bloc des phrases (pas celui des clés techniques `App.Title`, `Net.BlockIp`…), en conservant le BOM et
les fins de ligne CRLF.

Rappel de déploiement : l'application lit les traductions depuis
`C:\ProgramData\MCEMonitor\Languages`, alimenté par `build.bat` et l'installeur. Une clé ajoutée n'a
donc aucun effet tant que l'application n'a pas été recompilée puis réinstallée.

Constat du 2026-11-22 : 15 clés absentes (surcharge disque de RomMonitor, libellés de `ServiceReload`),
invisibles en français à cause du repli. Ajoutées en `fr-FR` et `en-GB` (commit `8208797`).

---

## 🔧 En cours

_(rien)_

## 📋 À faire

### 🔴 Échéance datée

_(rien)_

### 🟡 Planifié

- [ ] **Traduire `de-DE` et `es-ES`** *(via Autotrad)*
  - Les deux fichiers sont vides (`{}`) ; `fr-FR` (743 clés) et `en-GB` (698 clés) sont complets
  - Autotrad remplit les JSON depuis les clés détectées dans le code (XAML, C#, HTML)
  - **Priorité** : 🟡 moyenne (dépend d'une passe Autotrad + clé API)

### 🟢 Opportuniste

- [ ] **Vérifier les fenêtres secondaires des modules en thème clair**
  - Les fenêtres d'aperçu et de détail des quatre modules n'ont été regardées qu'en thème sombre
  - Les boîtes de dialogue, elles, sont désormais thémées : reste à confirmer leur rendu en clair
  - **Priorité** : 🟢 basse

- [ ] **Traduire les libellés du XAML de NetMonitor**
  - Constat de la session du 2026-11-24 : le **code** est traduit (186 messages), mais les chaînes
    `{loc:Tr '…'}` du XAML ne le sont pas. Le contrôle rituel en signale des dizaines :
    « 1ʳᵉ tentative », « Activité par période », « Actions irréversibles. À utiliser uniquement en cas
    de problème majeur… », « Top FAI par pays », etc.
  - Même méthode que pour le code : clés en `fr-FR` **et** `en-GB`, puis compilation
  - **Priorité** : 🟢 basse (l interface reste lisible en français)

- [ ] **Renforcer les ACL de `email.config` et des `*.web.config`**
  - Ces fichiers contiennent les mots de passe SMTP et web. Leur ACL actuelle accorde **`BUILTIN\Utilisateurs : Read`**, donc tout compte local peut les lire
  - `icacls` de l'installeur retire l'héritage mais accorde délibérément la lecture au groupe Utilisateurs : la protection porte sur la *modification*, pas la confidentialité
  - **Piste** : ne garder que `SYSTEM` et `Administrateurs`. Les services tournent en SYSTEM et l'UI en `requireAdministrator`, aucun Tray ne lit ces fichiers (vérifié)
  - **Écarté pour l'instant** : Christophe ne veut pas y toucher. DPAPI couvre déjà le vol de fichier, pas la lecture locale
  - **Priorité** : 🟡 moyenne

---

## ✅ Terminé récent

### Session 2026-11-24 — opportunistes : boîtes, traductions, seuil réglable

**Les boîtes de dialogue.** Les 190 `MessageBox` WinForms des interfaces sont remplacés par une boîte
WPF au style des modules : fond, bordure, accent et icône issus du thème, titre dans l'en-tête, croix
discrète, boutons Oui / Non ou OK. Chaque module porte **sa propre copie** (`Boite.cs` +
`FenetreDialogue`), les modules étant indépendants : rien n est partagé. Les sept `App.xaml.cs` gardent
leur `MessageBox` — ils traitent les exceptions non récupérées au démarrage, avant que les ressources de
thème soient chargées, où une boîte thémée lèverait une seconde exception.

**Les traductions.** 186 messages passent par `LanguageManager`, **148 clés** ajoutées en `fr-FR` et
`en-GB` (908 et 863 au total). Les messages interpolés deviennent des clés à paramètres via
`string.Format`, pour que l ordre des mots reste celui de la langue cible. Les `\n` du source sont
dé-échappés à l écriture, sans quoi `LanguageManager` ne les aurait jamais retrouvés.

**Un piège corrigé en route.** L automatisation avait produit `LanguageManager.Get("X") ?? "X" + suite` :
`??` étant moins prioritaire que `+`, la concaténation se retrouvait avalée et le message tronqué dès que
la clé existait. Fichier restauré, tout parenthésé, vérifié par recherche.

**Le seuil des processus.** La constante `SeuilProcessus` (10 %) devient `ProcessThresholdPercent`,
réglable depuis l onglet Configuration de SystemMonitor.UI, bornée entre 1 et 100, relue à chaud.

**Nettoyages.** `warning.png` et `critical.png` du Tray RomMonitor supprimés (déclarés en ressource, plus
chargés depuis la bascule vers `hdd-critical.png`) ; `Theme.cs` du client de configuration supprimé
(palette WinForms sans lecteur) ; overlay de lecture de la popup MediaMonitor retiré, il se superposait aux
logos de chaîne et d enregistrement.

**Deux items clos sans travail** : le gris des en-têtes était déjà harmonisé à `#D6D6D6` dans les quatre
modules, et les deux `ForceTick demandé via IPC` sont des messages exacts. L aperçu de courbe étendu aux
alertes globales a été **écarté** par Christophe.

### Session 2026-11-24 — liste de confiance lisible et actualisation suspendue

**La colonne « Liste ».** Le tableau des alertes ignorées ne disait pas quelle liste avait retiré
l'alerte. Un champ `TrustedList` porte désormais le nom : Microsoft, Google, ou vide. Distinct de
l'origine, qui décrit la nature de l'IP et mélange scanners, robots et blacklist. Les entrées
antérieures au champ le déduisent de leur origine, pour ne pas afficher un tiret sur une IP dont on sait
pertinemment d'où elle vient.

**Les origines manquantes.** `IpOrigin` ne connaissait que Scanner, Crawler, Bot, Microsoft, Blacklist et
Whitelist : les 135 IP Cloudflare et réseau local de l'audit n'avaient aucune origine, et la colonne
affichait un tiret gris sur fond transparent — une cellule vide. Deux valeurs ajoutées, **Cloudflare**
et **Local**, classées par `SeverityScorer`, avec leurs badges orange et gris bleuté. L historique
d'audit est reclassé au chargement (`[AUDIT] 135 origine(s) précisée(s)` dans le journal). Le cas
réellement indéterminé affiche maintenant un badge **« Inconnu »** explicite — il n en reste que cinq,
les ajouts manuels de liste blanche.

**Trois chaînes muettes réparées**, toutes de la même famille : l information existait côté service et
mourait en route, sans erreur.

  - `get-alerts` ne projetait pas l'origine : l'historique des intrusions affichait « Inconnu » sur ses
    42 lignes ;
  - le groupage de l'audit ne recopiait pas l'origine : la colonne du tableau vert était vide malgré des
    événements qui la portaient ;
  - la colonne « Liste » liait une propriété absente de la classe de ligne — WPF ne signale rien, il
    affiche du vide.

**Actualisation automatique suspendue.** Elle tourne toutes les 5 s dans l UI, toutes les 20 s sur la
page web, et ramenait la vue au point zéro au moment où on la lisait. Elle se tait désormais dès qu on
touche à un filtre, à une recherche ou à un tri de colonne, et reprend par n importe quel bouton
d'actualisation. Un bandeau le signale : dans la barre d'état de l UI, et sur la page web avec un bouton
**Reprendre**.

**Largeurs.** « Tentatives » était coupé : renommé **« Nb/T »** (70 px, 60 sur le web) dans les quatre
tableaux, les deux interfaces portant enfin le même nom. Colonne Liste à 115, colonne IP à 165 — les
adresses s'affichaient tronquées en `20.250.119-`.

**Constat non traité, volontairement** : les colonnes Pays, FAI et Organisation du tableau vert restent
vides. Le quota d ipwho.is est épuisé (`HTTP 429`, pause de 24 h), et les compilations répétées
relancent le rattrapage de géolocalisation à chaque redémarrage du service. Piste notée : ne pas
interroger l API pour les IP dont l'origine est déjà connue (Microsoft, Cloudflare, Google, local), soit
98 % des appels. Christophe a préféré ne pas y toucher pour l instant.

### Session 2026-11-24 — signatures par catalogue Windows

Les exécutables Microsoft revenaient sans arrêt dans la liste des processus à approuver, malgré une
approbation. La cause n était pas l approbation mais la **lecture de la signature** : la quasi-totalité des
binaires de Windows 10 et 11 — `svchost`, `smartscreen`, `DeviceCensus`, `cleanmgr`, `MoUsoCoreWorker`,
`GameBarPresenceWriter`, `SoftLandingTask` — ne portent aucune signature dans le fichier. Ils sont signés
par un **catalogue** : un `.cat` rangé dans `CatRoot`, relié au binaire par le hash de son contenu.
`TryGetAuthenticodePublisher` n appelait `WinVerifyTrust` qu avec `WTD_CHOICE_FILE`, donc ces binaires
passaient pour non signés : ni auto-trust « signé Microsoft », ni filtre système, et une entrée dans la
liste à chaque nouvelle connexion.

**Lecture des catalogues** : `CryptCATAdminCalcHashFromFileHandle` puis `CryptCATAdminEnumCatalogFromHash`,
le `.cat` trouvé étant lui-même signé en dur. Résultat mémoïsé 30 minutes par chemin : hacher un binaire de
plusieurs mégaoctets à chaque paquet aurait coûté cher.

**Paquets du Store** : pas de catalogue dans `CatRoot`. L éditeur est lu dans `AppxManifest.xml`. Ce n est
pas une preuve cryptographique — le dossier `WindowsApps` est en lecture seule et protégé par
TrustedInstaller, falsifier ce manifeste suppose déjà les droits SYSTEM — mais c est un faisceau suffisant
pour ne pas noyer la liste.

**Plus d entrée `[no-path]::Nom`** à l approbation : cette clé ne peut correspondre à aucun chemin réel, et
le processus revenait indéfiniment. L approbation est refusée, avec un message dans le journal.

Vérifié en appelant le service compilé : `smartscreen`, `DeviceCensus`, `cleanmgr` et `svchost` donnent
`O=Microsoft Corporation` ; le paquet `MicrosoftStartFeedProvider` donne `CN=Microsoft Corporation` ;
`Discord.exe` reste `CN=Discord Inc.` ; `MCEMonitor.Languages.dll` ne donne aucun éditeur.

Installé sur le serveur Windows 10 de Christophe et sur le PC de dev. À confirmer sur la durée.

### Session 2026-11-24 — coches de blocage automatique dans MCEMonitor

L onglet NetMonitor de MCEMonitor montrait les seuils des détecteurs — fenêtre de scan de ports, seuil,
beaconing, jitter — qui ne se règlent qu une fois, et une seule case de blocage. Il affiche désormais ce
qui se change au quotidien : l interrupteur principal, puis les cinq catégories, chacune avec sa pastille
colorée : critiques, inconnues, scanners, robots officiels, robots génériques. Enregistrer suffit à bloquer
une catégorie sans ouvrir NetMonitor. Les seuils restent dans NetMonitor.UI, où ils étaient déjà.

**Le bug qui comptait.** L enregistrement réécrivait `NetMonitor.config` en entier avec huit clés.
Tout le reste disparaissait : `CriticalPatterns`, `ScannerPatterns`, `SafePatterns`, `WhitelistPatterns`,
`IgnoredNetworks`, `CrawlerPatterns`, `BlacklistLevel`, `BlacklistLastUpdate`, GeoIP, `IpLookupUrl`, les
listes officielles Microsoft et Google. Le service repartait sur ses valeurs par défaut, sans un mot dans
les journaux. Un `File.WriteAllLines` remplacé par une fusion : les valeurs sont remplacées sur place, les
clés absentes ajoutées à la suite du bloc, le reste recopié tel quel. Vérifié sur une copie de la
configuration réelle : aucune clé perdue, `BlacklistLastUpdate` intacte.

**Le style.** Une pastille ronde plutôt qu'une case : la couleur vient de la propriété `Tag` du `CheckBox`
(`Style CocheColoree`), et chaque thème porte ses teintes (`PastilleCritique`, `PastilleInconnue`,
`PastilleScanner`, `PastilleRobot`, `PastilleCrawler`) — plus soutenues en thème clair, où les teintes du
sombre disparaissent. Le point intérieur mesure un nombre pair de pixels (`Width="8"`) : à 7, centré dans
une boîte de 16, il tombait sur un demi-pixel et le lissage le décalait visiblement.

**Les messages.** `EstVrai` supposait « coché » quand une clé manque ; `AutoBlock` valant faux par défaut
dans le service, une variante prend le défaut réel. Le message d enregistrement distingue désormais service
arrêté et service injoignable, et s efface quand on bascule le service — il décrivait sinon un état périmé,
ce qui a fait croire un moment à une notification en échec alors que les trois services étaient simplement
arrêtés.

**Le rechargement à chaud** (`ServiceReload`) parle le protocole des clients des modules : octets bruts,
lecture jusqu à la fermeture, 2 s pour se connecter et 12 s pour la réponse, durée journalisée. Mesuré sur
la machine : 47 à 62 ms, huit essais sur huit.

**Langues** : huit phrases ajoutées en `fr-FR` et `en-GB`, dont les six libellés des coches. Ceux-ci
manquaient dans les deux fichiers alors que NetMonitor.UI les emploie mot pour mot : ses coches sont
désormais traduites aussi.

### Session 2026-11-24 — page des serveurs Android

**Carte « TOP IPs » retirée** de la page NetMonitor : sur un écran de téléphone, elle prenait la place des
informations utiles sans rien apprendre qu'une liste de blocages ne dise déjà. Le code du modèle suit
(`TopIps`, `HasTopIps`, `NetTopIpItem`). L'API du service continue de publier `topIps` : la page web s'en
sert.

**La sélection orange ne venait pas du XAML.** Sur Android, le `CollectionView` dessine sa propre
surbrillance au niveau natif, avec la couleur du système — orange sur certains appareils — qu'aucun état
visuel ne peut neutraliser. La sélection native est donc désactivée (`SelectionMode=None`) et l'état porté
par le modèle : `IsSelected` sur `ServerItemViewModel`, un `DataTrigger` sur le `Border`. Le serveur retenu
se signale par un **relief** — bordure d'accent de 2 px, ombre portée bleutée — et non par un aplat.

**Le violet du gabarit, éliminé à trois niveaux.**

- `Shell.TitleColor` valait `#9880e5` en thème sombre, et le magenta `#D600AA` servait aux onglets en thème
  clair. Cette couleur peint aussi les titres des pages de chaque service, qui se contentent de définir
  leur `Title`. Le style du Shell est maintenant fixe (`#14212B` / `#4CC2FF`), sans dépendance au thème
- la couleur d'accent de l'application (`Primary`) et `colorAccent` Android passent au bleu `#4CC2FF` ;
  `PrimaryDark`, fond des boutons en thème sombre, devient `#1E3A4D` pour garder le texte clair lisible
- la barre système Android (`colorPrimary`) abandonne `#512BD4` pour les teintes du fond de page

**Fond de page** : dégradé sombre bleuté au lieu du gris uniforme, en harmonie avec l'accent.

**Versions** : le tag `v3.0.10` a été posé sur `e5c128b`, avant ces corrections. Il faudra le déplacer,
ou publier une 3.0.11.

### Session 2026-11-24 — surcharges d'E/S visibles dans les clients

**Requêtes ou débit : la confusion était dans le message.**

- l'alerte annonçait un pourcentage d'occupation et deux débits, alors que ce sont les **requêtes** qui
  saturent un disque. Un disque peut être à genoux en échangeant quelques centaines de kilo-octets par
  seconde, s'il s'agit de milliers de petites E/S
- deux compteurs ajoutés (`DiskReadsPerSec`, `DiskWritesPerSec`) ; message et mail présentent désormais
  les requêtes, la file d'attente, puis le débit — présenté comme une conséquence
- cas réel : une base SQLite mal en point, 588 % d'occupation pour 0,2 Mo/s
- `PercentDiskTime` **n'est pas borné à 100 %** : sa valeur élevée est parlante, elle n'est pas plafonnée,
  et le libellé devient « temps d'occupation »

**Les alertes remontent enfin dans les clients.**

- `WorstSeverity` ignorait les E/S : ni l'icône du Tray ni celle d'Android ne changeaient. C'est la cause
  du silence côté clients alors que le mail partait
- `LastIo` conserve le relevé, comme `LastDisks` et `LastSmart` ; `/api/summary` et `/api/detail`
  publient la catégorie `DiskIo` ; nouvelle commande IPC `get-io`, seuils exposés dans `get-config`
- Tray : interroge `get-io`, ajoute le problème à la liste, libellé « Disque »
- Android : `ProblemKind.DiskIo`, libellé « Disque (E/S) », et traduction des catégories techniques
- seul le relevé courant compte : une surcharge passée ne laisse pas l'icône bloquée en alerte

**Icônes.**

- `romok.png` et `hdd-critical.png`, copiées du client de configuration, remplacent `warning.png` et
  `critical.png` : RomMonitor ne surveille que des disques, l'icône le dit maintenant
- côté Android, `hdd_critical` pour la variante critique

**Non modifié volontairement** : le filtre des lecteurs réseau. `I:` d'un serveur est un partage
`\\192.168.1.19\medias 3`, et sa valeur aberrante venait d'un disque réellement saturé, pas d'un défaut de
mesure.

### Session 2026-11-23 — filtres, finitions graphiques, release 3.0.9

**Filtres et analyse des blocages.**

- filtres par type, sévérité et origine sur le tableau des IP bloquées, dans l'interface Windows et sur
  la page web, avec compteur de lignes visibles
- bouton « Réanalyser les blocages » dans les deux interfaces : recalcule sévérité et origine avec la
  blacklist du moment, signale les blocages qui ne seraient plus décidés, et ne débloque rien sans
  confirmation
- l'origine d'une IP est conservée au moment du blocage : elle était perdue, d'où des lignes vides dans
  le tableau alors que l'intrusion l'indiquait
- **piège des traits de tableau** : le trait posé sur la ligne est recouvert par le fond opaque des
  cellules ; il doit être porté par la cellule, et dans la teinte du tableau

**Apparence.**

- **boutons** : bordure de trois points, puis fond dessiné **sous** le contour — un seul `Border` arrondit
  le fond à rayon moins épaisseur et laisse un liseré du fond de fenêtre dans les angles. Corrigé dans les
  six projets, y compris les gabarits nommés dont les déclencheurs visaient le `Border`
- **`TemplateBinding` rétabli** : mon script d'épaississement avait figé `BorderThickness` en dur dans les
  gabarits, rendant tout réglage par bouton inopérant — d'où un correctif qui semblait sans effet
- survol des boutons ordinaires en gris au lieu du bleu, qui les confondait avec les boutons d'action
- bouton « i » recentré : un `ContentPresenter` centre la ligne de texte, pas le glyphe ; rendu en niveaux
  de gris pour éviter les franges du ClearType sur 22 px
- en-têtes de tableaux plus contrastés en thème clair ; ligne du tableau rouge rosie
- **Tray** : la fenêtre des serveurs reprend les ressources du client de configuration, barre de
  défilement comprise. Le client reste autonome — aucune lecture de `theme-ui.config`

**Page web.**

- bouton « Restaurer les règles à l'origine » retiré : action trop lourde pour une page consultée à
  distance, elle reste dans l'interface Windows
- bouton « Réanalyser les blocages » ajouté ; colonne Origine de l'historique à largeur fixe

**Installation.**

- quatorze mégaoctets de natives Linux, macOS et Android retirés : `SystemMonitor.Service` publiait les
  bibliothèques de toutes les plateformes. `RuntimeIdentifier` `win-x64` **avec** `SelfContained=false`,
  faute de quoi .NET 6+ bascule en publication autonome et ajoute 150 Mo
- assemblys satellites limités à `fr;en;de;de-DE;es;es-ES` via `Directory.Build.props` : dix-huit fichiers
  de ressources deviennent quatre. Les variantes régionales sont indispensables, DiskInfoToolkit ne
  publiant que `de-DE` et `es-ES`
- installeur : nettoyage des runtimes étrangers, des dossiers de culture abandonnés (dont `zh-Hant`, que
  TaskScheduler publie sous ce nom et non `zh-CN`), et des `WinDivert*.old`
- **ne pas supprimer le dossier `runtimes` entier** : `win` et `win-x64` portent les bibliothèques .NET
  des services et le pilote WinDivert

**Release 3.0.9** : installeur 3.0.9.0 (110,5 Mo, contre 115,9 pour la 3.0.8) et APK `versionCode 30009`.

### Session 2026-11-23 — hygiène : encodages, ancien projet, branche morte

**Accents détruits dans quatre fichiers.**

- même signature partout : le caractère de substitution `U+FFFD` (`EF BF BD`), trace d'une conversion
  d'encodage ratée où chaque octet accentué a été remplacé
- touchés : `MCEMonitor.UI/Partage/EmailSender.cs`, `MCEMonitorClient.Service/Services/PollingEngine.cs`,
  `RomMonitor.UI/App.xaml.cs`, `SystemMonitor.UI/App.xaml.cs`
- deux messages affichés à l'utilisateur étaient concernés (« RomMonitor.UI est déjà au premier plan. »,
  son équivalent SystemMonitor, « Instance déjà active »), ainsi que trois messages de journal de
  PollingEngine
- **piège évité** : PowerShell lit un fichier sans BOM en Windows-1252 et réencode les accents déjà
  corrects en double encodage (`Durée` devient `DurÃ©e`). Un premier script a abîmé les fichiers, vus aux
  octets (`c3 83 c2 a9` au lieu de `c3 a9`), restaurés depuis Git et refaits avec bun/Node

**Six `EmailSender.cs` convertis de Windows-1252 vers UTF-8.**

- MediaMonitor.Core, NetMonitor, RomMonitor, SystemMonitor, StopMonitor, WakeMonitor : ils s'affichaient
  mal dans tout éditeur réglé sur UTF-8, et le mélange d'encodages finit toujours par coûter des accents
- conversion par décodage explicite `windows-1252`, sans BOM, fins de ligne inchangées
- contrôles : nombre de caractères identique, nombre de lignes identique, UTF-8 valide après écriture,
  34 lignes modifiées et aucune autre
- **piège évité** : ces fichiers contiennent l'octet `0x96`, tiret demi-cadratin en windows-1252 mais
  caractère de contrôle en latin-1. Un décodage latin-1 aurait glissé un caractère invisible dans les
  sujets d'email
- le nom d'affichage de MCEMonitor.UI est aligné sur les autres modules : `MCEMonitor – MACHINE`

**`MCEMonitor.old` supprimé.**

- l'ancien projet WinForms, 49 fichiers, était inerte depuis la bascule 3.0.0 : absent de la solution,
  de `build.bat` et de l'installeur, aucune référence nulle part
- supprimé deux jours avant la date prévue, la 3.0.8 ayant fait ses preuves entre-temps
- rien n'est perdu : le renommage s'est fait au commit `3fde0e1`, donc
  - `git show 3fde0e1^:MCEMonitor/MainForm.cs` pour lire un fichier
  - `git checkout 3fde0e1^ -- MCEMonitor` pour restaurer le dossier complet
- solution recompilée après suppression : 0 erreur

**Branche distante `refactor/decompose-webserver` supprimée.**

- reliquat d'un essai de découpage du serveur web, dernier commit « Revert » du 07/09/2026
- entièrement contenue dans `main` : aucun commit en avance, 548 commits de retard. Branche morte

### Session 2026-11-22 — Threat intelligence et audit des réglages NetMonitor

**Threat intelligence : deux réglages sans effet et une liste qui ne se mettait pas à jour.**

- le niveau IPsum (1–6) saisi dans l'interface était enregistré, transmis et relu, mais
  `ThreatIntelService` était construit sans paramètre et son URL pointait sur `levels/3.txt` en dur :
  changer le niveau ne faisait rien
- la date de dernière mise à jour n'était **jamais** écrite : l'interface affichait « Dernière MAJ :
  inconnue » à perpétuité (`BlacklistLastUpdate` restait à `0001-01-01`)
- le minuteur tirait une fois par 24 h. Si le réseau n'était pas prêt au démarrage du service, si la
  machine sortait de veille ou revenait d'un arrêt, **rien ne réessayait avant le lendemain** — le cas
  le plus fréquent, et le plus silencieux
- désormais : vérification toutes les 15 min, téléchargement seulement si dû (cache absent, niveau
  différent de celui du cache, ou liste de plus de 24 h), date enregistrée après succès. Le cache porte
  son niveau en première ligne ; un verrou évite deux téléchargements simultanés
- la provenance, pour mémoire : liste publique IPsum (`stamparm/ipsum`), niveau 3 = IP signalées par au
  moins 3 sources indépendantes, fusionnée avec `blacklist.txt` (manuelle)

**Audit des réglages : quatre incohérences, toutes corrigées.**

- `IgnoreSearchScanners` et `IgnoredProcesses` ne servaient à rien : déclarés, persistés, transmis à
  l'interface pour le premier, remplis de 26 processus pour le second, mais lus par aucun code. Leur
  suppression nettoie aussi `NetMonitor.config` au prochain enregistrement
- `AutoBlockSubnet` devient `BlockKnownScanners` : le réglage bloque les scanners connus (Shodan,
  Censys…), pas des plages entières. L'ancienne clé reste lue, la nouvelle prime quel que soit l'ordre
  des lignes du fichier
- le GeoIP (activation, fournisseur, reverse DNS) est rechargé à chaud ; l'ancien objet n'est pas
  libéré, des résolutions pouvant être en vol
- le message de confirmation ne promet plus un rechargement complet : il liste ce qui s'applique à
  chaud et rappelle que la surveillance des processus de confiance demande un redémarrage

**Réanalyse des blocages existants.**

- bouton « ♻ Réanalyser les blocages » dans la barre du tableau des IP bloquées, message IPC
  `reevaluate-blocked`, rapport détaillé (analysées / reclassées / obsolètes / overrides manuels)
- critère d'obsolescence : l'IP **n'est plus blacklistée** et sa sévérité recalculée ne déclencherait
  plus le blocage automatique avec les réglages en vigueur
- **rien n'est débloqué sans accord** : l'interface affiche le bilan, liste les IP concernées (10 au plus
  dans la boîte de dialogue) et demande confirmation
- garde-fous : on ne juge l'obsolescence que si `AutoBlock` est actif, sinon tous les blocages sont
  volontaires ; une IP encore blacklistée reste légitime quel que soit le seuil ; les sévérités forcées
  à la main sont préservées et comptées à part
- premier passage réel : 60 IP analysées, 37 origines récupérées (`Unknown` → `Blacklist`), 0 obsolète.
  Les deux IP sorties de la liste sont classées Crawler et `BlockCrawlers=true` : elles seraient encore
  bloquées aujourd'hui, donc à juste titre conservées
- `ReevaluateAllBlockedIps()` est conservée pour ses trois appelants (restauration des patterns via IPC
  et web, migration au démarrage) et délègue à `ReanalyzeBlockedIps()`, qui renvoie le rapport

**Piège d'automatisation rencontré, à retenir.**

- les clics en coordonnées partent vers la **fenêtre au premier plan**, pas vers la cible ; les captures,
  elles, photographient la fenêtre même en arrière-plan. Des clics ont ainsi atterri dans la fenêtre de
  conversation au lieu de l'application testée, en donnant l'illusion que les boutons ne répondaient pas
- vérifier `GetForegroundWindow` avant tout clic en coordonnées, ou faire cliquer l'utilisateur

### Session 2026-11-22 — thème clair pour les cinq interfaces

**Objectif** : proposer un thème clair à côté du thème sombre, dans MCEMonitor et les quatre modules,
sans rien changer au sombre existant.

**Architecture retenue**, identique dans les cinq interfaces :

- `Themes/Sombre.xaml` et `Themes/Clair.xaml` ne contiennent que des `SolidColorBrush` — mêmes clés
  de part et d'autre — et `Themes/Styles.xaml` porte les styles, qui ne citent jamais une couleur en dur
- `GestionnaireTheme.cs` lit `C:\ProgramData\MCEMonitor\theme-ui.config` au démarrage et fusionne le
  dictionnaire correspondant, puis les styles. Absence ou valeur inconnue → sombre
- `App.xaml` ne déclare plus rien : le chargement se fait dans `OnStartup`, avant la création de la fenêtre
- le bouton « Changer de thème » écrit le fichier puis **propose un redémarrage automatique**
  (`App.LibererInstanceUnique()` avant la relance, sinon la nouvelle instance bute sur le mutex)
- **Le thème s'applique au démarrage, pas à chaud** : les cinq interfaces totalisent plus de 5 000 lignes
  de XAML qui utilisent `StaticResource`. Passer en `DynamicResource` partout fragiliserait l'ensemble
  pour un gain nul — le redémarrage est proposé à la place
- `theme-ui.config` est **distinct** de `theme.config`, vestige WinForms Krypton (`SparkleBlueDarkMode`),
  laissé tel quel

**Clés ajoutées pour le thème clair** (mêmes noms dans les cinq interfaces) :

- `AccentBascule` (interrupteurs), `PanelBackground` (#333A8DFF), `FondZoneLog`, `TraitTableau`
- `BoutonRougeFond` / `BoutonRougeBordure` / `BoutonRougeTexte` et l'équivalent vert : en clair, un fond
  rouge plein est trop lourd, les boutons de NetMonitor passent en fond neutre cerclé de la couleur
- `TeinteIntrusion` / `TeinteInterception` / `TeinteAudit`, `Ligne*`, `Entete*` : les tableaux colorés
  restent colorés, mais en pastel
- `TraitIntrusion` et, pour l'audit, la réutilisation de `BordureAuditClaire`

**Couleurs sémantiques préservées** : sévérités, badges, détecteurs, catégories de médias (17 couleurs),
échelle de la carte du monde gardent exactement les mêmes teintes dans les deux thèmes. Le thème clair
change le fond, pas le sens.

**Réglages esthétiques au cas par cas** : boutons de barre de titre en `DynamicResource Texte` (4 modules),
journal du test SMTP masqué au repos, en-têtes d'onglet colorés (rose intrusions, bleu interceptions, vert
audit), grille Connexions et Appareils de confiance en gris `#D4D4D4`, `GridLinesVisibility="None"` sur le
style implicite du `DataGrid`, jauges de SystemMonitor en `Accent`, boutons violet et rouge de MediaMonitor
neutralisés avec survols.

**Piège des traits de tableau, rencontré sur les trois tableaux colorés.**

- le trait horizontal posé sur la **ligne** ne s'affiche jamais : le fond opaque des cellules le recouvre.
  Il doit être porté par la **cellule** (`BorderThickness="0,0,1,1"`), jamais par les deux
- conséquence directe : le trait de la ligne doit être neutralisé, sinon on croit à un doublon
- et la couleur compte autant que la présence : en sombre, le gris commun `#3C3C3C` sur un fond rouge très
  sombre ne se voit pas ; il faut une teinte de la famille du tableau
- valable pour les trois tableaux colorés de NetMonitor (intrusions, interceptions, audit)

**MediaMonitor : deux oublis corrigés après coup.**

- la ligne sélectionnée prenait `Accent` en plein (`#0F6CBD` en clair) sur toute sa largeur : trop de
  bleu. Deux clés `SelectionFond` / `SelectionTexte` le remplacent dans les sept endroits concernés
  (cellules, lignes de liste déroulante, cellules de type de média, bouton au clic). En sombre, les
  valeurs sont celles de l'accent d'origine : aucune régression
- le survol du bouton « i » gardait l'accent plein sous un texte noir, donc illisible en clair : il
  prend la teinte de sélection, avec une bordure d'accent pour rester distinct du fond au repos
- `InfoPopupWindow.xaml` n'avait jamais été thémée : fond `#1A1A1A`, libellés `#42A5F5`, titre
  `#B4B4B4`, boutons `#2D2D30` — tout en dur sur 264 lignes. Elle passe entièrement par les clés,
  avec l'opacité de son image de fond ramenée à 0,08 en clair
- écarts assumés en sombre : fond de la popup `#1A1A1A` → `#1E1E1E` (la valeur du thème), boutons
  `#2D2D30` → `#252526`, libellés `#42A5F5` → `#4CC2FF`

**Vérification de la popup sans données réelles.**

- l'instance de contrôle tourne sans élévation : elle n'obtient rien du service et les tableaux
  restent vides, donc aucune popup à ouvrir
- deux lignes de démonstration injectées temporairement dans `_items`, et un appel direct à
  `InfoPopupWindow(...).ShowDialog()` dans le minuteur de démonstration : c'est ce qui a permis de
  voir le rendu réel dans les deux thèmes. Tout a été retiré ensuite (`grep TEMPORAIRE` → 0)
- **le clic sur « i » n'ouvre rien avec des données factices, et ce n'est pas un bug** : le bouton
  dépend d'un filtrage par nom et par type (film, série, audio…), que des lignes inventées ne
  satisfont pas

**Release 3.0.8 « Thème clair »** : installeur 3.0.8.0 et APK (`versionCode 30008`), tag `v3.0.8`,
release marquée Latest. Le tag intermédiaire `v3.0.7`, qui n'a mené à aucune publication, a été retiré
en local et sur le dépôt.

### Session 2026-11-22 — client en WPF, courbes CPU par processus

**Le client de configuration passe entièrement en WPF** (`MCEMonitorClient`).

- `MCEMonitorClient.Config` : fenêtre principale au gabarit MCEMonitor (900 × 680, non redimensionnable),
  dialogue d'ajout / modification d'un serveur avec test de connexion sur `/api/summary`, actions
  ajouter / modifier / supprimer / exporter / importer, section Service et Tray, options
- `MCEMonitorClient.Tray` : popup des serveurs réécrit (`FenetreServeurs.xaml`). Le projet reste un
  exécutable WinForms pour le `NotifyIcon` et passe en `UseWPF` pour héberger la fenêtre
- `App.xaml` reprend intégralement la palette de `MCEMonitor.UI` : c'est ce qui garantit le même style
- Anciens fichiers supprimés (2 245 lignes) ; ils restent dans l'historique Git
- Mode de contrôle `VERIF` : mutex et pipe distincts, pour tester à côté de l'exécutable livré, qui est élevé
- **Constat de méthode** : ce client ne se teste qu'en version élevée. Sans élévation, l'écriture dans
  `C:\ProgramData\MCEMonitor` échoue et le pipe du service est refusé aux processus de moindre intégrité

**Courbes CPU par processus** (SystemMonitor).

- `ProcessCpuTracker` mesure l'usage par **écart de `TotalProcessorTime`** entre deux relevés, agrégé
  par nom. `TopProcessesCollector` ne convenait pas : il échantillonne pendant une seconde entière
- jusqu'à cinq séries au-dessus de 10 %, courbe globale en gras, légende avec la charge courante
- un processus repassé sous le seuil disparaît ; les séries sont alignées à droite sur l'échelle de
  temps de l'historique global
- après cinq minutes continues, entrée d'historique `info` / `ProcessHigh` **avec la courbe enregistrée**
  (~1,5 Ko). Double-clic dans l'historique → aperçu (période, durée, pic, dernière valeur, courbe)
- seuil, plafond de cinq et durée de cinq minutes sont des constantes du service

**Journal et notifications de SystemMonitor.**

- seuils en vigueur au démarrage, une ligne au début et à la fin de la chauffe (au lieu de dix-huit),
  transitions `[ÉTAT]`, problèmes apparus et résolus, entrées et sorties de suivi des processus
- la durée de chauffe était incohérente : 90 s réels, 60 s au journal, 90 s au commentaire. Une seule
  définition désormais
- l'API publie un état `warmup` pendant la chauffe ; les clients l'ignorent et ne notifient pas
- `GetProblems()` ne publie plus rien pendant la chauffe : le journal annonçait un problème critique
  alors que les seuils étaient suspendus
- la reconnexion prime sur l'icône du problème en cours (Windows et Android) ; un serveur qui revenait
  avec une surcharge affichait `cpu-overload.png` sur une notification de reconnexion

**Parité WinForms rétablie — deux régressions signalées.**

- les boutons « Ouvrir MediaMonitor / RomMonitor / SystemMonitor / NetMonitor » ne lançaient plus le
  Tray de leur module, seulement le service
- les boutons « Automatique » ne recréaient plus la tâche `ONLOGON` du Tray, jamais recréée depuis la
  bascule
- **Un Tray ne peut pas être lancé par son service**, qui tourne en session 0 (SYSTEM) sans bureau :
  il est lancé par l'interface, et la tâche `ONLOGON` couvre le démarrage de session. Le Tray attend
  déjà son service puis se ferme s'il n'arrive pas
- audit complet de parité : 31 opérations sur tâches, IPC, processus lancés, 60 gestionnaires —
  aucune autre régression trouvée

**Release 3.0.6 « WPF Only »** : installeur et APK, notes reprenant tout l'historique 3.x, page 3.0.4 retirée.

### Session 2026-11-22 — surcharge disque, journal de MCEMonitor, parité WPF

**RomMonitor surveille désormais la surcharge de lecture / écriture.**

- `DiskIoChecker` interroge WMI (`Win32_PerfFormattedData_PerfDisk_LogicalDisk`) : taux d'occupation,
  débits lecture/écriture, file d'attente, par lecteur (`_Total` et `HarddiskVolume*` écartés)
- Deux alertes (`DiskIoHigh`, `DiskIoCritical`) déclenchées après **6 relevés consécutifs** au-dessus de
  80 % (une minute de charge continue), avec envoi de mail ; cinq réglages exposés dans les deux UI
- Onglet Configuration de RomMonitor.UI séparé en deux colonnes : « Indices de capacité » et
  « Indices de charge »

**MCEMonitor écrit son propre journal** (`%ProgramData%\MCEMonitor\Logs\MCEMonitor.log`) :
ticket de session, état des sept tâches planifiées au démarrage, contrôle SMTP et déverrouillage des
onglets, tous les clics et changements d'onglet, réglages On/Off et des modules, exceptions non gérées.
Le fichier est vidé à chaque ouverture ; une session qui ne finit pas par « Fermeture » a été interrompue.

**Mise en veille programmée** : elle ne dépend plus de l'envoi du mail. Une connexion SMTP bloquée en
`CloseWait` empêchait la tâche d'atteindre `SetSuspendState` ; le mail part maintenant en tâche de fond
bornée à 20 s, et l'attente vise l'heure cible absolue. Timeout SMTP à 15 s.

**Parité WinForms → WPF rétablie** : les deux appels `...IpcClient.ReloadConfigAsync` de l'ancienne
interface (RomMonitor, SystemMonitor) avaient été perdus, ainsi que la reprise du mode (veille/arrêt)
depuis la tâche planifiée. `ServiceReload.cs` notifie désormais RomMonitor, SystemMonitor et NetMonitor
après chaque enregistrement de réglages ; les deux issues sont journalisées.

**Traductions complétées** : 15 clés manquaient pour la surcharge disque et les messages de
`ServiceReload`. Le repli du `LanguageManager` les affichait en français, y compris en anglais —
d'où l'ajout d'une règle de contrôle des chaînes traduisibles.

---

### Session 2026-11-21 — bascule de l'interface vers WPF (3.0.0)

**L'interface WinForms Krypton est remplacée par un projet WPF. Tag `v3.0.0`.**

- Nouveau projet **`MCEMonitor.UI`** : 9 onglets, 2 fenêtres secondaires (MAC autorisées, confirmation), style MyAutoDNS — coins arrondis, ombre, fond commun aux modules
- **`MediaMonitor.UI` gère désormais ses exceptions** : trois gestionnaires et `MediaMonitor.UI.crash.log`, comme les UI NetMonitor, RomMonitor et SystemMonitor
  - Le filtre « bruit Mapsui » de NetMonitor n'a pas été repris : ce module n'utilise pas de carte
- Modules masqués tant que l'envoi d'emails n'est pas opérationnel (contrôle SMTP au démarrage et à chaque enregistrement)
- Mode `--run-scheduled` porté dans l'application WPF : l'arrêt programmé exécute réellement l'action
- Boutons de test SMTP de StopMonitor et WakeMonitor : envoi réel
- Traductions : 92 libellés XAML (dont les 9 en-têtes d'onglets), 49 clés ajoutées en `fr-FR` et `en-GB`
- Correctifs : encodage CP850 non enregistré (toutes les tâches planifiées échouaient), icône de l'installeur pointant vers le dossier archivé
- Bascule : ancien projet en `MCEMonitor.old`, logique copiée dans `MCEMonitor.UI\Partage\`, `build.bat` publie le nouveau projet, exécutable toujours nommé `MCEMonitor.exe`

---

### Session 2026-11-21 — chiffrement DPAPI du mot de passe SMTP, release 2.3.10

**Le mot de passe SMTP n'est plus stocké en clair. Publication de la 2.3.10.**

#### Sécurité — chiffrement au repos

- [x] **Constat** : `email.config` contenait `Password=<clair>`, lisible par tout compte local (`BUILTIN\Utilisateurs : Read`). Aucun chiffrement nulle part dans la solution (ni DPAPI, ni AES)
- [x] **Chiffrement DPAPI** (`ProtectedData.Protect`, **portée `LocalMachine`**, Base64) autour de la clé `PasswordEnc`
  - `LocalMachine` est indispensable : l'UI tourne en administrateur, les services en SYSTEM — `CurrentUser` rendrait la valeur illisible pour l'un des deux
  - Une **entropie applicative commune** `MCEMonitor.email.v1` est utilisée dans les sept copies : sans elle, un programme quelconque pourrait déchiffrer ; avec une entropie divergente, un module ne relirait pas ce qu'un autre a écrit
- [x] **Sept modules traités** : quatre écrivent (`MCEMonitor`, `MediaMonitor.Core`, `StopMonitor`, `WakeMonitor`), trois lisent seulement (`NetMonitor.Service`, `RomMonitor.Service`, `SystemMonitor.Service`)
- [x] **Rétrocompatibilité** : `Password=` reste lu tant qu'aucune valeur chiffrée n'existe ; le premier enregistrement depuis l'UI réécrit le fichier en forme chiffrée. Aucune intervention manuelle
- [x] **Paquet `System.Security.Cryptography.ProtectedData` 8.0.0** ajouté aux sept projets
- [x] **Modèle de référence** : `MyAutoDNS/MyAutoDNS.Core/ConfigurationEmail.cs`, repris tel quel (entropie, portée, écriture atomique)
- [x] **Vérifié** : les sept projets compilent sans avertissement ; le fichier est bien chiffré après enregistrement ; le test d'envoi depuis l'UI fonctionne
  - **Reste à confirmer** : un envoi déclenché par un service (SYSTEM) — c'est le cas qui validerait la portée DPAPI

#### Encodages

- [x] `RomMonitor.Service/EmailConfig.cs` et `SystemMonitor.Service/EmailConfig.cs` étaient en CP1252 → convertis en UTF-8
- [x] Les sept `EmailConfig.cs` alignés sur `charset = utf-8-bom` de `.editorconfig`

#### Vérifications faites

- [x] **Aucun mot de passe journalisé** : vérifié dans les sept modules, aucune instruction de log ne mentionne `Password` (les occurrences sont des libellés d'interface)
- [x] **Client Android** : les identifiants sont stockés dans `SecureStorage` et retirés du JSON ✔
- [x] **Client Windows** : corrigé le 2026-11-21 — les quinze mots de passe sont désormais chiffrés DPAPI (voir la sous-section suivante)
- **`adb logcat`** : non vérifié — nécessite un appareil, et le client Android est en pause

#### i18n — clés mortes

- [x] **Six clés `Tab.*` supprimées** de `fr-FR.json` et `en-GB.json` : `Tab.Email`, `Tab.MediaMonitor`, `Tab.WakeMonitor`, `Tab.StopMonitor`, `Tab.OnOff`, `Tab.About`
  - Aucune n'était référencée : le Designer de `MainForm` appelle `Get("Email")`, `Get("OnOff")`, `Get("À propos")`, sans préfixe
  - Autotrad ne les recréera pas : il ne collecte que les clés présentes dans le code

#### Client Windows — mots de passe serveurs chiffrés

- [x] **`ServerEntry`** : `Password` passe en `[JsonIgnore]` (jamais sérialisé) ; `PasswordLegacy` mappe l'ancien champ `password` pour migrer sans intervention ; `PasswordEnc` devient la seule forme écrite
- [x] **`ServerConfigStore`** (Config et Service) : chiffrement avant écriture, déchiffrement à la lecture, repli sur l'ancienne valeur en clair
- [x] **Portée `LocalMachine`** : le fichier est écrit par l'interface (compte utilisateur) et relu par le service, qui tourne en **SYSTEM** (`/RU SYSTEM` confirmé dans `TaskSchedulerHelper`)
- [x] **Entropie `MCEMonitor.client.v1`**, identique dans les deux projets
- [x] **Paquet `ProtectedData`** ajouté aux deux projets ; les quatre fichiers, en CP1252, convertis en UTF-8 avec BOM
- [x] **Vérifié sur la machine** : après enregistrement depuis la Config, `MCEMonitorClient.config` contient **0 `password` renseigné, 15 vides, 15 `passwordEnc`**
  - **Reste à confirmer** : le service client (SYSTEM) doit continuer à interroger les serveurs — même contrôle que pour le SMTP
- Note : `MCEMonitorClient.Tray` possède des copies de `ServerEntry` et `ServerConfigStore` **qu'il n'utilise jamais** (il passe par l'IPC). Code mort à nettoyer, et ce sont les copies qui contiendraient encore un `Password` sérialisable

#### build.bat — version laissée à Git

- [x] **Bloc d'auto-tag supprimé** : il créait et poussait un tag `vX.Y.(Z+1)` au-delà de 100 commits depuis le dernier tag. Les tags sont désormais créés volontairement, à la publication. Les 21 `publish` sont intacts
- **Précision** : ce bloc n'était pas la cause du passage en 2.3.11. C'est GitVersion (`ContinuousDelivery`) : tout commit postérieur au tag `v2.3.10` fait porter au build suivant la version 2.3.11
- **Piste non retenue** : épingler `-p:Version=` sur le dernier tag dans `build.bat`, pour n'avoir aucune dérive entre deux releases. Christophe a préféré ne rien changer de plus

#### Distribution

- [x] **Tag `v2.3.10`** créé sur le commit du chiffrement, puis poussé
- [x] **Installeur `MCEMonitorSetup-2.3.10.0.exe`** compilé (120 Mo) et publié
- [x] **APK en 2.3.10 / versionCode 20310** : la version est calculée depuis le tag Git, plus figée à 1.0. Vérifié dans le manifeste binaire de l'APK — sa taille étant identique à la 2.3.9, seule cette lecture prouvait la version
- [x] **Release `v2.3.10`** publiée avec l'installeur, l'APK et `Autotrad.exe`. La `2.3.9` est conservée volontairement (retour arrière possible sur un changement de format de fichier)
- [x] **Dépôt rendu découvrable** : 20 topics (monitoring, homelab, self-hosted, intrusion-detection, windivert, dvbviewer, csharp, wpf…), homepage liée vers le site GitHub Pages, description inchangée

### Session 2026-11-19 / 20 — i18n complet, barres de titre WPF, releases

**Chantier d'internationalisation terminé, harmonisation visuelle des fenêtres, remise à plat des releases GitHub.**

#### i18n — traduction complète du code

- [x] **C#** : 94 chaînes passées à `LanguageManager.Get(...) ?? "..."` — 7 fichiers (MCEMonitor, MCEMonitorClient.Config, MCEMonitorClient.Tray, MediaMonitor.UI, plus les trois UI NetMonitor / SystemMonitor / RomMonitor)
- [x] **XAML NetMonitor** : 160 chaînes dans 5 fenêtres ; `xmlns:loc` ajouté aux 4 fenêtres qui ne le déclaraient pas
- [x] **XAML — clés courtes** : 26 clés `Net.*` créées pour les chaînes contenant une apostrophe (ni la syntaxe XAML ni le parseur d'Autotrad ne les acceptent), avec entrées dans `fr-FR.json` **et** `en-GB.json`
- [x] **HTML** : 68 textes dans `NetPage.html`, `Charts.html`, `Map.html` — y compris dans les blocs `<script>` (descriptions des cartes, `alert` / `confirm` / `prompt`)
- [x] **Pages HTML construites en C#** : login des 4 modules (9 + 5 + 5 + 5 textes), 9 messages d'erreur du login NetMonitor, « Historique effacé » de MediaMonitor via `HTMLTranslator`
- [x] **Trays** : `LanguageManager` branché dans les 4 projets (référence projet + `Load()` au démarrage depuis `language.config`), menus, popups et dialogues traduits
- [x] **Clé morte `"jours"`** (minuscules) supprimée des deux JSON. Les variantes `ERREUR :` et `STATISTIQUES AVANCÉES` sont légitimes : le `Dictionary` est sensible à la casse, les deux sont réellement appelées
- [x] **Piège documenté** : une extension `{loc:Tr …}` n'est résolue que si elle occupe **toute** la valeur de l'attribut. Trois endroits affichaient leur balise en clair (`✖ {loc:Tr 'Annuler'}` et deux textes d'aide `ℹ`) → corrigés par un `StackPanel` pour le bouton et des `<Run>` pour les textes

#### NetMonitor — processus bloqués et pare-feu

- [x] **Doublons de processus** : `BlockProcess` ajoutait une entrée à chaque blocage → met désormais à jour l'entrée existante (identité = `ProcessPath`, comme `UnblockProcess`)
- [x] **Plantage de la page web** : `WebHandler` et `IpcCommandHandler` indexaient un dictionnaire par `b.Ip`, qui vaut le **nom du processus** pour les entrées `IsProcess` → `GroupBy` + entrée la plus récente (symptôme : `An item with the same key has already been added. Key: SyncLive`)
- [x] **`LoadBlockedIps`** déduplique les fichiers existants et les réécrit
- [x] **`SyncProcessRules`** : rétablit au démarrage les règles de processus manquantes (après un vidage manuel des règles du pare-feu, par exemple)
- [x] **`SyncFirewallRules`** n'accepte plus que de vraies adresses IP : un nom de processus dans la liste faisait échouer tout un bloc netsh

#### Sécurité des interfaces web

- [x] **Cookies qualifiés par le port** sur les 4 modules (`netmon_session_14003`, `netmon_trusted_device_14003`, `sysmon_session_13003`, …) : deux instances sur le même hôte ne s'écrasent plus mutuellement l'OTP
- [x] **Refus explicite** : `/set-severity` et `/set-origin` renvoient un **409** avec un message traduit quand le moteur refuse ; la page l'affiche dans une alerte au lieu de recharger

#### WPF — barres de titre

- [x] **4 MainWindow + 6 dialogues** : `WindowStyle="None"` + `WindowChrome` (sans `AllowsTransparency`), titre lié à `Window.Title`, boutons Réduire / Agrandir / Fermer, déplacement par l'en-tête, double-clic, coins arrondis via DWM (sans effet sous Windows 10)
- [x] **`Background="Transparent"`** sur la grille d'en-tête : sans lui, seule la zone du titre répondait à la souris et la fenêtre ne se déplaçait que par le coin haut-gauche
- [x] **Style `Button` implicite** ajouté aux 6 dialogues : leurs boutons utilisaient le rendu WPF par défaut (gris système)
- [x] **BOM UTF-8** ajouté aux XAML qui n'en avaient pas (NetMonitor, SystemMonitor)

#### Documentation et distribution

- [x] **README complet** : modules, architecture, prérequis, compilation, installation, emplacements, configuration, interfaces web, clients, i18n, outils annexes, décisions d'architecture, dépannage, licence
- [x] **Build Android** : `BuildAndroid.bat` calcule la version depuis le dernier tag `v…` et l'injecte en `-p:` (`ApplicationDisplayVersion` + versionCode, soit `20309` pour 2.3.9). Les valeurs du csproj restent en repli
- [x] **Releases GitHub consolidées** : `v2.3.9` unique, avec l'installeur, l'APK et `Autotrad.exe`. Les releases `v2.3.7.0`, `AC2.3.7.0` et `v0.2` ont été supprimées avec leurs tags
- [x] **`audit-process.ps1` restauré** : il avait disparu (renommé en `.old`, probablement par Autotrad) et `build.bat` ignorait l'audit silencieusement

#### Décisions actées

- **Client Android** : le préfixe de tag `AC…` est abandonné, un seul numéro de version pour toute la suite
- **Styles WPF dupliqués** : volontaire — les modules doivent rester indépendants, ils ont vocation à devenir des add-ons de MCEMonitor. Pas de dictionnaire de ressources partagé

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
- [x] **Route `/restore-default-patterns`** + section « Zone de danger » dans la page web, avec bouton `↺ Restaurer les règles à l'origine` et confirmation nommant les 5 listes. Séquence alignée sur l'IPC : `RestoreDefaultPatterns` → `ReloadConfig` → `ReevaluateAllBlockedIps`
  - **Reste à valider** : clic réel sur le bouton en web (compilé et déployé, mais jamais exercé)

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
- **Audit complet des quatre serveurs web** *(clôturé le 2026-11-17)* : les 8 routes destructives sont en POST. Runtime vérifié pour NetMonitor, SystemMonitor et RomMonitor ; **MediaMonitor validé par revue de code uniquement** (aucune sauvegarde disponible ce jour-là).
- **Restent en GET, volontairement** : `/download`, `/export-pdf`, `/logout`, et toutes les routes de lecture et d'API.

#### Analyse — routes d'export et de rapport *(aucune modification nécessaire, 2026-11-17)*

- **`/export-pdf`** (NetMonitor) : le PDF est généré **en mémoire** (`MemoryStream` + `document.Save`), sans aucune écriture disque, puis renvoyé en `attachment`. Route authentifiée. Le `GET` est le **bon choix** : c'est un téléchargement déclenché par un simple lien `<a href="/export-pdf">`, qu'un `POST` casserait (il faudrait du JavaScript et un blob).
- **`/report`** (MediaMonitor) : rapport HTML construit depuis l'historique, en lecture seule, protégé par session (hors liste `isApiRoute`). Aucun appelant dans le dépôt — route conservée pour un usage manuel.
- **Correction d'une affirmation antérieure** : l'item d'origine prétendait que ces routes « écrivaient un fichier temporaire ». C'est faux, tout se fait en mémoire.

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