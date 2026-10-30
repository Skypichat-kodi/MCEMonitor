﻿# TODO MCEMonitor

TODO global du projet, organisé par thème.

---

## 🔧 En cours

_(rien)_

## 📋 À faire

### Colonnes « Nom de domaine »

- [ ] Ajouter colonne `ReverseDns` dans le tableau IP bloquées (UI + Web)
- [ ] Ajouter colonne `ReverseDns` dans le tableau Interceptions/récidive (UI + Web)
- [ ] Ajouter colonne `ReverseDns` dans le tableau Historique intrusions (UI + Web)
- [ ] Étendre `BlockedIpRetryInfo` et `IntrusionEvent` avec le champ `ReverseDns`
- [ ] Arbitrer : reverse DNS brut (`31-14-254-18.infrawat.ch`) ou nom court extrait ?

### Améliorations UX

- [ ] Filtre « Type » inactif sur IP bloquées (UI WPF) — `cmbBlockedType` jamais lu dans `BlockedFilter_Changed`
- [ ] Alignement des tris UI ↔ Web sur les autres tableaux (Intrusions actives, Historique, Audit…)
- [ ] Bouton « Vider les problèmes signalés » dans l'onglet Réglages (finir la popup WinDivert)
- [ ] Top ports distants (remplace « Activité par heure » dans Stats section 2)
- [ ] Top processus en attente dans la section 1 de Statistiques

### MediaMonitor — vignettes album

- [x] CoverFinder : folder.jpg, cover.jpg, poster.jpg, {nom_fichier}.jpg, {nom_dossier}.jpg
- [ ] Tester avec MP3 sans ID3 + folder.jpg
- [ ] Tester avec vidéo codec KO + poster.jpg
- [ ] (optionnel) Vérification taille minimale cover (< 100×100 px)
- [ ] (optionnel) Support WebP

### Décisions produit (à trancher)

- [ ] Vider `TrustedScannerPatterns` (philosophie « tout bot est maudit »)
  - Actuellement : liste gardée (Shodan, Censys, ONYPHE… classés `Trusted` vert moyen)
  - Alternative : vider la liste → tous les scans deviennent `Warning` par défaut

---

## ✅ Terminé

### Session 2026-10-31 — Ajustements UI Web (Badges & Tableaux)

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