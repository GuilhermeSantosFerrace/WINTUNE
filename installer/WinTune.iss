; Instalador do WinTune (Inno Setup 6). Compile com:
;   ISCC.exe /DAppVersion=1.0.0 installer\WinTune.iss
; Pré-requisito: .\build.ps1 publish (gera .\publish\WinTune.exe).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{0EF28575-3493-406A-A0FC-3CF771F5A8D5}
AppName=WinTune
AppVersion={#AppVersion}
AppPublisher=WinTune
AppVerName=WinTune {#AppVersion}
DefaultDirName={autopf}\WinTune
DefaultGroupName=WinTune
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts
OutputBaseFilename=WinTune-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\WinTune.exe
UninstallDisplayName=WinTune

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "..\publish\WinTune.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\SAFETY.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\TWEAKS.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{group}\WinTune"; Filename: "{app}\WinTune.exe"
Name: "{group}\Recuperação manual (SAFETY.md)"; Filename: "{app}\docs\SAFETY.md"
Name: "{group}\Desinstalar WinTune"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\WinTune.exe"; Description: "Abrir o WinTune"; Flags: nowait postinstall skipifsilent shellexec

[Code]
var
  RevertedOk: Boolean;

{ Antes de remover: oferece "Desfazer tudo" para não deixar ajustes ativos sem avisar. }
function InitializeUninstall(): Boolean;
var
  Answer, Code: Integer;
begin
  Result := True;
  RevertedOk := False;
  Answer := MsgBox('Antes de remover, deseja desfazer todos os ajustes feitos pelo WinTune (recomendado)?' + #13#10#13#10 +
    'Sim: restaura o estado original e depois remove.' + #13#10 +
    'Não: remove o app e MANTÉM os ajustes ativos (o backup fica em %ProgramData%\WinTune para recuperação manual).',
    mbConfirmation, MB_YESNOCANCEL);
  if Answer = IDCANCEL then
  begin
    Result := False;
    exit;
  end;

  if Answer = IDYES then
  begin
    if Exec(ExpandConstant('{app}\WinTune.exe'), '--revert-all', '', SW_SHOW, ewWaitUntilTerminated, Code) and (Code = 0) then
      RevertedOk := True
    else
      Result := MsgBox('Alguns ajustes não puderam ser desfeitos automaticamente. O backup será mantido em %ProgramData%\WinTune ' +
        'e a recuperação manual está em docs\SAFETY.md.' + #13#10#13#10 + 'Continuar a desinstalação?', mbError, MB_YESNO) = IDYES;
  end;
end;

{ Depois de remover: apaga os dados só se tudo foi revertido; dados pessoais (perfis, sessões, logs) só com confirmação. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if RevertedOk then
      DelTree(ExpandConstant('{commonappdata}\WinTune'), True, True, True);

    if MsgBox('Remover também perfis, sessões de benchmark e logs do WinTune deste usuário?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\WinTune'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\WinTune'), True, True, True);
    end;
  end;
end;
