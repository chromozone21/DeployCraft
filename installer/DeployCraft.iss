; Installeur DeployCraft : installe le service (démarrage automatique) et la console Windows.
; Rien d'autre à installer : .NET est embarqué.
;
; Compilation : build.ps1 à la racine du dépôt.

#define AppName "DeployCraft"
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#define ServiceName "DeployCraft"

[Setup]
AppId={{6F1C2B7E-4D0A-4C61-9E3B-DEC0DE0A0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
AppPublisherURL=https://github.com/vzeol/DeployCraft
AppCopyright=Copyright (C) 2026 vzeol - GPL v3
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=DeployCraft-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName={#AppName}
CloseApplications=no

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[InstallDelete]
; Interface web de la v0.1, remplacée par la console Windows.
Type: filesandordirs; Name: "{app}\wwwroot"
Type: files; Name: "{app}\DeployCraft.url"
Type: files; Name: "{group}\DeployCraft.url"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DeployCraft"; Filename: "{app}\DeployCraft.exe"
Name: "{autodesktop}\DeployCraft"; Filename: "{app}\DeployCraft.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"

[Run]
; Service sous LocalSystem : administrateur du serveur. Les accès aux postes utilisent le compte de déploiement.
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\DeployCraft.Server.exe\"""" start= delayed-auto DisplayName= ""DeployCraft"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Serveur de déploiement de logiciels DeployCraft"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden
Filename: "{app}\DeployCraft.exe"; Description: "Ouvrir DeployCraft"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\timeout.exe"; Parameters: "/t 3 /nobreak"; Flags: runhidden; RunOnceId: "WaitService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"

[Code]
// Mise à jour : on arrête et retire l'ancien service avant de remplacer les fichiers.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(3000);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
  Result := '';
end;
