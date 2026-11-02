#include "..\..\DevelopmentTools.Addin\obj\Release\module-platform\owned-dlls.iss"

#ifndef MyAppVersion
  #define MyAppVersion "3.2.7"
#endif

[Setup]
AppName=Room Tile System v3
AppVersion={#MyAppVersion}
; 公司防毒會攔截系統碟（C:）使用者目錄中的下載與執行檔，預設安裝到第一個可寫入的非系統固定磁碟，
; 例如 D:\DevelopmentTools；找不到時才退回 {localappdata}\DevelopmentTools。
DefaultDirName={code:GetDefaultInstallRoot}
UsePreviousAppDir=no
DefaultGroupName=Room Tile System
OutputDir=..\..\dist
OutputBaseFilename=DevelopmentTools_Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes

[Dirs]
Name: "{app}\Update\Staging"
Name: "{app}\Update\Backup"
Name: "{app}\Update\Logs"
Name: "{app}\Update\Extract"
Name: "{app}\Update\UpdaterStaging"
Name: "{app}\Modules"

[Files]
; 複製主程式與依賴 DLL
#include "..\..\DevelopmentTools.Addin\obj\Release\module-platform\owned-files.iss"
Source: "..\..\DevelopmentTools.Addin\TileJointSharedParam.txt"; DestDir: "{app}\App"; Flags: ignoreversion
Source: "..\..\DevelopmentTools.Addin\bin\Release\net48\version.json"; DestDir: "{app}\App"; Flags: ignoreversion
Source: "..\..\DevelopmentTools.Addin\platform_config.json"; DestDir: "{app}\App"; Flags: ignoreversion
Source: "..\..\DevelopmentTools.Addin\bin\Release\net48\*.dll"; DestDir: "{app}\App"; Excludes: "{#OwnedDllExcludes}"; Flags: ignoreversion
; 複製設定檔 (若使用者原本已有則不覆蓋)
Source: "..\..\DevelopmentTools.Addin\bin\Release\net48\appsettings.json"; DestDir: "{app}\Config"; Flags: onlyifdoesntexist
; 複製 Resources 資料夾
Source: "..\..\DevelopmentTools.Addin\Resources\*"; DestDir: "{app}\App\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\DevelopmentTools.Addin\Rules\*"; DestDir: "{app}\App\Rules"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\DevelopmentTools.Addin\bin\Release\net48\Runtime\*"; DestDir: "{app}\App\Runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\DevelopmentTools.Addin\bin\Release\net48\Agents\*"; DestDir: "{app}\App\Agents"; Flags: ignoreversion recursesubdirs createallsubdirs
; 複製 Updater 與其所需的依賴 DLL
Source: "..\..\DevelopmentTools.Updater\bin\Release\net48\DevelopmentTools.Updater.exe"; DestDir: "{app}\Updater"; Flags: ignoreversion
Source: "..\..\DevelopmentTools.Updater\bin\Release\net48\*.dll"; DestDir: "{app}\Updater"; Flags: ignoreversion
Source: "..\..\DevelopmentTools.Updater\bin\Release\net48\DevelopmentTools.Updater.exe.config"; DestDir: "{app}\Updater"; Flags: ignoreversion
Source: "..\..\dist\mcp-runtime\*"; DestDir: "{app}\App\MCP"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\setup-mcp-clients.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Run]
; 安裝程式不再自動在背景執行 PowerShell。未簽章安裝檔以隱藏視窗啟動
; `powershell -ExecutionPolicy Bypass` 並改寫其他程式（Claude/Codex/Antigravity）的設定檔，
; 是防毒與 EDR 的典型攔截特徵。MCP 客戶端設定改為完成頁的選用勾選項目，預設不勾選。
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup-mcp-clients.ps1"" -InstallRoot ""{app}"""; Description: "設定 AI 客戶端 MCP（選用，需要 Node.js 22 以上）"; Flags: postinstall unchecked skipifsilent runhidden waituntilterminated

[Code]
const
  DRIVE_FIXED = 3;

function GetDriveType(lpRootPathName: string): Cardinal;
  external 'GetDriveTypeW@kernel32.dll stdcall';

function IsWritableRoot(Dir: string): Boolean;
var
  Probe: string;
begin
  Result := False;
  if not ForceDirectories(Dir) then Exit;
  Probe := AddBackslash(Dir) + '.write_probe';
  if SaveStringToFile(Probe, 'ok', False) then
  begin
    DeleteFile(Probe);
    Result := True;
  end;
end;

function GetDefaultInstallRoot(Param: string): string;
var
  I: Integer;
  Root: string;
  SystemLetter: string;
  RootOverride: string;
begin
  RootOverride := GetEnv('DEVELOPMENTTOOLS_ROOT');
  if (RootOverride <> '') and IsWritableRoot(RootOverride) then
  begin
    Result := RootOverride;
    Exit;
  end;

  SystemLetter := Uppercase(Copy(ExpandConstant('{sys}'), 1, 1));
  for I := Ord('D') to Ord('Z') do
  begin
    if Chr(I) <> SystemLetter then
    begin
      Root := Chr(I) + ':\';
      if (GetDriveType(Root) = DRIVE_FIXED) and DirExists(Root) then
      begin
        if IsWritableRoot(Root + 'DevelopmentTools') then
        begin
          Result := Root + 'DevelopmentTools';
          Exit;
        end;
      end;
    end;
  end;

  Result := ExpandConstant('{localappdata}') + '\DevelopmentTools';
end;

procedure CopyLegacyConfig();
var
  LegacyConfig: string;
  TargetConfig: string;
begin
  // 從舊的 C: 安裝位置帶入使用者設定（例如自訂 ManifestUrl），避免改裝到 D: 後遺失。
  LegacyConfig := ExpandConstant('{localappdata}') + '\DevelopmentTools\Config\appsettings.json';
  TargetConfig := ExpandConstant('{app}') + '\Config\appsettings.json';
  if FileExists(LegacyConfig) and (not FileExists(TargetConfig))
     and (CompareText(LegacyConfig, TargetConfig) <> 0) then
  begin
    ForceDirectories(ExtractFileDir(TargetConfig));
    FileCopy(LegacyConfig, TargetConfig, True);
  end;
end;

procedure CleanupLegacyBeaiManifests(RootPath: string);
var
  FindRec: TFindRec;
  ManifestPath: string;
  ManifestContent: AnsiString;
begin
  if not DirExists(RootPath) then
    Exit;

  if FindFirst(RootPath + '\*.addin', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          ManifestPath := RootPath + '\' + FindRec.Name;
          if LoadStringFromFile(ManifestPath, ManifestContent) and
             ((Pos('DevelopmentTools.Addin.App', ManifestContent) > 0) or
              (Pos('BEAI Runtime (DevelopmentTools)', ManifestContent) > 0) or
              (CompareText(FindRec.Name, 'BEAI.QueryHost.addin') = 0)) then
          begin
            if DeleteFile(ManifestPath) then
              Log('Removed stale BEAI Runtime registration: ' + ManifestPath)
            else
            begin
              // 常見於舊版由系統管理員安裝到 ProgramData 的註冊檔：一般權限刪不掉，
              // Revit 啟動時就會出現「無法初始化增益集 ... 找不到類別 DevelopmentTools.Addin.App」。
              Log('WARNING: Unable to remove stale BEAI Runtime registration: ' + ManifestPath);
              SuppressibleMsgBox('發現舊版 BEAI 註冊檔，但沒有權限刪除：' + #13#10 + ManifestPath + #13#10 + #13#10 +
                '這個檔案會讓 Revit 顯示「無法初始化增益集」。請請 IT 或系統管理員刪除它，然後重新開啟 Revit。',
                mbError, MB_OK, IDOK);
            end;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AddinContent: string;
  AddinPath: string;
  OldAddinPath: string;
  OldUpdaterPath: string;
  RevitVersion: string;
  I: Integer;
  RevitVersions: array[0..2] of string;
begin
  if CurStep = ssInstall then
  begin
    CopyLegacyConfig();
  end;

  if CurStep = ssPostInstall then
  begin
    RevitVersions[0] := '2024';
    RevitVersions[1] := '2025';
    RevitVersions[2] := '2026';

    // 1.1. 清理舊版殘留的 Updater
    OldUpdaterPath := 'C:\ProgramData\DevelopmentTools\Updater\RoomTileSystem.Updater.exe';
    if FileExists(OldUpdaterPath) then
    begin
      DeleteFile(OldUpdaterPath);
    end;
    
    // 1. 清理舊版殘留的 RoomTileSystem.addin
    for I := 0 to 2 do
    begin
      RevitVersion := RevitVersions[I];

      CleanupLegacyBeaiManifests(ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\' + RevitVersion);
      CleanupLegacyBeaiManifests(ExpandConstant('{commonappdata}') + '\Autodesk\Revit\Addins\' + RevitVersion);
      
      // 清理 ProgramData 殘留
      OldAddinPath := 'C:\ProgramData\Autodesk\Revit\Addins\' + RevitVersion + '\RoomTileSystem.addin';
      if FileExists(OldAddinPath) then
      begin
        DeleteFile(OldAddinPath);
      end;
      
      // 清理 ProgramData 的舊版 DevelopmentTools.addin 殘留，避免重複載入
      OldAddinPath := 'C:\ProgramData\Autodesk\Revit\Addins\' + RevitVersion + '\DevelopmentTools.addin';
      if FileExists(OldAddinPath) then
      begin
        DeleteFile(OldAddinPath);
      end;
      
      // 清理 AppData Roaming 殘留
      OldAddinPath := ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\' + RevitVersion + '\RoomTileSystem.addin';
      if FileExists(OldAddinPath) then
      begin
        DeleteFile(OldAddinPath);
      end;

      // BEAI is embedded in Development Tools; remove its standalone registration.
      OldAddinPath := ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\' + RevitVersion + '\BEAI.QueryHost.addin';
      if FileExists(OldAddinPath) then
      begin
        DeleteFile(OldAddinPath);
      end;
    end;

    // 2. 註冊新版 DevelopmentTools.addin
    AddinContent := 
      '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
      '<RevitAddIns>' + #13#10 +
      '  <AddIn Type="Application">' + #13#10 +
      '    <Name>Development Tools</Name>' + #13#10 +
      '    <Assembly>' + ExpandConstant('{app}') + '\App\DevelopmentTools.Addin.dll</Assembly>' + #13#10 +
      '    <FullClassName>DevelopmentTools.App</FullClassName>' + #13#10 +
      '    <ClientId>c2d5d85c-4d33-4f9e-a89e-21ef1ea3b361</ClientId>' + #13#10 +
      '    <VendorId>RoomTileSystem</VendorId>' + #13#10 +
      '    <VendorDescription>Room Tile System v3</VendorDescription>' + #13#10 +
      '  </AddIn>' + #13#10 +
      '</RevitAddIns>';

    // 外掛以 Revit 2024 API（.NET Framework 4.8）建置；Revit 2025 以後改用 .NET 8，無法載入，
    // 只註冊 2024，並移除舊版安裝程式留在 2025/2026 的註冊檔。
    for I := 1 to 2 do
    begin
      AddinPath := ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\' + RevitVersions[I] + '\DevelopmentTools.addin';
      if FileExists(AddinPath) then
      begin
        DeleteFile(AddinPath);
        Log('Removed unsupported registration for Revit ' + RevitVersions[I]);
      end;
    end;

    // 使用者的 Revit 2024 Addins 資料夾不一定存在（例如 Revit 尚未以此帳號啟動過）。
    // 以前資料夾不存在時會「安靜地」略過註冊，安裝成功但 Revit 看不到外掛。
    AddinPath := ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\' + RevitVersions[0];
    if not DirExists(AddinPath) then
      ForceDirectories(AddinPath);
    if SaveStringToFile(AddinPath + '\DevelopmentTools.addin', AddinContent, False) then
      Log('Registered DevelopmentTools.addin for Revit ' + RevitVersions[0])
    else
    begin
      Log('ERROR: Unable to register DevelopmentTools.addin in ' + AddinPath);
      SuppressibleMsgBox('無法寫入 Revit 2024 外掛註冊檔：' + #13#10 + AddinPath + '\DevelopmentTools.addin' + #13#10 +
        '外掛已複製，但 Revit 不會載入。請確認防毒或公司政策沒有封鎖此資料夾，再重新執行安裝程式。',
        mbError, MB_OK, IDOK);
    end;
  end;
end;

procedure CurUninstallStepChanged(JustAfter: TUninstallStep);
var
  RevitVersions: array[0..2] of string;
  RevitVersion: string;
  AddinPath: string;
  I: Integer;
begin
  if JustAfter = usPostUninstall then
  begin
    RevitVersions[0] := '2024';
    RevitVersions[1] := '2025';
    RevitVersions[2] := '2026';
    
    for I := 0 to 2 do
    begin
      RevitVersion := RevitVersions[I];
      AddinPath := ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\' + RevitVersion + '\DevelopmentTools.addin';
      if FileExists(AddinPath) then
      begin
        DeleteFile(AddinPath);
      end;
    end;
  end;
end;
