; Inno Setup script for FreeScreen Recorder.
; Build with: .\build.ps1
; Direct compile: ISCC.exe FreeScreenRecorder.iss

#define AppName "FreeScreen Recorder"
#define AppExeName "FreeScreenRecorder.exe"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef AppPublisher
  #define AppPublisher "AppPublisher"
#endif
#ifndef AppURL
  #define AppURL "https://example.com"
#endif
#ifndef SourceDir
  #define SourceDir "bin\Release\net8.0-windows10.0.26100.0\win-x64\publish"
#endif
#ifndef OutputDir
  #define OutputDir "artifacts"
#endif
#ifndef Runtime
  #define Runtime "win-x64"
#endif
#ifndef WinArch
  #define WinArch "x64compatible"
#endif
#ifndef SetupIcon
  #define SetupIcon "Assets\AppIcon.ico"
#endif

[Setup]
AppId={{B7E1C2A4-9F3D-4A6B-8E5C-2D7F1A9B0C33}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=FreeScreenRecorder-{#AppVersion}-{#Runtime}-Setup
SetupIconFile={#SetupIcon}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={#WinArch}
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
LicenseFile=LICENSE

#ifdef Inno64BitMode
ArchitecturesInstallIn64BitMode=x64compatible
#endif

#if FileExists(AddBackslash(SourcePath) + "WizardImage.bmp")
WizardImageFile=WizardImage.bmp
#endif
#if FileExists(AddBackslash(SourcePath) + "WizardSmallImage.bmp")
WizardSmallImageFile=WizardSmallImage.bmp
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
