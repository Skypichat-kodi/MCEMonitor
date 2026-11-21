; ============================================
; Installeur MCEMonitor - Multilingue FR + EN
; Version 64 bits + Vérification .NET 8 Desktop
; ============================================

[Setup]
#ifndef MyAppVersion
  #error MyAppVersion n'est pas défini. Lancez build.bat, pas iscc.exe directement.
#endif
AppVersion={#MyAppVersion}
AppName=MCEMonitor
UninstallDisplayName=MCEMonitor {#MyAppVersion}
OutputBaseFilename=MCEMonitorSetup-{#MyAppVersion}
DefaultDirName={autopf}\MCEMonitor
DefaultGroupName=MCEMonitor
OutputDir=Installer
Compression=lzma
SolidCompression=yes
UsedUserAreasWarning=no
WizardSmallImageFile=InnoMCEMonitor.png
PrivilegesRequired=admin
CloseApplications=no
RestartApplications=no
LicenseFile=LICENSE_inno.txt

; Installeur 64 bits moderne
ArchitecturesInstallIn64BitMode=x64compatible

; Windows 10 minimum (build 10.0.10240 = Windows 10 RTM)
MinVersion=10.0.10240

; Icônes
SetupIconFile="MCEMonitor.UI\Assets\MCEMonitor.ico"
UninstallDisplayIcon={app}\MCEMonitor.exe

; Langues
ShowLanguageDialog=yes
DisableDirPage=no

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "deletetasks"; \
    Description: "Supprimer les tâches planifiées existantes avant l'installation"; \
    GroupDescription: "Tâches planifiées :"; \
    Flags: unchecked; \
    Components: core

; Choix des composants à installer
[Components]
Name: "core";   Description: "MCEMonitor (application principale, services, tray, UI)"; Types: full
Name: "client"; Description: "MCEMonitor Client (surveillance de vos serveurs MCE distants)"; Types: full

[InstallDelete]
; Nettoyage des vestiges d'anciennes versions
; ffmpeg était installé dans Program Files avant la version <nouvelle>.
; Il est maintenant uniquement dans ProgramData.
Type: filesandordirs; Name: "{autopf}\MCEMonitor\ffmpeg"

[Files]
Source: "LICENSE_inno.txt"; DestDir: "{app}"; Flags: ignoreversion

; --- PawnIO (pilote de lecture capteurs) ---
Source: "redist\PawnIO_setup.exe"; DestDir: "{tmp}"; \
    Flags: deleteafterinstall; \
    Components: core

; --- Fichiers destinés à Program Files (x64) ---
; (tous SAUF les fichiers du Client, les fichiers partagés
;  et les dossiers partagés Resources\ et Languages\)
Source: "MCEMonitor Ver 1.0\ProgramFiles\*"; \
    DestDir: "{autopf}\MCEMonitor"; \
    Excludes: "MCEMonitorClient.*,MCEMonitor.Languages.*,Microsoft.Toolkit.Uwp.Notifications.dll,Microsoft.Windows.SDK.NET.dll,WinRT.Runtime.dll,Resources\*,Languages\*"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; \
    Components: core

; --- ProgramData : .config → ne pas remplacer ---
Source: "MCEMonitor Ver 1.0\ProgramData\*.config"; \
    DestDir: "{commonappdata}\MCEMonitor"; \
    Flags: ignoreversion onlyifdoesntexist; \
    Components: core

; --- Fichiers destinés à ProgramData (sauf .config) ---
; (sauf aussi les binaires du MCEMonitor Client et les fichiers partagés)
; Le couple WinDivert (DLL + pilote) est exclu ici : il est déployé par
; DeployWinDivert() dans [Code], qui vérifie qu'il n'est pas verrouillé.
Source: "MCEMonitor Ver 1.0\ProgramData\*"; \
    DestDir: "{commonappdata}\MCEMonitor"; \
    Excludes: "*.config,MCEMonitorClient.*,MCEMonitor.Languages.*,runtimes\win-x64\native\WinDivert.dll,runtimes\win-x64\native\WinDivert64.sys"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; \
    Components: core

; --- WinDivert : extraits dans {tmp}, copiés par DeployWinDivert() ---
Source: "MCEMonitor Ver 1.0\ProgramData\runtimes\win-x64\native\WinDivert.dll"; \
    Flags: dontcopy; \
    Components: core

Source: "MCEMonitor Ver 1.0\ProgramData\runtimes\win-x64\native\WinDivert64.sys"; \
    Flags: dontcopy; \
    Components: core

; --- MCEMonitor Client : binaires dans Program Files ---
Source: "MCEMonitor Ver 1.0\ProgramFiles\MCEMonitorClient.*"; \
    DestDir: "{autopf}\MCEMonitor"; \
    Flags: ignoreversion; \
    Components: client

; --- MCEMonitor Client : dépendances Toast du Tray ---
Source: "MCEMonitor Ver 1.0\ProgramFiles\Microsoft.Toolkit.Uwp.Notifications.dll"; \
    DestDir: "{autopf}\MCEMonitor"; \
    Flags: ignoreversion; \
    Components: client

Source: "MCEMonitor Ver 1.0\ProgramFiles\Microsoft.Windows.SDK.NET.dll"; \
    DestDir: "{autopf}\MCEMonitor"; \
    Flags: ignoreversion; \
    Components: client

Source: "MCEMonitor Ver 1.0\ProgramFiles\WinRT.Runtime.dll"; \
    DestDir: "{autopf}\MCEMonitor"; \
    Flags: ignoreversion; \
    Components: client

; --- MCEMonitor Client : binaires du Service dans ProgramData ---
Source: "MCEMonitor Ver 1.0\ProgramData\MCEMonitorClient.*"; \
    DestDir: "{commonappdata}\MCEMonitor"; \
    Flags: ignoreversion; \
    Components: client

; --- Fichiers partagés entre MCEMonitor (core) et MCEMonitor Client ---
; (installés si core OU client est sélectionné, jamais dupliqués)
Source: "MCEMonitor Ver 1.0\ProgramFiles\MCEMonitor.Languages.dll"; \
    DestDir: "{autopf}\MCEMonitor"; \
    Flags: ignoreversion; \
    Components: core client

Source: "MCEMonitor Ver 1.0\ProgramFiles\MCEMonitor.Languages.pdb"; \
    DestDir: "{autopf}\MCEMonitor"; \
    Flags: ignoreversion; \
    Components: core client

Source: "MCEMonitor Ver 1.0\ProgramData\MCEMonitor.Languages.dll"; \
    DestDir: "{commonappdata}\MCEMonitor"; \
    Flags: ignoreversion; \
    Components: core client

Source: "MCEMonitor Ver 1.0\ProgramData\MCEMonitor.Languages.pdb"; \
    DestDir: "{commonappdata}\MCEMonitor"; \
    Flags: ignoreversion; \
    Components: core client

; --- Dossiers partagés (icônes, sons, traductions) ---
Source: "MCEMonitor Ver 1.0\ProgramFiles\Resources\*"; \
    DestDir: "{autopf}\MCEMonitor\Resources"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; \
    Components: core client

Source: "MCEMonitor Ver 1.0\ProgramFiles\Languages\*"; \
    DestDir: "{autopf}\MCEMonitor\Languages"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; \
    Components: core client

[Icons]
Name: "{group}\MCEMonitor"; Filename: "{app}\MCEMonitor.exe"; Components: core
Name: "{commondesktop}\MCEMonitor"; Filename: "{app}\MCEMonitor.exe"; WorkingDir: "{app}"; Components: core

; --- MCEMonitor Client (raccourci menu Démarrer + bureau) ---
Name: "{group}\MCEMonitor Client"; \
    Filename: "{app}\MCEMonitorClient.Config.exe"; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\MCEMonitorClient.ico"; \
    Components: client

Name: "{commondesktop}\MCEMonitor Client"; \
    Filename: "{app}\MCEMonitorClient.Config.exe"; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\MCEMonitorClient.ico"; \
    Components: client    

[Run]
; --- Installation silencieuse du pilote PawnIO (si absent) ---
Filename: "{tmp}\PawnIO_setup.exe"; \
    Parameters: "-install -silent"; \
    StatusMsg: "Installation du pilote PawnIO (capteurs matériels)..."; \
    Flags: runhidden waituntilterminated; \
    Check: not IsPawnIOInstalled
   
; --- Protection des *.web.config en lecture seule (après installation) ---
Filename: "icacls.exe"; \
    Parameters: """{commonappdata}\MCEMonitor\RomMonitor.web.config"" /inheritance:r /grant ""SYSTEM:(F)"" /grant ""BUILTIN\Administrators:(F)"" /grant ""BUILTIN\Users:(R)"""; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Protection de RomMonitor.web.config..."; \
    Check: FileExists(ExpandConstant('{commonappdata}\MCEMonitor\RomMonitor.web.config')); \
    Components: core

Filename: "icacls.exe"; \
    Parameters: """{commonappdata}\MCEMonitor\SystemMonitor.web.config"" /inheritance:r /grant ""SYSTEM:(F)"" /grant ""BUILTIN\Administrators:(F)"" /grant ""BUILTIN\Users:(R)"""; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Protection de SystemMonitor.web.config..."; \
    Check: FileExists(ExpandConstant('{commonappdata}\MCEMonitor\SystemMonitor.web.config')); \
    Components: core

Filename: "icacls.exe"; \
    Parameters: """{commonappdata}\MCEMonitor\NetMonitor.web.config"" /inheritance:r /grant ""SYSTEM:(F)"" /grant ""BUILTIN\Administrators:(F)"" /grant ""BUILTIN\Users:(R)"""; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Protection de NetMonitor.web.config..."; \
    Check: FileExists(ExpandConstant('{commonappdata}\MCEMonitor\NetMonitor.web.config')); \
    Components: core

Filename: "icacls.exe"; \
    Parameters: """{commonappdata}\MCEMonitor\MediaMonitor.web.config"" /inheritance:r /grant ""SYSTEM:(F)"" /grant ""BUILTIN\Administrators:(F)"" /grant ""BUILTIN\Users:(R)"""; \
    Flags: runhidden waituntilterminated; \
    StatusMsg: "Protection de MediaMonitor.web.config..."; \
    Check: FileExists(ExpandConstant('{commonappdata}\MCEMonitor\MediaMonitor.web.config')); \
    Components: core
        
; Lancement AVEC UAC — MCEMonitor (core)
Filename: "{app}\MCEMonitor.exe"; \
    Description: "{cm:LaunchProgram,MCEMonitor}"; \
    Flags: shellexec postinstall skipifsilent; \
    Components: core

; Lancement — MCEMonitor Client (Config)
Filename: "{app}\MCEMonitorClient.Config.exe"; \
    Description: "{cm:LaunchProgram,MCEMonitor Client}"; \
    Flags: shellexec postinstall skipifsilent nowait; \
    Components: client

[UninstallRun]
; --- Arrêt des processus MCEMonitor ---
Filename: "taskkill.exe"; Parameters: "/IM MCEMonitor.exe /F";           Flags: runhidden; RunOnceId: "KillMCEM"
; --- Arrêt des processus MCEMonitorClient ---
Filename: "taskkill.exe"; Parameters: "/IM MCEMonitorClient.Service.exe /F"; Flags: runhidden; RunOnceId: "KillClientSvc"
Filename: "taskkill.exe"; Parameters: "/IM MCEMonitorClient.Tray.exe /F";    Flags: runhidden; RunOnceId: "KillClientTray"
Filename: "taskkill.exe"; Parameters: "/IM MCEMonitorClient.Config.exe /F";  Flags: runhidden; RunOnceId: "KillClientCfg"
; --- Arrêt des processus MediaMonitor ---
Filename: "taskkill.exe"; Parameters: "/IM MediaMonitor.Service.exe /F"; Flags: runhidden; RunOnceId: "KillMediaSvc"
Filename: "taskkill.exe"; Parameters: "/IM MediaMonitor.Tray.exe /F";    Flags: runhidden; RunOnceId: "KillMediaTray"
Filename: "taskkill.exe"; Parameters: "/IM MediaMonitor.UI.exe /F";    Flags: runhidden; RunOnceId: "KillMediaTray"
; --- Arrêt des processus RomMonitor ---
Filename: "taskkill.exe"; Parameters: "/IM RomMonitor.Service.exe /F";   Flags: runhidden; RunOnceId: "KillRomSvc"
Filename: "taskkill.exe"; Parameters: "/IM RomMonitor.Tray.exe /F";      Flags: runhidden; RunOnceId: "KillRomTray"
Filename: "taskkill.exe"; Parameters: "/IM RomMonitor.UI.exe /F";      Flags: runhidden; RunOnceId: "KillRomTray"
; --- Arrêt des processus SystemMonitor ---
Filename: "taskkill.exe"; Parameters: "/IM SystemMonitor.Service.exe /F"; Flags: runhidden; RunOnceId: "KillSysSvc"
Filename: "taskkill.exe"; Parameters: "/IM SystemMonitor.Tray.exe /F";    Flags: runhidden; RunOnceId: "KillSysTray"
Filename: "taskkill.exe"; Parameters: "/IM SystemMonitor.UI.exe /F";    Flags: runhidden; RunOnceId: "KillSysTray"
; --- Arrêt des processus NetMonitor ---
Filename: "taskkill.exe"; Parameters: "/IM NetMonitor.Service.exe /F"; Flags: runhidden; RunOnceId: "KillNetSvc"
Filename: "taskkill.exe"; Parameters: "/IM NetMonitor.Tray.exe /F";    Flags: runhidden; RunOnceId: "KillNetTray"
Filename: "taskkill.exe"; Parameters: "/IM NetMonitor.UI.exe /F";      Flags: runhidden; RunOnceId: "KillNetUI"

; --- Suppression des tâches planifiées NetMonitor ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_NetMonitorService"" /F"; Flags: runhidden; RunOnceId: "DelNetSvcTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_NetMonitorTray"" /F";    Flags: runhidden; RunOnceId: "DelNetTrayTask"
; --- Suppression des tâches planifiées MediaMonitor ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_MediaMonitorService"" /F"; Flags: runhidden; RunOnceId: "DelMediaSvcTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_MediaMonitorTray"" /F";    Flags: runhidden; RunOnceId: "DelMediaTrayTask"
; --- Suppression des tâches planifiées RomMonitor ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_RomMonitorService"" /F";   Flags: runhidden; RunOnceId: "DelRomSvcTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_RomMonitorTray"" /F";      Flags: runhidden; RunOnceId: "DelRomTrayTask"
; --- Suppression des tâches planifiées SystemMonitor ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_SystemMonitorService"" /F"; Flags: runhidden; RunOnceId: "DelSysSvcTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_SystemMonitorTray"" /F";    Flags: runhidden; RunOnceId: "DelSysTrayTask"
; --- Suppression des tâches planifiées WakeMonitor ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_WakeMonitor"" /F";         Flags: runhidden; RunOnceId: "DelWakeTask"
; --- Suppression des tâches planifiées On / Off ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_Shutdown"" /F";            Flags: runhidden; RunOnceId: "DelShutdownTask"
; --- Suppression des tâches planifiées StopMonitor ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_StopMonitor_Boot"" /F";    Flags: runhidden; RunOnceId: "DelStopBootTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_StopMonitor_Shutdown"" /F";Flags: runhidden; RunOnceId: "DelStopShutdownTask"
; --- Suppression des tâches planifiées NetMonitor ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_NetMonitorService"" /F"; Flags: runhidden; RunOnceId: "DelNetSvcTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_NetMonitorTray"" /F";    Flags: runhidden; RunOnceId: "DelNetTrayTask"

; --- Suppression des anciens noms de tâches (migration) ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_Tray"" /F";                Flags: runhidden; RunOnceId: "DelOldTrayTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_Wake"" /F";                Flags: runhidden; RunOnceId: "DelOldWakeTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_Service"" /F";             Flags: runhidden; RunOnceId: "DelOldServiceTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_MediaService"" /F";        Flags: runhidden; RunOnceId: "DelOldMediaServiceTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_RomService"" /F";          Flags: runhidden; RunOnceId: "DelOldRomServiceTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_RomTray"" /F";             Flags: runhidden; RunOnceId: "DelOldRomTrayTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_StopMonitor"" /F";         Flags: runhidden; RunOnceId: "DelOldStopTask"

; --- Suppression des tâches planifiées MCEMonitor Client ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitorClient_Service"" /F";       Flags: runhidden; RunOnceId: "DelClientSvcTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitorClient_Tray"" /F";          Flags: runhidden; RunOnceId: "DelClientTrayTask"

[UninstallDelete]
Type: filesandordirs; Name: "{commonprograms}\MCEMonitor"

[CustomMessages]
fr.RAMError=MCEMonitor nécessite au moins 8 Go de RAM.%nRAM détectée : %1 Go.%n%nL'installation va s'arrêter.
en.RAMError=MCEMonitor requires at least 8 GB of RAM.%nDetected RAM: %1 GB.%n%nSetup will now exit.

[Code]
// ============================================================
//  Attend que la clé de registre WinDivert ait disparu
//  (ou timeout après ~10 secondes)
// ============================================================
function WaitForWinDivertCleanup(MaxWaitMs: Integer): Boolean;
var
  Elapsed: Integer;
begin
  Result := False;
  Elapsed := 0;

  while Elapsed < MaxWaitMs do
  begin
    // RegKeyExists vérifie l'existence de la CLÉ (pas d'une valeur)
    if RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\WinDivert') then
    begin
      Sleep(500);
      Elapsed := Elapsed + 500;
      Log('WaitForWinDivertCleanup : clé encore présente (' + IntToStr(Elapsed) + ' ms)');
    end
    else
    begin
      Log('WaitForWinDivertCleanup : clé supprimée après ' + IntToStr(Elapsed) + ' ms');
      Result := True;
      Exit;
    end;
  end;

  Log('WaitForWinDivertCleanup : timeout après ' + IntToStr(MaxWaitMs) + ' ms');
end;

// ============================================================
//  Déploiement du couple WinDivert (WinDivert.dll + WinDivert64.sys)
//
//  Ces deux fichiers ne sont PAS copiés par la section [Files] : le
//  pilote reste mappé par le noyau tant que son déchargement n'est pas
//  terminé (état STOP_PENDING), et Windows refuse alors l'écriture.
//
//  Principe :
//    - fichiers identiques            → aucune copie
//    - fichiers remplaçables          → remplacement des DEUX ensemble
//    - un des deux verrouillé         → on ne touche à rien (paire intacte)
//
//  Le test de verrou est un renommage : un pilote chargé ne peut pas
//  être renommé, c'est fiable et sans effet de bord sur un fichier libre.
// ============================================================
function WinDivertDiffers(const NewFile, ExistingFile: String): Boolean;
var
  SizeNew, SizeExisting: Integer;
  VersionNew, VersionExisting: String;
begin
  if not FileExists(ExistingFile) then
  begin
    Result := True;
    Exit;
  end;

  // FileSize(Name, var Size) : renvoie False si le fichier est illisible
  if FileSize(NewFile, SizeNew) and FileSize(ExistingFile, SizeExisting) and
     (SizeNew <> SizeExisting) then
  begin
    Result := True;
    Exit;
  end;

  // Même taille (ou taille illisible pour l'un des deux) : on compare les
  // versions, si les deux fichiers en exposent une
  if GetVersionNumbersString(NewFile, VersionNew) and
     GetVersionNumbersString(ExistingFile, VersionExisting) then
    Result := VersionNew <> VersionExisting
  else
    Result := False;   // pas de version exploitable → considéré identique
end;

procedure DeployWinDivert();
var
  Dir, NewDll, NewSys, DestDll, DestSys, OldDll, OldSys: String;
  NeedDll, NeedSys, Copied: Boolean;
begin
  Dir     := ExpandConstant('{commonappdata}\MCEMonitor\runtimes\win-x64\native');
  DestDll := AddBackslash(Dir) + 'WinDivert.dll';
  DestSys := AddBackslash(Dir) + 'WinDivert64.sys';
  OldDll  := DestDll + '.old';
  OldSys  := DestSys + '.old';

  try
    ExtractTemporaryFile('WinDivert.dll');
    ExtractTemporaryFile('WinDivert64.sys');
  except
    Log('WinDivert : extraction des fichiers embarqués impossible, déploiement ignoré');
    Exit;
  end;

  NewDll := ExpandConstant('{tmp}\WinDivert.dll');
  NewSys := ExpandConstant('{tmp}\WinDivert64.sys');

  NeedDll := WinDivertDiffers(NewDll, DestDll);
  NeedSys := WinDivertDiffers(NewSys, DestSys);

  if not (NeedDll or NeedSys) then
  begin
    Log('WinDivert : DLL et pilote déjà à jour, aucune copie');
    Exit;
  end;

  if NeedDll then Log('WinDivert : WinDivert.dll diffère de la version installée');
  if NeedSys then Log('WinDivert : WinDivert64.sys diffère de la version installée');

  if not DirExists(Dir) then ForceDirectories(Dir);

  DeleteFile(OldDll);
  DeleteFile(OldSys);

  // 1. Ecarter les anciens fichiers (le renommage échoue si le pilote est chargé)
  if FileExists(DestSys) and not RenameFile(DestSys, OldSys) then
  begin
    Log('WinDivert : pilote verrouillé (chargé) - versions installées conservées');
    if not WizardSilent then
      MsgBox('Le pilote WinDivert est encore en cours de déchargement.' + #13#10 +
             'Les fichiers déjà installés ont été conservés : NetMonitor fonctionnera normalement.' + #13#10 + #13#10 +
             'Si une mise à jour du pilote était attendue, fermez tous les programmes utilisant WinDivert ' + #13#10 +
             'puis relancez l''installation.',
             mbInformation, MB_OK);
    Exit;
  end;

  if FileExists(DestDll) then
    RenameFile(DestDll, OldDll);

  // 2. Copier les nouveaux fichiers, toujours par paire
  Copied := CopyFile(NewSys, DestSys, False) and CopyFile(NewDll, DestDll, False);

  if Copied then
  begin
    DeleteFile(OldSys);
    DeleteFile(OldDll);
    Log('WinDivert : DLL et pilote mis à jour');
    Exit;
  end;

  // 3. Echec : on restaure la paire précédente
  DeleteFile(DestSys);
  DeleteFile(DestDll);
  if FileExists(OldSys) then RenameFile(OldSys, DestSys);
  if FileExists(OldDll) then RenameFile(OldDll, DestDll);

  Log('WinDivert : copie impossible - versions précédentes restaurées');
  if not WizardSilent then
    MsgBox('La mise à jour du pilote WinDivert n''a pas pu être effectuée.' + #13#10 +
           'Les fichiers précédents ont été restaurés : NetMonitor fonctionnera normalement.',
           mbInformation, MB_OK);
end;
// ============================================================
//  Détection du pilote PawnIO (version fiable)
//  Vérifie la clé de registre de désinstallation — méthode
//  utilisée par LibreHardwareMonitor lui-même.
// ============================================================
function IsPawnIOInstalled(): Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKLM,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO',
      'DisplayVersion', Version) or
    RegQueryStringValue(HKLM,
      'SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO',
      'DisplayVersion', Version);
end;

function GetPhysicallyInstalledSystemMemory(var TotalMemoryInKilobytes: Int64): Boolean;
  external 'GetPhysicallyInstalledSystemMemory@kernel32.dll stdcall';

// ============================================================
//  Arrêt des process + suppression des tâches
//  via un fichier batch temporaire (contourne les limites d'Inno)
// ============================================================
procedure StopAllAndCleanTasks();
var
  BatchFile: string;
  BatchContent: TStringList;
  ResultCode: Integer;
begin
  BatchFile := ExpandConstant('{tmp}\kill_mcem.bat');

  BatchContent := TStringList.Create;
  try
    BatchContent.Add('@echo off');

    // ─── Suppression des tâches UNIQUEMENT si l'utilisateur a coché ───
    if WizardIsTaskSelected('deletetasks') then
    begin
      Log('=== Suppression des tâches planifiées (choix utilisateur) ===');
      
      // --- MediaMonitor ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_MediaMonitorService" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_MediaMonitorTray" /F >nul 2>&1');
      // --- SystemMonitor ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_SystemMonitorService" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_SystemMonitorTray" /F >nul 2>&1');
      // --- RomMonitor ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_RomMonitorService" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_RomMonitorTray" /F >nul 2>&1');
      // --- WakeMonitor ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_WakeMonitor" /F >nul 2>&1');
      // --- ON/OFF ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_Shutdown" /F >nul 2>&1');
      // --- StopMonitor ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_StopMonitor_Boot" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_StopMonitor_Shutdown" /F >nul 2>&1');
      // --- MCEMonitor Client ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitorClient_Service" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitorClient_Tray" /F >nul 2>&1');
      // --- NetMonitor ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_NetMonitorService" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_NetMonitorTray" /F >nul 2>&1');
      // --- Anciens noms (migration) ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_Tray" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_Wake" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_Service" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_MediaService" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_RomService" /F >nul 2>&1');
    end
    else
      Log('=== Conservation des tâches planifiées (choix utilisateur) ===');
      
    // ─── Arrêt et suppression du service WinDivert (NetMonitor) ───
    BatchContent.Add('sc stop WinDivert >nul 2>&1');
    BatchContent.Add('sc delete WinDivert >nul 2>&1');
    // ─── Kill des process (TOUJOURS, obligatoire pour remplacer les fichiers) ───
    BatchContent.Add('taskkill /F /IM MCEMonitor.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM MCEMonitorClient.Service.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM MCEMonitorClient.Tray.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM MCEMonitorClient.Config.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM MediaMonitor.Service.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM MediaMonitor.Tray.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM MediaMonitor.UI.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM SystemMonitor.Service.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM SystemMonitor.Tray.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM SystemMonitor.UI.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM RomMonitor.Service.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM RomMonitor.Tray.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM RomMonitor.UI.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM NetMonitor.Service.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM NetMonitor.Tray.exe /T >nul 2>&1');
    BatchContent.Add('taskkill /F /IM NetMonitor.UI.exe /T >nul 2>&1');
    BatchContent.Add('exit /b 0');

    BatchContent.SaveToFile(BatchFile);
  finally
    BatchContent.Free;
  end;

  Log('=== Exécution du script d''arrêt (batch) ===');
  Exec(BatchFile, '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('=== Script terminé (code ' + IntToStr(ResultCode) + ') ===');
  
    // ⚡ Attendre que Windows finalise la suppression de WinDivert
  WaitForWinDivertCleanup(10000);   // jusqu'à 10 secondes

  Sleep(3000);
end;

// ============================================================
//  Appelé par Inno AVANT que les fichiers ne soient écrits
//  → arrête les process, supprime les tâches, vérifie la RAM
// ============================================================
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RAMKb: Int64;
  RAMGo: Integer;
begin
  Result := '';

  // ⚡ 1) Arrêt de tous les process MCEMonitor + tâches planifiées
  StopAllAndCleanTasks();

  // ⚡ 2) Vérification RAM (min 8 Go)
  if GetPhysicallyInstalledSystemMemory(RAMKb) then
  begin
    RAMGo := RAMKb div 1024 div 1024;
    if RAMGo < 8 then
      Result := FmtMessage(CustomMessage('RAMError'), [IntToStr(RAMGo)]);
  end;
end;

// ============================================================
//  Message si PawnIO n'a pas pu être installé
//  (affiché uniquement en mode interactif, en fin d'installation)
// ============================================================
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Le couple WinDivert est déployé ici, une fois les autres fichiers en place.
    // Il n'est copié que s'il diffère ET qu'il n'est pas verrouillé par un pilote chargé.
    DeployWinDivert();

    if not WizardSilent then
    begin
      if not IsPawnIOInstalled() then
      begin
        MsgBox(
          'Le pilote PawnIO n''a pas pu être installé.' + #13#10 +
          'Les températures CPU/GPU ne seront pas disponibles.' + #13#10 +
          'Installez PawnIO manuellement depuis https://pawnio.eu/',
          mbInformation, MB_OK);
      end;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir, BackupDir: String;
  CRLF, MsgText: String;
  ConfigFiles: TArrayOfString;
  i: Integer;
  SourceFile, DestFile: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if UninstallSilent then
      Exit;

    DataDir := ExpandConstant('{commonappdata}\MCEMonitor');

    if not DirExists(DataDir) then
      Exit;

    CRLF := Chr(13) + Chr(10);

    MsgText := 'Voulez-vous supprimer les fichiers de log et les fichiers temporaires ?' + CRLF + CRLF +
               'Dossier : ' + DataDir + CRLF + CRLF +
               'Oui = supprimer les logs et fichiers temporaires' + CRLF +
               '        (les fichiers de configuration seront CONSERVÉS)' + CRLF +
               'Non = tout conserver';

    if MsgBox(MsgText, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      // ── 1) Liste des fichiers de configuration à conserver ──
      ConfigFiles := [
          // ── Configs principales ──
          'email.config',
          'MCEMonitorClient.config',
          'MediaMonitor.config',
          'NetMonitor.config',
          'RomMonitor.config',
          'Shutdown.config',
          'SystemMonitor.config',
          'theme.config',
          'WakeMonitor.config',

          // ── Configs Web (*.web.config) ──
          'MediaMonitor.web.config',
          'NetMonitor.web.config',
          'RomMonitor.web.config',
          'SystemMonitor.web.config',

          // ── Données NetMonitor ──
          'NetMonitor\blocked_ips.json',
          'NetMonitor\alert_history.json',
          'NetMonitor\audit_history.json',
          'NetMonitor\manual_safe_ips.json',
          'NetMonitor\trusted_processes.json',
          'NetMonitor\blocked_ip_retries.json',
          'NetMonitor\blacklist.txt',
          'NetMonitor\blacklist_auto.txt'
      ];

      // ── 2) Sauvegarde des .config dans un dossier temporaire ──
      BackupDir := ExpandConstant('{commonappdata}\MCEMonitor_backup');
      ForceDirectories(BackupDir);

      for i := 0 to GetArrayLength(ConfigFiles) - 1 do
      begin
          SourceFile := AddBackslash(DataDir) + ConfigFiles[i];
          DestFile   := AddBackslash(BackupDir) + ConfigFiles[i];

          if FileExists(SourceFile) then
          begin
              // Crée le sous-dossier de destination si besoin (ex: NetMonitor\)
              ForceDirectories(ExtractFileDir(DestFile));
              CopyFile(SourceFile, DestFile, False);
              Log('Sauvegardé : ' + ConfigFiles[i]);
          end;
      end;

      // ── 3) Suppression complète du dossier ──
      DelTree(DataDir, True, True, True);

      // ── 4) Restauration du dossier + des fichiers sauvegardés ──
      ForceDirectories(DataDir);

      for i := 0 to GetArrayLength(ConfigFiles) - 1 do
      begin
        SourceFile := AddBackslash(BackupDir) + ConfigFiles[i];
        DestFile   := AddBackslash(DataDir) + ConfigFiles[i];

        if FileExists(SourceFile) then
        begin
          // Crée le sous-dossier de destination (ex: NetMonitor\)
          ForceDirectories(ExtractFileDir(DestFile));
          CopyFile(SourceFile, DestFile, False);
          Log('Config restauré : ' + ConfigFiles[i]);
        end;
      end;

      // ── 5) Nettoyage du dossier de sauvegarde ──
      DelTree(BackupDir, True, True, True);
    end;
  end;
end;