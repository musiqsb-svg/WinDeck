; WinDeck 설치 파일 (Inno Setup 6). release.ps1 이 /DAppVersion=x.y.z 를 넘겨 컴파일한다.
; 관리자 권한 없이 사용자별로 설치한다: %LOCALAPPDATA%\Programs\WinDeck

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "WinDeck"
#define AppExe "WinDeck.exe"

[Setup]
AppId={{8F3C2A71-5B0E-4D7C-9A61-2E4B7C9D1F05}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=musiqsb-svg
AppPublisherURL=https://github.com/musiqsb-svg/WinDeck
AppSupportURL=https://github.com/musiqsb-svg/WinDeck
AppUpdatesURL=https://github.com/musiqsb-svg/WinDeck/releases/latest
VersionInfoVersion={#AppVersion}
VersionInfoDescription=WinDeck 설치
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
OutputDir=..\release
OutputBaseFilename=WinDeck-Setup-{#AppVersion}
SetupIconFile=..\build\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
CloseApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로가기 만들기"; GroupDescription: "추가 작업:"
Name: "autostart"; Description: "Windows 시작 시 자동 실행"; GroupDescription: "추가 작업:"

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\deploy\*.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
; 팀 기본 버튼 세트 (deploy 폴더에 있을 때만 포함, 첫 실행 때 한 번 사용)
Source: "..\deploy\WinDeck.defaults.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\WinDeck 사용 안내"; Filename: "{app}\WinDeck 사용 안내.txt"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; 앱의 자동 실행 설정(AutoStart.cs)과 같은 값 형식을 쓴다.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WinDeck"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "WinDeck 실행"; Flags: nowait postinstall skipifsilent

[Code]
procedure CloseWinDeck;
var
  ResultCode: Integer;
begin
  { 실행 중이면 파일을 덮어쓸 수 없으므로 먼저 종료한다. 버튼 설정은 바뀔 때마다 저장되어 있다. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode = 0 then Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseWinDeck;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseWinDeck;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  { 앱 설정에서 켠 자동 실행도 함께 정리한다. }
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WinDeck');
  SettingsDir := ExpandConstant('{userappdata}\WinDeck');
  if DirExists(SettingsDir) and not UninstallSilent then
    if MsgBox('버튼 설정도 함께 삭제할까요?' + #13#10#13#10 +
              '[아니요]를 누르면 나중에 다시 설치했을 때 지금 버튼을 그대로 쓸 수 있습니다.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(SettingsDir, True, True, True);
end;
