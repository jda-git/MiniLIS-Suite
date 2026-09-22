; ════════════════════════════════════════════════════════════════════════════════════
;  Instalador de MiniLIS Suite (Inno Setup 6)
;
;  No se compila a mano: usar installer\Build-Installer.ps1, que publica el programa y
;  pasa la versión (/DAppVersion) y la carpeta publicada (/DAppSource).
;
;  El asistente solo recoge los datos y muestra resultados. Todo el trabajo (requisitos,
;  certificado, servicio, permisos, cortafuegos, arranque) lo hace
;  scripts\Install-MiniLIS.ps1, que también sirve para instalar sin asistente.
; ════════════════════════════════════════════════════════════════════════════════════

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppSource
  #define AppSource "out\app"
#endif

[Setup]
; No cambiar nunca el AppId: es lo que identifica una instalación existente al actualizar.
AppId={{6E0B8C3A-4F2D-4B7E-9C51-2A7D3E9F1B64}
AppName=MiniLIS Suite
AppVersion={#AppVersion}
AppVerName=MiniLIS Suite {#AppVersion}
AppPublisher=Laboratorio de Citometría
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\MiniLIS
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.14393
OutputDir=out
OutputBaseFilename=MiniLIS-Suite-{#AppVersion}-instalador
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName=MiniLIS Suite
; El servicio lo detiene y arranca el script; el gestor de reinicio de Windows no debe tocarlo.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; Flags: unchecked

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "scripts\Install-MiniLIS.ps1"; DestDir: "{app}\instalacion"; Flags: ignoreversion
; Copia para usarla ANTES de instalar (requisitos, certificado, preparar actualización).
Source: "scripts\Install-MiniLIS.ps1"; Flags: dontcopy
Source: "..\docs\INSTALACION.md"; DestDir: "{app}\instalacion"; Flags: ignoreversion

[INI]
Filename: "{app}\MiniLIS Suite.url"; Section: "InternetShortcut"; Key: "URL"; String: "{code:GetAppUrl}"

[Icons]
Name: "{autoprograms}\MiniLIS Suite"; Filename: "{app}\MiniLIS Suite.url"
Name: "{autodesktop}\MiniLIS Suite"; Filename: "{app}\MiniLIS Suite.url"; Tasks: desktopicon

[Run]
Filename: "{app}\MiniLIS Suite.url"; Description: "Abrir MiniLIS en el navegador"; Flags: postinstall shellexec nowait skipifsilent; Check: InstallSucceeded

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\instalacion\Install-MiniLIS.ps1"" -Action Uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "QuitarServicio"

[UninstallDelete]
Type: files; Name: "{app}\MiniLIS Suite.url"

[Code]
var
  IsUpgrade: Boolean;
  PrevUrl, PrevDataDir, PrevMode, PrevPort, PrevVersion: String;
  ModePage: TInputOptionWizardPage;
  NetPage: TInputQueryWizardPage;
  CertPage: TInputOptionWizardPage;
  PfxPage: TInputFileWizardPage;
  PfxPassword: TPasswordEdit;
  AdminPage: TInputQueryWizardPage;
  DataPage: TInputDirWizardPage;
  CheckPage: TOutputMsgMemoWizardPage;
  ResultPage: TOutputMsgMemoWizardPage;
  CheckHasErrors: Boolean;
  InstallOk: Boolean;
  UninstallDataDir: String;

// ── Utilidades ──────────────────────────────────────────────────────────────────────

function PowerShellExe(): String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function IsServer(): Boolean;
begin
  if IsUpgrade then
    Result := PrevMode <> 'Puesto'
  else
    Result := ModePage.Values[0];
end;

function UsePfx(): Boolean;
begin
  // Opción 0 = certificado del hospital (PFX); opción 1 = autofirmado.
  Result := (not IsUpgrade) and IsServer() and CertPage.Values[0];
end;

function PortValue(): String;
begin
  if IsUpgrade then Result := PrevPort else Result := Trim(NetPage.Values[0]);
end;

function DataDirValue(): String;
begin
  if IsUpgrade then Result := PrevDataDir else Result := DataPage.Values[0];
end;

function HostNamesValue(): String;
begin
  if IsServer() then Result := Trim(NetPage.Values[1]) else Result := 'localhost';
end;

function FirstHost(): String;
var
  s: String;
  p: Integer;
begin
  s := HostNamesValue();
  StringChangeEx(s, ',', ';', True);
  StringChangeEx(s, ' ', ';', True);
  p := Pos(';', s);
  if p > 0 then s := Copy(s, 1, p - 1);
  if s = '' then s := 'localhost';
  Result := Lowercase(s);
end;

function GetAppUrl(Param: String): String;
begin
  if IsUpgrade and (PrevUrl <> '') then
    Result := PrevUrl
  else if PortValue() = '443' then
    Result := 'https://' + FirstHost() + '/'
  else
    Result := 'https://' + FirstHost() + ':' + PortValue() + '/';
end;

function InstallSucceeded(): Boolean;
begin
  Result := InstallOk;
end;

function JsonEscape(s: String): String;
begin
  StringChangeEx(s, '\', '\\', True);
  StringChangeEx(s, '"', '\"', True);
  Result := s;
end;

// Los secretos van en un fichero temporal, no como argumentos (serían visibles en la
// lista de procesos). El script lo borra en cuanto lo lee.
function WriteSecrets(IncludeAdmin: Boolean): String;
var
  lines: TArrayOfString;
  admin, cert: String;
begin
  Result := ExpandConstant('{tmp}\secretos.json');
  admin := '';
  cert := '';
  if IncludeAdmin then admin := AdminPage.Values[1];
  if UsePfx() then cert := PfxPassword.Text;
  SetArrayLength(lines, 1);
  lines[0] := '{"adminPassword":"' + JsonEscape(admin) + '","certPassword":"' + JsonEscape(cert) + '"}';
  SaveStringsToUTF8File(Result, lines, False);
end;

function CommonArgs(): String;
begin
  Result := ' -Port ' + PortValue() +
            ' -DataDir "' + DataDirValue() + '"' +
            ' -InstallDir "' + WizardDirValue() + '"' +
            ' -AppVersion "{#AppVersion}"';
  if IsServer() then
    Result := Result + ' -Mode Servidor -HostNames "' + HostNamesValue() + '"'
  else
    Result := Result + ' -Mode Puesto';
  if not IsUpgrade then
    Result := Result + ' -AdminUser "' + Trim(AdminPage.Values[0]) + '"';
  if UsePfx() then
    Result := Result + ' -CertPfxPath "' + PfxPage.Values[0] + '"';
end;

// Ejecuta una acción del script y devuelve su código de salida; Report recibe el informe
// ya formateado para mostrarlo, y HasErrors si alguna línea es ERROR.
function RunScript(Action, Script, Extra: String; var Report: String; var HasErrors: Boolean): Integer;
var
  reportFile, params, level, check, detail, line: String;
  lines: TArrayOfString;
  i, p: Integer;
begin
  reportFile := ExpandConstant('{tmp}\informe-' + Action + '.txt');
  DeleteFile(reportFile);
  params := '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '" -Action ' + Action +
            ' -ReportFile "' + reportFile + '"' + Extra;
  Log('PowerShell: -Action ' + Action);
  if not Exec(PowerShellExe(), params, '', SW_HIDE, ewWaitUntilTerminated, Result) then
    Result := -1;

  Report := '';
  HasErrors := False;
  if LoadStringsFromFile(reportFile, lines) then
    for i := 0 to GetArrayLength(lines) - 1 do
    begin
      line := lines[i];
      p := Pos('|', line);
      if p = 0 then continue;
      level := Copy(line, 1, p - 1);
      line := Copy(line, p + 1, Length(line));
      p := Pos('|', line);
      check := Copy(line, 1, p - 1);
      detail := Copy(line, p + 1, Length(line));
      if level = 'ERROR' then HasErrors := True;
      if level = 'OK' then level := '  OK   '
      else if level = 'INFO' then level := '  INFO '
      else if level = 'AVISO' then level := '  AVISO'
      else level := '» ERROR';
      Report := Report + level + '  ' + check + ': ' + detail + #13#10;
    end;
  if (Result <> 0) and (Report = '') then
  begin
    Report := '» ERROR  No se pudo ejecutar el script de instalación (código ' + IntToStr(Result) + ').' + #13#10;
    HasErrors := True;
  end;
end;

function TempScript(): String;
begin
  Result := ExpandConstant('{tmp}\Install-MiniLIS.ps1');
end;

function PasswordPolicyError(pw: String): String;
var
  i, upper, lower, digit, symbol, uniq: Integer;
  c: Char;
  seen: String;
begin
  upper := 0; lower := 0; digit := 0; symbol := 0; seen := '';
  for i := 1 to Length(pw) do
  begin
    c := pw[i];
    if (c >= 'A') and (c <= 'Z') then upper := upper + 1
    else if (c >= 'a') and (c <= 'z') then lower := lower + 1
    else if (c >= '0') and (c <= '9') then digit := digit + 1
    else symbol := symbol + 1;
    if Pos(c, seen) = 0 then seen := seen + c;
  end;
  uniq := Length(seen);
  Result := '';
  if Length(pw) < 12 then Result := 'Debe tener al menos 12 caracteres.'
  else if upper = 0 then Result := 'Debe incluir alguna letra mayúscula.'
  else if lower = 0 then Result := 'Debe incluir alguna letra minúscula.'
  else if digit = 0 then Result := 'Debe incluir algún número.'
  else if symbol = 0 then Result := 'Debe incluir algún símbolo (por ejemplo - _ ! @ #).'
  else if uniq < 4 then Result := 'Debe tener al menos 4 caracteres distintos.';
end;

function DefaultHostNames(): String;
var
  name, domain: String;
begin
  name := Lowercase(GetComputerNameString());
  domain := Lowercase(GetEnv('USERDNSDOMAIN'));
  Result := name;
  if domain <> '' then Result := name + '.' + domain + ';' + name;
end;

// ── Arranque del asistente ──────────────────────────────────────────────────────────

function InitializeSetup(): Boolean;
var
  newV, oldV: Int64;
begin
  Result := True;
  IsUpgrade := RegQueryStringValue(HKLM64, 'SOFTWARE\MiniLIS', 'DataDir', PrevDataDir);
  if IsUpgrade then
  begin
    RegQueryStringValue(HKLM64, 'SOFTWARE\MiniLIS', 'Url', PrevUrl);
    RegQueryStringValue(HKLM64, 'SOFTWARE\MiniLIS', 'Mode', PrevMode);
    RegQueryStringValue(HKLM64, 'SOFTWARE\MiniLIS', 'Port', PrevPort);
    RegQueryStringValue(HKLM64, 'SOFTWARE\MiniLIS', 'Version', PrevVersion);
    if PrevPort = '' then PrevPort := '443';
    // Volver a una versión anterior es peligroso: las migraciones de la base de datos no
    // se deshacen. Se permite solo confirmándolo.
    if (PrevVersion <> '') and StrToVersion(PrevVersion, oldV) and StrToVersion('{#AppVersion}', newV) then
      if ComparePackedVersion(newV, oldV) < 0 then
        Result := MsgBox('Está instalada la versión ' + PrevVersion + ', más nueva que esta ({#AppVersion}).' + #13#10#13#10 +
          'Instalar una versión anterior puede dejar la base de datos inservible: sus cambios no se deshacen.' + #13#10 +
          '¿Continuar de todos modos?', mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
  ExtractTemporaryFile('Install-MiniLIS.ps1');
end;

procedure InitializeWizard();
var
  lbl: TNewStaticText;
begin
  ModePage := CreateInputOptionPage(wpSelectDir,
    'Tipo de instalación', '¿Cómo se va a usar MiniLIS en este equipo?',
    'Elija una opción y pulse Siguiente.', True, False);
  ModePage.Add('Servidor: los usuarios entrarán desde otros equipos de la red (https://nombre-del-servidor)');
  ModePage.Add('Puesto único: solo se usará en este equipo (https://localhost)');
  ModePage.Values[0] := True;

  NetPage := CreateInputQueryPage(ModePage.ID,
    'Acceso', 'Puerto y nombres del servidor',
    'MiniLIS funciona siempre por HTTPS. El puerto habitual es 443. Los nombres son los que ' +
    'escribirán los usuarios en el navegador; pida al Servicio de Informática el nombre DNS ' +
    'definitivo (por ejemplo minilis.hospital.local).');
  NetPage.Add('Puerto HTTPS:', False);
  NetPage.Add('Nombres de acceso (separados por punto y coma):', False);
  NetPage.Values[0] := '443';
  NetPage.Values[1] := DefaultHostNames();

  CertPage := CreateInputOptionPage(NetPage.ID,
    'Certificado HTTPS', '¿Qué certificado usará el servidor?',
    'Con un certificado autofirmado, los demás equipos mostrarán un aviso de seguridad al entrar. ' +
    'Para el uso real, use el certificado emitido por el hospital.', True, False);
  CertPage.Add('Usar el certificado del hospital (fichero .pfx)');
  CertPage.Add('Generar un certificado autofirmado (solo para pruebas)');
  CertPage.Values[0] := True;

  PfxPage := CreateInputFilePage(CertPage.ID,
    'Certificado del hospital', 'Fichero .pfx y su contraseña',
    'El certificado debe cubrir los nombres de acceso indicados antes e incluir la clave privada.');
  PfxPage.Add('Fichero del certificado:', 'Certificados (*.pfx;*.p12)|*.pfx;*.p12|Todos los ficheros|*.*', '.pfx');

  lbl := TNewStaticText.Create(PfxPage);
  lbl.Parent := PfxPage.Surface;
  lbl.Caption := 'Contraseña del certificado:';
  lbl.Top := PfxPage.Edits[0].Top + PfxPage.Edits[0].Height + ScaleY(16);
  PfxPassword := TPasswordEdit.Create(PfxPage);
  PfxPassword.Parent := PfxPage.Surface;
  PfxPassword.Top := lbl.Top + lbl.Height + ScaleY(4);
  PfxPassword.Width := PfxPage.Edits[0].Width;

  AdminPage := CreateInputQueryPage(PfxPage.ID,
    'Administrador inicial', 'Primer usuario con acceso a MiniLIS',
    'Al menos 12 caracteres, con mayúsculas, minúsculas, números y algún símbolo. ' +
    'En el primer acceso se pedirá cambiarla.');
  AdminPage.Add('Usuario (correo electrónico):', False);
  AdminPage.Add('Contraseña:', True);
  AdminPage.Add('Repita la contraseña:', True);
  AdminPage.Values[0] := 'admin@minilis.com';

  DataPage := CreateInputDirPage(AdminPage.ID,
    'Carpeta de datos', '¿Dónde se guardarán los datos?',
    'Aquí van la base de datos, las copias de seguridad y la configuración de la instalación. ' +
    'Se mantiene separada del programa: actualizar o desinstalar MiniLIS no la toca. ' +
    'Para datos reales de pacientes debe estar en un volumen cifrado (BitLocker).',
    False, '');
  DataPage.Add('');
  DataPage.Values[0] := ExpandConstant('{commonappdata}\MiniLIS');

  CheckPage := CreateOutputMsgMemoPage(DataPage.ID,
    'Comprobación de requisitos', 'Resultado de la comprobación de este equipo',
    'Los ERROR impiden continuar; los AVISO se pueden resolver después.', '');

  ResultPage := CreateOutputMsgMemoPage(wpInstalling,
    'Resultado de la instalación', '', '', '');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if IsUpgrade then
    Result := (PageID = ModePage.ID) or (PageID = NetPage.ID) or (PageID = CertPage.ID) or
              (PageID = PfxPage.ID) or (PageID = AdminPage.ID) or (PageID = DataPage.ID)
  else if PageID = CertPage.ID then
    Result := not IsServer()
  else if PageID = PfxPage.ID then
    Result := not UsePfx();
end;

procedure CurPageChanged(CurPageID: Integer);
var
  report: String;
begin
  if (not IsUpgrade) and (CurPageID = NetPage.ID) then
  begin
    NetPage.Edits[1].Enabled := IsServer();
    if not IsServer() then NetPage.Values[1] := 'localhost'
    else if NetPage.Values[1] = 'localhost' then NetPage.Values[1] := DefaultHostNames();
  end;

  if CurPageID = CheckPage.ID then
  begin
    CheckPage.RichEditViewer.Lines.Text := 'Comprobando el equipo...';
    WizardForm.NextButton.Enabled := False;
    RunScript('Check', TempScript(), CommonArgs(), report, CheckHasErrors);
    if IsUpgrade then
      report := 'ACTUALIZACIÓN de MiniLIS ' + PrevVersion + ' a {#AppVersion}. Se conserva la configuración actual ' +
                'y se copia la base de datos antes de actualizar.' + #13#10#13#10 + report;
    CheckPage.RichEditViewer.Lines.Text := report;
    WizardForm.NextButton.Enabled := True;
  end;

  if CurPageID = ResultPage.ID then
    WizardForm.BackButton.Visible := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  port: Integer;
  report, err: String;
  hasErr: Boolean;
begin
  Result := True;

  if (not IsUpgrade) and (CurPageID = NetPage.ID) then
  begin
    port := StrToIntDef(Trim(NetPage.Values[0]), 0);
    if (port < 1) or (port > 65535) then
    begin
      MsgBox('El puerto debe ser un número entre 1 y 65535.', mbError, MB_OK);
      Result := False;
    end
    else if IsServer() and (Trim(NetPage.Values[1]) = '') then
    begin
      MsgBox('Indique al menos un nombre de acceso.', mbError, MB_OK);
      Result := False;
    end;
  end;

  if (not IsUpgrade) and (CurPageID = PfxPage.ID) then
  begin
    if not FileExists(PfxPage.Values[0]) then
    begin
      MsgBox('No se encuentra el fichero del certificado.', mbError, MB_OK);
      Result := False;
      exit;
    end;
    RunScript('TestCertificate', TempScript(), CommonArgs() + ' -SecretsFile "' + WriteSecrets(False) + '"', report, hasErr);
    if hasErr then
    begin
      MsgBox('El certificado no se puede usar:' + #13#10#13#10 + report, mbError, MB_OK);
      Result := False;
    end
    else if Pos('AVISO', report) > 0 then
      Result := MsgBox('Revise el certificado:' + #13#10#13#10 + report + #13#10 + '¿Continuar con este certificado?',
                       mbConfirmation, MB_YESNO) = IDYES;
  end;

  if (not IsUpgrade) and (CurPageID = AdminPage.ID) then
  begin
    if Pos('@', AdminPage.Values[0]) < 2 then
    begin
      MsgBox('El usuario debe ser una dirección de correo electrónico.', mbError, MB_OK);
      Result := False;
      exit;
    end;
    err := PasswordPolicyError(AdminPage.Values[1]);
    if err <> '' then
    begin
      MsgBox('La contraseña no cumple la política de seguridad: ' + err, mbError, MB_OK);
      Result := False;
    end
    else if AdminPage.Values[1] <> AdminPage.Values[2] then
    begin
      MsgBox('Las dos contraseñas no coinciden.', mbError, MB_OK);
      Result := False;
    end;
  end;

  if (CurPageID = CheckPage.ID) and CheckHasErrors then
  begin
    MsgBox('Hay requisitos sin cumplir (marcados como ERROR). Corríjalos y vuelva a intentarlo: ' +
           'pulse Atrás y Siguiente para repetir la comprobación.', mbError, MB_OK);
    Result := False;
  end;
end;

// ── Instalación ─────────────────────────────────────────────────────────────────────

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  report: String;
  hasErr: Boolean;
begin
  Result := '';
  if IsUpgrade then
    if RunScript('PrepareUpgrade', TempScript(), CommonArgs(), report, hasErr) <> 0 then
      Result := 'No se pudo preparar la actualización:' + #13#10 + report;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  report, header, script: String;
  hasErr: Boolean;
  code: Integer;
begin
  if CurStep <> ssPostInstall then exit;
  script := ExpandConstant('{app}\instalacion\Install-MiniLIS.ps1');
  WizardForm.StatusLabel.Caption := 'Configurando y arrancando el servicio MiniLIS...';

  if IsUpgrade then
    code := RunScript('CompleteUpgrade', script, CommonArgs(), report, hasErr)
  else
    code := RunScript('Configure', script, CommonArgs() + ' -SecretsFile "' + WriteSecrets(True) + '"', report, hasErr);

  InstallOk := code = 0;
  if InstallOk then
    header := 'MiniLIS está instalado y en marcha.' + #13#10 +
              'Dirección: ' + GetAppUrl('') + #13#10#13#10
  else
    header := 'La instalación NO ha terminado correctamente. Los ficheros del programa se han copiado, ' +
              'pero el servicio no está funcionando. Revise los errores, corríjalos y vuelva a ejecutar ' +
              'el instalador. Registro detallado: ' + DataDirValue() + '\logs\instalacion.log' + #13#10#13#10;

  ResultPage.RichEditViewer.Lines.Text := header + report;
end;

// ── Desinstalación ──────────────────────────────────────────────────────────────────

function InitializeUninstall(): Boolean;
begin
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\MiniLIS', 'DataDir', UninstallDataDir) then
    UninstallDataDir := ExpandConstant('{commonappdata}\MiniLIS');
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    MsgBox('MiniLIS se ha desinstalado.' + #13#10#13#10 +
           'Los datos NO se han borrado (base de datos, copias de seguridad y configuración): ' +
           'son registros con plazo de conservación. Están en:' + #13#10 + UninstallDataDir + #13#10#13#10 +
           'Si se vuelve a instalar indicando esa misma carpeta de datos, se reutilizan.',
           mbInformation, MB_OK);
end;
