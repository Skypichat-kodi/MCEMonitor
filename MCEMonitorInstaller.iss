; ============================================
; Installeur MCEMonitor - Multilingue FR + EN
; Version 64 bits + Vérification .NET 8 Desktop
; ============================================

[Setup]
AppName=MCEMonitor
AppVersion=2.0.0
DefaultDirName={autopf}\MCEMonitor
DefaultGroupName=MCEMonitor
OutputDir=Installer
OutputBaseFilename=MCEMonitorSetup
Compression=lzma
SolidCompression=yes
UsedUserAreasWarning=no
WizardSmallImageFile=MCEMonitor.png
PrivilegesRequired=admin

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

[Files]
; --- PawnIO (pilote de lecture capteurs) ---
Source: "redist\PawnIO_setup.exe"; DestDir: "{tmp}"; \
    Flags: deleteafterinstall
; --- Fichiers destinés à Program Files (x64) ---
Source: "MCEMonitor Ver 1.0\ProgramFiles\*"; \
    DestDir: "{autopf}\MCEMonitor"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

; --- ProgramData : .config → ne pas remplacer ---
Source: "MCEMonitor Ver 1.0\ProgramData\*.config"; \
    DestDir: "{commonappdata}\MCEMonitor"; \
    Flags: ignoreversion onlyifdoesntexist

; --- Fichiers destinés à ProgramData (sauf .config) ---
Source: "MCEMonitor Ver 1.0\ProgramData\*"; \
    DestDir: "{commonappdata}\MCEMonitor"; \
    Excludes: "*.config"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MCEMonitor"; Filename: "{app}\MCEMonitor.exe"
Name: "{commondesktop}\MCEMonitor"; Filename: "{app}\MCEMonitor.exe"; WorkingDir: "{app}"

[Run]
; --- Installation silencieuse du pilote PawnIO (si absent) ---
Filename: "{tmp}\PawnIO_setup.exe"; \
    Parameters: "-install -silent"; \
    StatusMsg: "Installation du pilote PawnIO (capteurs matériels)..."; \
    Flags: runhidden waituntilterminated; \
    Check: not IsPawnIOInstalled
    
Filename: "taskkill.exe"; \
    Parameters: "/IM MediaMonitor.Service.exe /F"; \
    Flags: runhidden waituntilterminated
    
; Lancement AVEC UAC
Filename: "{app}\MCEMonitor.exe"; \
    Description: "{cm:LaunchProgram,MCEMonitor}"; \
    Flags: shellexec postinstall skipifsilent

[UninstallRun]
; --- Arrêt des processus ---
Filename: "taskkill.exe"; Parameters: "/IM MCEMonitor.exe /F";           Flags: runhidden; RunOnceId: "KillMCEM"
Filename: "taskkill.exe"; Parameters: "/IM MediaMonitor.Service.exe /F"; Flags: runhidden; RunOnceId: "KillMediaSvc"
Filename: "taskkill.exe"; Parameters: "/IM RomMonitor.Service.exe /F";   Flags: runhidden; RunOnceId: "KillRomSvc"
Filename: "taskkill.exe"; Parameters: "/IM MediaMonitor.Tray.exe /F";    Flags: runhidden; RunOnceId: "KillMediaTray"
Filename: "taskkill.exe"; Parameters: "/IM RomMonitor.Tray.exe /F";      Flags: runhidden; RunOnceId: "KillRomTray"

; --- Suppression des tâches planifiées (noms actuels) ---
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_MediaMonitorService"" /F"; Flags: runhidden; RunOnceId: "DelMediaSvcTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_MediaMonitorTray"" /F";    Flags: runhidden; RunOnceId: "DelMediaTrayTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_RomMonitorService"" /F";   Flags: runhidden; RunOnceId: "DelRomSvcTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_RomMonitorTray"" /F";      Flags: runhidden; RunOnceId: "DelRomTrayTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_WakeMonitor"" /F";         Flags: runhidden; RunOnceId: "DelWakeTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_Shutdown"" /F";            Flags: runhidden; RunOnceId: "DelShutdownTask"
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

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RAMKb: Int64;
  RAMGo: Integer;
begin
  Result := '';
  if GetPhysicallyInstalledSystemMemory(RAMKb) then
  begin
    // RAMKb est en kilo-octets -> conversion en Go
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