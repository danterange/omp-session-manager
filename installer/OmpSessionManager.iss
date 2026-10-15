#ifndef MyAppVersion
  #error MyAppVersion must be supplied by the release script.
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{EAB3FFCE-C17A-4BC8-8AC4-E971A84F40EA}
AppName=OMP Session Manager
AppVersion={#MyAppVersion}
AppVerName=OMP Session Manager {#MyAppVersion}
DefaultDirName={localappdata}\Programs\OmpSessionManager
DefaultGroupName=OMP Session Manager
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=OmpSessionManager-Setup-{#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName=OMP Session Manager

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "compiler:ChineseSimplified.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\OMP Session Manager"; Filename: "{app}\OmpSessionManager.exe"
Name: "{autodesktop}\OMP Session Manager"; Filename: "{app}\OmpSessionManager.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\OmpSessionManager.exe"; Description: "Launch OMP Session Manager"; Flags: nowait postinstall skipifsilent
