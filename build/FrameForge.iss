; FrameForge installer (Inno Setup 6). Built by build/publish.ps1, which passes:
;   /DAppVersion=1.2.3  /DSourceDir=<self-contained publish folder>  /DOutputDir=<artifacts>
; See docs/decisions/0019-installer-updates-crash-reports.md.
;
; - Installs for the current user only (no admin prompt) into %LOCALAPPDATA%\Programs\FrameForge.
; - Upgrading: run a newer installer. It closes FrameForge if it is running and replaces the app files.
;   The library (%LOCALAPPDATA%\FrameForge: database, settings, thumbnails, masks, models, logs) is never touched.
; - Uninstalling asks whether to remove the library too; the answer defaults to keeping it. Photos are never touched.

#ifndef AppVersion
  #error AppVersion is required (build/publish.ps1 passes it)
#endif
#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
; Never change this id: it is how Windows recognises an upgrade of the same app.
AppId={{5B0F7C3E-2E4D-4B8A-9C61-7F1D2A6E9B40}
AppName=FrameForge
AppVersion={#AppVersion}
AppVerName=FrameForge {#AppVersion}
AppPublisher=FrameForge
AppCopyright=Copyright (c) FrameForge
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\FrameForge
DefaultGroupName=FrameForge
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\src\FrameForge.App\Assets\FrameForge.ico
UninstallDisplayIcon={app}\FrameForge.exe
UninstallDisplayName=FrameForge
OutputDir={#OutputDir}
OutputBaseFilename=FrameForge-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[InstallDelete]
; An upgrade replaces the app folder's contents, so files dropped from a newer version don't linger.
; This is the app folder only; the library is elsewhere.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\FrameForge"; Filename: "{app}\FrameForge.exe"
Name: "{autodesktop}\FrameForge"; Filename: "{app}\FrameForge.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FrameForge.exe"; Description: "Start FrameForge"; Flags: nowait postinstall skipifsilent

[Code]
// Uninstall: offer to remove the library as well. Default No; never asked in a silent uninstall.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  LibraryDir: String;
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    LibraryDir := ExpandConstant('{localappdata}\FrameForge');
    if DirExists(LibraryDir) then
    begin
      if MsgBox('Also remove your FrameForge library?' + #13#10 + #13#10 +
                'This deletes your decisions, ratings, edits, presets, settings, thumbnails and AI models in:' + #13#10 +
                LibraryDir + #13#10 + #13#10 +
                'Your photos are not touched either way. Choose No to keep the library for a later install.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(LibraryDir, True, True, True);
    end;
  end;
end;
