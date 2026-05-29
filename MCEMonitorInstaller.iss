; ============================================
; Installeur MCEMonitor - Multilingue FR + EN
; Version 64 bits + Vérification .NET 8 Desktop
; ============================================

[Setup]
AppName=MCEMonitor
AppVersion=1.1
DefaultDirName={autopf}\MCEMonitor
DefaultGroupName=MCEMonitor
OutputDir=Installer
OutputBaseFilename=MCEMonitorSetup
Compression=lzma
SolidCompression=yes
UsedUserAreasWarning=no

; Installeur 64 bits moderne
ArchitecturesInstallIn64BitMode=x64compatible

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
; --- Fichiers destinés à Program Files (x64) ---
Source: "MCEMonitor Ver 1.0\ProgramFiles\*"; \
    DestDir: "{autopf}\MCEMonitor"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

; --- Fichiers destinés à ProgramData ---
Source: "MCEMonitor Ver 1.0\ProgramData\*"; \
    DestDir: "{commonappdata}\MCEMonitor"; \
    Flags: ignoreversion recursesubdirs createallsubdirs


[Icons]
Name: "{group}\MCEMonitor"; Filename: "{app}\MCEMonitor.exe"
Name: "{group}\MCEMonitor"; Filename: "{app}\MCEMonitor.exe"
Name: "{commondesktop}\MediaMonitor UI"; Filename: "{app}\MediaMonitor.UI.exe"; WorkingDir: "{app}"

[Run]
Filename: "taskkill.exe"; \
    Parameters: "/IM MediaMonitor.Service.exe /F"; \
    Flags: runhidden waituntilterminated
    
; Lancement AVEC UAC
Filename: "{app}\MCEMonitor.exe"; \
    Description: "{cm:LaunchProgram,MCEMonitor}"; \
    Flags: shellexec postinstall skipifsilent

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_MediaMonitorService"" /F"; Flags: runhidden; RunOnceId: "DelMediaTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_Wake"" /F"; Flags: runhidden; RunOnceId: "DelWakeTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_StopMonitor"" /F"; Flags: runhidden; RunOnceId: "DelStopTask"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""MCEMonitor_Shutdown"" /F"; Flags: runhidden; RunOnceId: "DelShutdownTask"