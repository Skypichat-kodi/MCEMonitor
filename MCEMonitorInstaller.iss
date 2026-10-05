; ============================================
; Installeur MCEMonitor - Multilingue FR + EN
; Version 64 bits + Vérification .NET 8 Desktop
; ============================================

[Setup]
AppName=MCEMonitor
AppVersion=2.1.5
DefaultDirName={autopf}\MCEMonitor
DefaultGroupName=MCEMonitor
OutputDir=Installer
OutputBaseFilename=MCEMonitorSetup
Compression=lzma
SolidCompression=yes
UsedUserAreasWarning=no
WizardSmallImageFile=MCEMonitor.png
PrivilegesRequired=admin
CloseApplications=no
RestartApplications=no

; Installeur 64 bits moderne
ArchitecturesInstallIn64BitMode=x64compatible

; Windows 10 minimum (build 10.0.10240 = Windows 10 RTM)
MinVersion=10.0.10240

; Icônes
SetupIconFile="MediaMonitor\MediaMonitor.Tray\MediaMonitor.ico"
UninstallDisplayIcon="{app}\MediaMonitor.ico"

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
    Flags: unchecked

; Choix des composants à installer
[Components]
Name: "core";   Description: "MCEMonitor (application principale, services, tray, UI)"; Types: full
Name: "client"; Description: "MCEMonitor Client (surveillance de vos serveurs MCE distants)"; Types: full

[Files]
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
Source: "MCEMonitor Ver 1.0\ProgramData\*"; \
    DestDir: "{commonappdata}\MCEMonitor"; \
    Excludes: "*.config,MCEMonitorClient.*,MCEMonitor.Languages.*"; \
    Flags: ignoreversion recursesubdirs createallsubdirs; \
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
Name: "{group}\MCEMonitor Client - Configuration"; Filename: "{app}\MCEMonitorClient.Config.exe"; Components: client

[Run]
; --- Installation silencieuse du pilote PawnIO (si absent) ---
Filename: "{tmp}\PawnIO_setup.exe"; \
    Parameters: "-install -silent"; \
    StatusMsg: "Installation du pilote PawnIO (capteurs matériels)..."; \
    Flags: runhidden waituntilterminated; \
    Check: not IsPawnIOInstalled
    
; Lancement AVEC UAC
Filename: "{app}\MCEMonitor.exe"; \
    Description: "{cm:LaunchProgram,MCEMonitor}"; \
    Flags: shellexec postinstall skipifsilent; \
    Components: core

[UninstallRun]
; --- Arrêt des processus ---
Filename: "taskkill.exe"; Parameters: "/IM MCEMonitor.exe /F";           Flags: runhidden; RunOnceId: "KillMCEM"
Filename: "taskkill.exe"; Parameters: "/IM MCEMonitorClient.Service.exe /F"; Flags: runhidden; RunOnceId: "KillClientSvc"
Filename: "taskkill.exe"; Parameters: "/IM MCEMonitorClient.Tray.exe /F";    Flags: runhidden; RunOnceId: "KillClientTray"
Filename: "taskkill.exe"; Parameters: "/IM MCEMonitorClient.Config.exe /F";  Flags: runhidden; RunOnceId: "KillClientCfg"
Filename: "taskkill.exe"; Parameters: "/IM MediaMonitor.Service.exe /F"; Flags: runhidden; RunOnceId: "KillMediaSvc"
Filename: "taskkill.exe"; Parameters: "/IM RomMonitor.Service.exe /F";   Flags: runhidden; RunOnceId: "KillRomSvc"
Filename: "taskkill.exe"; Parameters: "/IM MediaMonitor.Tray.exe /F";    Flags: runhidden; RunOnceId: "KillMediaTray"
Filename: "taskkill.exe"; Parameters: "/IM RomMonitor.Tray.exe /F";      Flags: runhidden; RunOnceId: "KillRomTray"
Filename: "taskkill.exe"; Parameters: "/IM SystemMonitor.Service.exe /F"; Flags: runhidden; RunOnceId: "KillSysSvc"
Filename: "taskkill.exe"; Parameters: "/IM SystemMonitor.Tray.exe /F";    Flags: runhidden; RunOnceId: "KillSysTray"

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

[CustomMessages]
fr.RAMError=MCEMonitor nécessite au moins 8 Go de RAM.%nRAM détectée : %1 Go.%n%nL'installation va s'arrêter.
en.RAMError=MCEMonitor requires at least 8 GB of RAM.%nDetected RAM: %1 GB.%n%nSetup will now exit.

[Code]
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

      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_MediaMonitorService" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_MediaMonitorTray" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_SystemMonitorService" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_SystemMonitorTray" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_RomMonitorService" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_RomMonitorTray" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_WakeMonitor" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_Shutdown" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_StopMonitor_Boot" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_StopMonitor_Shutdown" /F >nul 2>&1');
      // --- MCEMonitor Client ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitorClient_Service" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitorClient_Tray" /F >nul 2>&1');
      // --- Anciens noms (migration) ---
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_Tray" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_Wake" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_Service" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_MediaService" /F >nul 2>&1');
      BatchContent.Add('schtasks /Delete /TN "MCEMonitor_RomService" /F >nul 2>&1');
    end
    else
      Log('=== Conservation des tâches planifiées (choix utilisateur) ===');

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

    BatchContent.Add('exit /b 0');

    BatchContent.SaveToFile(BatchFile);
  finally
    BatchContent.Free;
  end;

  Log('=== Exécution du script d''arrêt (batch) ===');
  Exec(BatchFile, '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('=== Script terminé (code ' + IntToStr(ResultCode) + ') ===');

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
  DataDir: String;
  CRLF: String;
  MsgText: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if UninstallSilent then
      Exit;

    DataDir := ExpandConstant('{commonappdata}\MCEMonitor');

    if not DirExists(DataDir) then
      Exit;

    CRLF := Chr(13) + Chr(10);

    MsgText := 'Voulez-vous supprimer les fichiers de configuration et les logs ?' + CRLF + CRLF +
               'Dossier : ' + DataDir + CRLF + CRLF +
               'Oui = tout supprimer' + CRLF +
               'Non = tout conserver';

    if MsgBox(MsgText, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(DataDir, True, True, True);
    end;
  end;
end;