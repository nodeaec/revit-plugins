; Node.aec Connector - Inno Setup installer script (requires Inno Setup 6).
;
; Compiled automatically by scripts/release.ps1 when ISCC.exe is available:
;   ISCC.exe /DAppVersion=0.1.1 /DAppVersionNum=0.1.1 /DRevitYear=2026
;            /DPayloadStage=<abs path>\release\stage\NodeAec.Connector
;            /O<abs path>\release scripts\installer.iss
;
; Result: NodeAec.Connector-<version>-Setup.exe - double-click installer for
; non-technical users. Detects every installed Revit year, copies the payload
; into %ProgramData%\Autodesk\Revit\Addins\<year>\ and writes the .addin
; manifest with the correct absolute Assembly path per year.
;
; Rerun behavior: re-running Setup detects the previous install.
; Yes = uninstall it and close (run Setup again to install).
; No = upgrade in place. Uninstall is also available in
; Windows Settings > Apps and in {app}\unins000.exe.
;
; NOTE: this file must stay plain ASCII (ISCC reads scripts as ANSI/UTF-8-BOM).

#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif
#ifndef AppVersionNum
  #define AppVersionNum "0.1.1"
#endif
#ifndef RevitYear
  #define RevitYear "2026"
#endif
#ifndef PayloadStage
  #define PayloadStage "..\release\stage\NodeAec.Connector"
#endif

[Setup]
AppId={{CA728E7B-B193-47B0-B501-83A3CDECDD09}}
AppName=Node.aec Connector
AppVersion={#AppVersion}
AppPublisher=Node.aec
AppPublisherURL=https://nodeaec.com.br
AppSupportURL=https://nodeaec.com.br
AppUpdatesURL=https://nodeaec.com.br/products
VersionInfoVersion={#AppVersionNum}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription=Node.aec Connector Revit add-in installer
DefaultDirName={autopf}\Node.aec Connector
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputBaseFilename=NodeAec.Connector-{#AppVersion}-Setup
InfoAfterFile=installer-after.txt

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

; All user-facing custom strings, localized. Code reads them with
; CustomMessage('Name'), which follows the wizard language.
[CustomMessages]
brazilianportuguese.PreviousFound=Uma instalacao anterior do Node.aec Connector foi encontrada.%n%nSim = desinstalar a versao anterior e fechar o instalador.%n(Depois, execute o instalador novamente para instalar.)%nNao = atualizar por cima, sem desinstalar.%nCancelar = sair sem alterar nada.
english.PreviousFound=A previous Node.aec Connector install was found.%n%nYes = uninstall the previous version and close Setup.%n(Then run Setup again to install.)%nNo = upgrade in place without uninstalling.%nCancel = exit without changing anything.
brazilianportuguese.UninstalledDone=A versao anterior foi desinstalada. Execute o instalador novamente para instalar a nova versao.
english.UninstalledDone=The previous version was uninstalled. Run Setup again to install the new version.
brazilianportuguese.RevitMustClose=Feche o Autodesk Revit antes de continuar.%nO instalador precisa substituir os arquivos do add-in, que estao em uso.
english.RevitMustClose=Close Autodesk Revit before continuing.%nSetup needs to replace the add-in files, which are in use.
brazilianportuguese.CopyFailed=Nao foi possivel substituir os arquivos do add-in. Feche o Autodesk Revit e execute o instalador novamente.
english.CopyFailed=Could not replace the add-in files. Close Autodesk Revit and run Setup again.
brazilianportuguese.FinishFailedHeading=A instalacao nao foi concluida
english.FinishFailedHeading=Setup did not finish
brazilianportuguese.FinishFailedText=Alguns arquivos nao puderam ser atualizados (talvez o Revit estivesse aberto).%nFeche o Autodesk Revit e execute o instalador novamente.
english.FinishFailedText=Some files could not be updated (Revit may have been open).%nClose Autodesk Revit and run Setup again.
brazilianportuguese.AfterText=Instalacao concluida!%n%nAbra o Autodesk Revit, clique na aba "Node.aec" e depois em "Minha Conta" para entrar com sua conta e liberar seus plugins.%n%nEm caso de problema, consulte o manual do usuario em NodeAec.Connector/docs/USER_MANUAL.md ou fale com o suporte Node.aec: https://nodeaec.com.br
english.AfterText=Installation finished!%n%nOpen Autodesk Revit, click the "Node.aec" tab and then "My Account" to sign in and unlock your plugins.%n%nIf anything goes wrong, see the user manual in NodeAec.Connector/docs/USER_MANUAL.md or contact Node.aec support: https://nodeaec.com.br

; Payload staged by release.ps1 (plugin DLL, DPAPI dependency, Resources, README).
; The staged .addin is excluded: per-year manifests are generated in [Code].
[Files]
Source: "{#PayloadStage}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "*.addin"

; Post-install notes per language (embedded; ssDone loads the one
; matching the wizard language into the InfoAfter page).
Source: "installer-after.txt"; Flags: dontcopy
Source: "installer-after-en.txt"; Flags: dontcopy

[Code]
const
  ConnectorAddInId = '4B8E1A2C-9F3D-4E5A-8B7C-1D2E3F4A5B6C';
  UninstallKeySingle = '{CA728E7B-B193-47B0-B501-83A3CDECDD09}_is1';
  UninstallKeyLegacy = '{CA728E7B-B193-47B0-B501-83A3CDECDD09}}_is1';
  AddInTemplate =
    '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
    '<RevitAddIns>' + #13#10 +
    '  <AddIn Type="Application">' + #13#10 +
    '    <Name>Node.aec Connector</Name>' + #13#10 +
    '    <Assembly>%s</Assembly>' + #13#10 +
    '    <AddInId>' + ConnectorAddInId + '</AddInId>' + #13#10 +
    '    <FullClassName>NodeAec.Connector.App</FullClassName>' + #13#10 +
    '    <VendorId>NODEAEC</VendorId>' + #13#10 +
    '    <VendorDescription>Node.aec - https://nodeaec.com.br</VendorDescription>' + #13#10 +
    '  </AddIn>' + #13#10 +
    '</RevitAddIns>' + #13#10;

var
  InstallFailed: Boolean;

// Collects installed Revit years by scanning the default install folder.
// Falls back to the compile-time {#RevitYear} when nothing is detected.
procedure GetRevitYears(Years: TStringList);
var
  FindRec: TFindRec;
  Base: string;
begin
  Years.Clear;
  Base := 'C:\Program Files\Autodesk\Revit\';
  if FindFirst(Base + '*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        if DirExists(Base + FindRec.Name) and (Length(FindRec.Name) = 4) then
          if StrToIntDef(FindRec.Name, -1) >= 2015 then
            Years.Add(FindRec.Name);
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
  if Years.Count = 0 then
    Years.Add('{#RevitYear}');
end;

// Recursively copies SrcDir into DstDir. Returns the number of copy failures
// (locked files, e.g. Revit running with the DLL loaded).
function CopyDirTree(const SrcDir, DstDir: string): Integer;
var
  FindRec: TFindRec;
  Src, Dst: string;
begin
  Result := 0;
  ForceDirectories(DstDir);
  if FindFirst(SrcDir + '\*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Src := SrcDir + '\' + FindRec.Name;
        Dst := DstDir + '\' + FindRec.Name;
        if DirExists(Src) then
          Result := Result + CopyDirTree(Src, Dst)
        // The Setup uninstaller lives in {app} too (unins000.*, unins001.*,
        // ...); never propagate it into the Revit add-in folders.
        // FailIfExists=False overwrites the destination, which upgrades
        // and reinstalls require.
        else if CompareText(Copy(FindRec.Name, 1, 5), 'unins') <> 0 then
          if not CopyFile(Src, Dst, False) then
            Result := Result + 1;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

// Deletes stray Setup uninstaller files (unins*.*) from a directory.
// Harmless when none exist.
procedure DeleteUninstallerStrays(const Dir: string);
var
  FindRec: TFindRec;
  Path: string;
begin
  if FindFirst(Dir + '\unins*', FindRec) then
  try
    repeat
      Path := Dir + '\' + FindRec.Name;
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') and not DirExists(Path) then
        DeleteFile(Path);
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

// Writes the per-year .addin manifest with the absolute Assembly path.
function WriteAddInManifest(const AddinsDir: string): Boolean;
var
  AssemblyPath, Xml: string;
begin
  AssemblyPath := AddinsDir + '\NodeAec.Connector\NodeAec.Connector.dll';
  Xml := Format(AddInTemplate, [AssemblyPath]);
  Result := SaveStringToFile(AddinsDir + '\NodeAec.Connector.addin', Xml, False);
end;

// True when a process with the given image name is running (WMI lookup).
// Any lookup failure is treated as "not running" (fail-open for detection;
// the copy step still reports locked files honestly).
function IsProcessRunning(const ProcessName: string): Boolean;
var
  Locator, Service, Procs: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name="' + ProcessName + '"');
    Result := (not VarIsNull(Procs)) and (Procs.Count > 0);
  except
    Result := False;
  end;
end;

// Finds a previous install's uninstall command, checking the canonical key
// and one legacy spelling. Returns True when found.
function GetPreviousUninstallString(var UninstallString: string): Boolean;
begin
  Result := True;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + UninstallKeySingle, 'UninstallString', UninstallString) then Exit;
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + UninstallKeySingle, 'UninstallString', UninstallString) then Exit;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + UninstallKeyLegacy, 'UninstallString', UninstallString) then Exit;
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + UninstallKeyLegacy, 'UninstallString', UninstallString) then Exit;
  Result := False;
end;

// Offers uninstall-first when re-running over a previous install.
// Yes = uninstall the previous version and close (does NOT continue
// installing; run Setup again to install). No = upgrade in place.
// Silent installs skip the prompt and upgrade in place.
// All prompts follow the wizard language via [CustomMessages].
function InitializeSetup(): Boolean;
var
  UninstallString, Clean: string;
  Choice, ExecCode: Integer;
begin
  Result := True;
  InstallFailed := False;
  if WizardSilent() then
    Exit;
  if not GetPreviousUninstallString(UninstallString) then
    Exit;
  Choice := MsgBox(CustomMessage('PreviousFound'), mbConfirmation, MB_YESNOCANCEL);
  if Choice <> IDYES then
  begin
    Result := Choice = IDNO;
    Exit;
  end;
  Clean := RemoveQuotes(Trim(UninstallString));
  Exec(Clean, '/SILENT /SUPPRESSMSGBOXES', '', SW_SHOW, ewWaitUntilTerminated, ExecCode);
  MsgBox(CustomMessage('UninstalledDone'), mbInformation, MB_OK);
  Result := False;
end;

// Clean pre-flight abort (no files touched, no success page): refuse to
// install while Revit holds the add-in DLLs locked.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  Result := '';
  if IsProcessRunning('Revit.exe') then
    Result := CustomMessage('RevitMustClose');
end;

// The InfoAfter "success" page must not show when the copy step failed.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpInfoAfter) and InstallFailed;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Years: TStringList;
  I, Failures: Integer;
Base: string;
begin
  if CurStep = ssPostInstall then
  begin
    Years := TStringList.Create;
    try
      GetRevitYears(Years);
      Failures := 0;
      for I := 0 to Years.Count - 1 do
      begin
        Base := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Years[I]);
        ForceDirectories(Base);
        Failures := Failures + CopyDirTree(ExpandConstant('{app}'), Base + '\NodeAec.Connector');
        if not WriteAddInManifest(Base) then
          Failures := Failures + 1;
        // Repair: older installers copied the Setup uninstaller
        // (unins*.*) into the Revit folders; remove those strays.
        DeleteUninstallerStrays(Base + '\NodeAec.Connector');
      end;
      // NOTE: no RaiseException here on purpose. A raised exception inside
      // ssPostInstall does not roll back and Setup still reaches ssDone,
      // which would show the InfoAfter success page. Flag the failure
      // instead: the success page is skipped and the Finished page reports it.
      if Failures > 0 then
      begin
        InstallFailed := True;
        MsgBox(CustomMessage('CopyFailed'), mbError, MB_OK);
      end;
    finally
      Years.Free;
    end;
  end;
  if (CurStep = ssDone) and InstallFailed then
  begin
    WizardForm.FinishedHeadingLabel.Caption := CustomMessage('FinishFailedHeading');
    WizardForm.FinishedLabel.Caption := CustomMessage('FinishFailedText');
  end;
end;

// Runs when the InfoAfter page is shown (after any internal file load),
// so the localized note cannot be clobbered.
procedure CurPageChanged(CurPageID: Integer);
var
  AfterFile, AfterText: string;
  Note: AnsiString;
begin
  if (CurPageID <> wpInfoAfter) or InstallFailed then
    Exit;
  // InfoAfterFile is a single static file; load the note matching the
  // wizard language (CustomMessage AfterText stays as fallback).
  if CompareText(ActiveLanguage(), 'english') = 0 then
    AfterFile := 'installer-after-en.txt'
  else
    AfterFile := 'installer-after.txt';
  ExtractTemporaryFile(AfterFile);
  if LoadStringFromFile(ExpandConstant('{tmp}\' + AfterFile), Note) then
    WizardForm.InfoAfterMemo.Text := Note
  else
  begin
    AfterText := CustomMessage('AfterText');
    StringChange(AfterText, '%n', #13#10);
    WizardForm.InfoAfterMemo.Text := AfterText;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Years: TStringList;
  I: Integer;
  Base: string;
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  Years := TStringList.Create;
  try
    GetRevitYears(Years);
    for I := 0 to Years.Count - 1 do
    begin
      Base := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Years[I]);
      if DirExists(Base + '\NodeAec.Connector') then
        DelTree(Base + '\NodeAec.Connector', True, True, True);
      DeleteFile(Base + '\NodeAec.Connector.addin');
    end;
  finally
    Years.Free;
  end;
end;
