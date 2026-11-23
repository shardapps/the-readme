#ifndef Rid
  #error Rid is required: win-x64 or win-arm64
#endif
#ifndef Version
  #define Version "1.0.0"
#endif
#define Product "The Readme"
#define ProgId "ShardApps.TheReadme.Document"
#define StateKey "Software\ShardApps\TheReadme"
#define SourcePath "..\artifacts\publish\" + Version + "\" + Rid
#if Rid == "win-arm64"
  #define Arch "arm64"
  #define MinWindows "10.0.22621"
#else
  #define Arch "x64compatible"
  #define MinWindows "10.0.19045"
#endif

[Setup]
AppId=ShardApps.TheReadme
AppName={#Product}
AppVersion={#Version}
AppPublisher=Shard Apps
AppPublisherURL=https://github.com/shardapps/the-readme
AppSupportURL=https://github.com/shardapps/the-readme/issues
AppUpdatesURL=https://github.com/shardapps/the-readme/releases
DefaultDirName={localappdata}\Programs\The Readme
DefaultGroupName={#Product}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed={#Arch}
ArchitecturesInstallIn64BitMode={#Arch}
OutputDir=..\artifacts\installers
OutputBaseFilename=The-Readme-{#Version}-{#Rid}-setup
SetupIconFile=..\assets\readme.ico
UninstallDisplayIcon={app}\Readme.exe
UninstallDisplayName={#Product}
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
MinVersion={#MinWindows}
CloseApplications=yes

[Tasks]
Name: path; Description: "Add the readme command to my user PATH"; Flags: unchecked
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourcePath}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\The Readme"; Filename: "{app}\Readme.exe"
Name: "{userdesktop}\The Readme"; Filename: "{app}\Readme.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "{#StateKey}"; ValueType: string; ValueName: "InstallDirectory"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "{#StateKey}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#Product}"
Root: HKCU; Subkey: "{#StateKey}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Edit Markdown, plain text, and source files."
Root: HKCU; Subkey: "{#StateKey}\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\Readme.exe,0"
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#Product}"; ValueData: "{#StateKey}\Capabilities"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueData: "The Readme document"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: "{app}\Readme.exe,0"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\Readme.exe"" -- ""%1"""
Root: HKCU; Subkey: "Software\Classes\Applications\Readme.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#Product}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\Readme.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Readme.exe"" -- ""%1"""
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdown"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.mdown\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkd"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.mkd\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkdn"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.mkdn\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdwn"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.mdwn\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdtxt"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.mdtxt\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".txt"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".text"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.text\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "{#StateKey}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".hdl"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\Classes\.hdl\OpenWithProgids"; ValueType: none; ValueName: "{#ProgId}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Readme.exe"; Description: "Launch The Readme"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Readme.exe"; Parameters: "--installer-remove-path"; Flags: runhidden; RunOnceId: "RemoveTheReadmePath"

[Code]
procedure CurStepChanged(Step: TSetupStep);
var
  Code: Integer;
begin
  if (Step = ssPostInstall) and WizardIsTaskSelected('path') then
    if not Exec(ExpandConstant('{app}\Readme.exe'), '--installer-add-path', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      SuppressibleMsgBox('The Readme installed. Open Settings in The Readme to retry adding the command to PATH.', mbError, MB_OK, IDOK);
end;
