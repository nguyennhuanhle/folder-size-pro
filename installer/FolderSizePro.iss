; Bộ cài Folder Size Pro (UC-19, KT-23, KT-41, KT-06).
; Biên dịch qua scripts\build-installer.ps1 (publish trước, rồi ISCC). Có thể truyền /DAppVersion=x.y.z.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Folder Size Pro"
#define AppExe "FolderSizePro.exe"
#define PublishDir "..\publish\FolderSizePro"

[Setup]
; AppId cố định: bản mới nhận ra bản cũ để cài đè. Đừng đổi.
AppId={{B7E1D2A4-5C93-4F6B-8A1E-3D9C0F72E5B8}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Folder Size Pro
VersionInfoVersion={#AppVersion}
; KT-06 / KT-23: chỉ cài cho tài khoản hiện tại, không đòi admin, không cài driver hay dịch vụ → {autopf} = %LOCALAPPDATA%\Programs
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\FolderSizePro.App\Assets\app.ico
OutputDir=..\_deliverables
OutputBaseFilename=FolderSizePro-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=force
RestartApplications=no
ChangesEnvironment=yes
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage

[Languages]
Name: "vi"; MessagesFile: "compiler:Default.isl,Vietnamese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
vi.TaskMenu=Thêm “Quét bằng Folder Size Pro” vào menu chuột phải của thư mục và ổ đĩa (Windows 11: nằm trong “Show more options”)
en.TaskMenu=Add “Scan with Folder Size Pro” to the right-click menu of folders and drives (Windows 11: under “Show more options”)
vi.TaskPath=Thêm lệnh fsp vào PATH (dùng trong cửa sổ lệnh)
en.TaskPath=Add the fsp command to PATH (for the command prompt)
vi.TaskDesktop=Tạo lối tắt ngoài desktop
en.TaskDesktop=Create a desktop shortcut
vi.TaskGroup=Tuỳ chọn:
en.TaskGroup=Options:
vi.LaunchNow=Mở Folder Size Pro ngay
en.LaunchNow=Open Folder Size Pro now
vi.MenuScan=Quét bằng Folder Size Pro
en.MenuScan=Scan with Folder Size Pro
vi.DeleteData=Xoá luôn dữ liệu của Folder Size Pro (cài đặt, log, phiên quét dở)?%n%nChọn "Không" để giữ lại — cài lại sau sẽ dùng tiếp được.
en.DeleteData=Also delete Folder Size Pro's data (settings, logs, interrupted-scan session)?%n%nChoose "No" to keep it — a later reinstall will pick it up again.

[Tasks]
Name: "explorermenu"; Description: "{cm:TaskMenu}"; GroupDescription: "{cm:TaskGroup}"
Name: "addtopath"; Description: "{cm:TaskPath}"; GroupDescription: "{cm:TaskGroup}"
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; GroupDescription: "{cm:TaskGroup}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; UC-19: menu chuột phải (HKCU — không cần admin). KT-41: KHÔNG có khoá Run / dịch vụ / Scheduled Task nào.
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderSizePro"; ValueType: string; ValueName: ""; ValueData: "{cm:MenuScan}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderSizePro"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\FolderSizePro\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --scan ""%1"""; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Drive\shell\FolderSizePro"; ValueType: string; ValueName: ""; ValueData: "{cm:MenuScan}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Drive\shell\FolderSizePro"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Drive\shell\FolderSizePro\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --scan ""%1"""; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\FolderSizePro"; ValueType: string; ValueName: ""; ValueData: "{cm:MenuScan}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\FolderSizePro"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\FolderSizePro\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --scan ""%V"""; Tasks: explorermenu
; Mở file .fsp bằng Folder Size Pro
Root: HKCU; Subkey: "Software\Classes\.fsp"; ValueType: string; ValueName: ""; ValueData: "FolderSizePro.Snapshot"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\FolderSizePro.Snapshot"; ValueType: string; ValueName: ""; ValueData: "Folder Size Pro snapshot"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\FolderSizePro.Snapshot\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKCU; Subkey: "Software\Classes\FolderSizePro.Snapshot\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsAddPath(Dir: string): Boolean;
var
  Path: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Path) then
    Result := True
  else
    Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Path) + ';') = 0;
end;

{ Thêm thư mục cài vào PATH của người dùng (không cần admin), không tạo ";;" }
procedure AddToPath(Dir: string);
var
  Path: string;
begin
  if not NeedsAddPath(Dir) then Exit;
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Path) then Path := '';
  if (Length(Path) > 0) and (Path[Length(Path)] <> ';') then Path := Path + ';';
  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Path + Dir);
end;

procedure RemoveFromPath(Dir: string);
var
  Path, Upper, Needle: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Path) then Exit;
  Upper := ';' + Uppercase(Path) + ';';
  Needle := ';' + Uppercase(Dir) + ';';
  P := Pos(Needle, Upper);
  if P = 0 then Exit;
  Path := ';' + Path + ';';
  Delete(Path, P, Length(Dir) + 1);
  if (Length(Path) > 0) and (Path[1] = ';') then Delete(Path, 1, 1);
  if (Length(Path) > 0) and (Path[Length(Path)] = ';') then Delete(Path, Length(Path), 1);
  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Path);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveFromPath(ExpandConstant('{app}'));
    DataDir := ExpandConstant('{localappdata}\FolderSizePro');
    if DirExists(DataDir) then
      if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
