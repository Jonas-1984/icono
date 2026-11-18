; Inno Setup script of the icono installer (test version).
; Build it with installer\build-installer.ps1 (publishes the app first), or compile it yourself after "dotnet publish ... -o installer\publish".

#define AppName "icono"
#define AppVersion "0.1.0"
#define AppVersionText "0.1.0-test.11"
#define Publisher "Jonas Shojaei (bitbins.de)"
#define PublishDir "publish"
#ifndef Suffix
  #define Suffix ""
#endif

[Setup]
; The AppId identifies the program for updates and uninstall. Never change it.
AppId={{B7E1C3A4-5D2F-4E88-9A61-3F0C7D4E2B19}
AppName={#AppName}
AppVersion={#AppVersionText}
AppVerName={#AppName} {#AppVersionText}
AppPublisher={#Publisher}
AppPublisherURL=https://www.bitbins.de
AppSupportURL=https://www.bitbins.de
AppCopyright=Copyright (c) 2026 Jonas Shojaei
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}.0
VersionInfoCompany={#Publisher}
VersionInfoDescription=icono setup (test version)
VersionInfoProductName={#AppName}

; Per user by default (no administrator rights needed); the user may choose "for all users" in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\icono
DefaultGroupName=icono
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir=Output
OutputBaseFilename=ICONO-Setup-{#AppVersionText}{#Suffix}
SetupIconFile=..\src\Icono.App\Assets\icono.ico
UninstallDisplayIcon={app}\icono.exe
UninstallDisplayName={#AppName} {#AppVersionText}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; icono files (.icn) get the icono file icon and open with icono (a choice in the wizard, per user, nothing else in the system is changed)
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto
UsePreviousLanguage=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "docs\Testbedingungen-DE.txt"; InfoBeforeFile: "docs\Datenschutz-DE.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "docs\Terms-EN.txt"; InfoBeforeFile: "docs\Privacy-EN.txt"

[CustomMessages]
german.DeleteUserData=Sollen auch Ihre lokalen icono-Daten gelöscht werden (Einstellungen, Liste der zuletzt geöffneten Dateien, Icon-Bibliothek, Fehlerprotokoll)?%n%nIhre gespeicherten Zeichnungen bleiben in jedem Fall erhalten.
english.DeleteUserData=Do you also want to delete your local icono data (settings, list of recent files, icon library, error log)?%n%nYour saved drawings are kept in any case.
german.UninstallLink=icono deinstallieren
english.UninstallLink=Uninstall icono
german.AssociateFiles=icono-Dateien (.icn) mit icono öffnen und das icono-Dateisymbol verwenden
english.AssociateFiles=Open icono files (.icn) with icono and show the icono file icon
german.FileAssociations=Dateien:
english.FileAssociations=Files:
german.DocumentType=icono-Zeichnung
english.DocumentType=icono document

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associate"; Description: "{cm:AssociateFiles}"; GroupDescription: "{cm:FileAssociations}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "docs\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "docs\THIRD-PARTY-NOTICES-DOTNET.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "docs\Testbedingungen-DE.txt"; DestDir: "{app}"; DestName: "Testbedingungen.txt"; Flags: ignoreversion
Source: "docs\Terms-EN.txt"; DestDir: "{app}"; DestName: "Terms.txt"; Flags: ignoreversion
Source: "docs\Datenschutz-DE.txt"; DestDir: "{app}"; DestName: "Datenschutz.txt"; Flags: ignoreversion
Source: "docs\Privacy-EN.txt"; DestDir: "{app}"; DestName: "Privacy.txt"; Flags: ignoreversion

[Registry]
; the file type of icono documents (.icn, and the older .icono): its icon and the command that opens it. HKA is the current user for a per user install; uninstall removes it.
Root: HKA; Subkey: "Software\Classes\.icn"; ValueType: string; ValueName: ""; ValueData: "icono.Document"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.icono"; ValueType: string; ValueName: ""; ValueData: "icono.Document"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\icono.Document"; ValueType: string; ValueName: ""; ValueData: "{cm:DocumentType}"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\icono.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icono-file.ico"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\icono.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\icono.exe"" ""%1"""; Tasks: associate

[Icons]
Name: "{autoprograms}\icono"; Filename: "{app}\icono.exe"
Name: "{autoprograms}\{cm:UninstallLink}"; Filename: "{uninstallexe}"
Name: "{autoprograms}\icono Handbuch"; Filename: "{app}\Handbuch-DE.html"; Languages: german
Name: "{autoprograms}\icono User Manual"; Filename: "{app}\Manual-EN.html"; Languages: english
Name: "{autodesktop}\icono"; Filename: "{app}\icono.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\icono.exe"; Description: "{cm:LaunchProgram,icono}"; Flags: nowait postinstall skipifsilent

[Code]
// After the uninstall: offer to delete what icono kept in %LocalAppData%\Icono (nothing leaves the device, but it is the user's data).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\Icono');
    if DirExists(DataDir) and (not UninstallSilent) then
    begin
      if MsgBox(ExpandConstant('{cm:DeleteUserData}'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
