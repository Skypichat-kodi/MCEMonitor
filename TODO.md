# TODO MCEMonitor

TODO global du projet, organisé par thème.

---

## 🔧 En cours

_(rien)_

## 📋 À faire

### Refonte de la sévérité (basée sur ReverseDns + ISP)

- [ ] Créer `SeverityScorer` dans `NetMonitor.Service/Services/`
- [ ] Taxonomie : Critical / Warning / Green 1-2-3 (Safe / Scanner / Whitelist)
- [ ] Config : `CriticalPatterns`, `ScannerPatterns`, `TrustedPatterns`, `WhitelistPatterns`
- [ ] Intégrer dans `AddIntrusion` et `BlockIp`
- [ ] Migration one-shot : ré-évaluer `Severity` des blocked_ips.json existants
- [ ] Adapter `SeverityBadgeTemplate` (UI) et CSS `.badge.ok` (Web)
- [ ] Arbitrer : Reverse DNS brut (`*.infrawat.ch`) ou domaine extrait (`infrawat.ch`)
- [ ] Arbitrer : config fichier plat ou nouvelle UI dans Réglages
- [ ] Arbitrer : garder ou vider `TrustedScannerPatterns`

### Colonnes « Nom de domaine »

- [ ] Ajouter colonne `ReverseDns` dans le tableau IP bloquées (UI + Web)
- [ ] Ajouter colonne `ReverseDns` dans le tableau Interceptions/récidive (UI + Web)
- [ ] Ajouter colonne `ReverseDns` dans le tableau Historique intrusions (UI + Web)
- [ ] Étendre `BlockedIpRetryInfo` et `IntrusionEvent` avec le champ `ReverseDns`

### Améliorations UX

- [ ] Filtre « Type » inactif sur IP bloquées (UI WPF)
- [ ] Alignement des tris UI ↔ Web sur les autres tableaux
- [ ] Bouton « Vider les problèmes signalés » dans l'onglet Réglages
- [ ] Top ports distants (remplace « Activité par heure »)
- [ ] Top processus en attente dans la section 1 de Statistiques

### MediaMonitor — vignettes album

- [x] CoverFinder : folder.jpg, cover.jpg, poster.jpg, {nom_fichier}.jpg, {nom_dossier}.jpg
- [ ] Tester avec MP3 sans ID3 + folder.jpg
- [ ] Tester avec vidéo codec KO + poster.jpg
- [ ] (optionnel) Vérification taille minimale cover (< 100×100 px)
- [ ] (optionnel) Support WebP

### Décisions produit (à trancher)

- [ ] Vider `TrustedScannerPatterns` (philosophie « tout bot est maudit »)

---

## ✅ Terminé

### Session 2026-10-30
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