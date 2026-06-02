[Setup]
AppName=Room Tile System v3
AppVersion=1.0.0
DefaultDirName=C:\ProgramData\RoomTileSystem
DefaultGroupName=Room Tile System
OutputDir=..\..\dist
OutputBaseFilename=RoomTileSystem_Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
DisableDirPage=yes
DisableProgramGroupPage=yes

[Dirs]
Name: "{app}\Update\Staging"
Name: "{app}\Update\Backup"
Name: "{app}\Update\Logs"
Name: "{app}\Modules"

[Files]
; 複製主程式與依賴 DLL
Source: "..\..\RoomTileSystem.Addin\bin\Release\net48\Obfuscated\RoomTileSystem.Addin.dll"; DestDir: "{app}\App"; Flags: ignoreversion
Source: "..\..\RoomTileSystem.Addin\bin\Release\net48\TileJointSharedParam.txt"; DestDir: "{app}\App"; Flags: ignoreversion
Source: "..\..\RoomTileSystem.Addin\bin\Release\net48\version.json"; DestDir: "{app}\App"; Flags: ignoreversion
; 複製設定檔 (若使用者原本已有則不覆蓋)
Source: "..\..\RoomTileSystem.Addin\bin\Release\net48\appsettings.json"; DestDir: "{app}\Config"; Flags: onlyifdoesntexist
; 複製 Updater
Source: "..\..\RoomTileSystem.Updater\bin\Release\net48\RoomTileSystem.Updater.exe"; DestDir: "{app}\Updater"; Flags: ignoreversion

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  AddinContent: string;
  AddinPath: string;
  RevitVersion: string;
  I: Integer;
  RevitVersions: array[0..2] of string;
begin
  if CurStep = ssPostInstall then
  begin
    RevitVersions[0] := '2024';
    RevitVersions[1] := '2025';
    RevitVersions[2] := '2026';
    
    AddinContent := 
      '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
      '<RevitAddIn>' + #13#10 +
      '  <AddIn Type="Application">' + #13#10 +
      '    <Name>Room Tile System</Name>' + #13#10 +
      '    <Assembly>C:\ProgramData\RoomTileSystem\App\RoomTileSystem.Addin.dll</Assembly>' + #13#10 +
      '    <FullClassName>RoomTileSystem.App</FullClassName>' + #13#10 +
      '    <ClientId>a2b3c4d5-0001-4ebc-9999-tile0000001a</ClientId>' + #13#10 +
      '    <VendorId>MAYOUCHR</VendorId>' + #13#10 +
      '    <VendorDescription>MAYOUCHR, Revit Tile Tool Developer</VendorDescription>' + #13#10 +
      '  </AddIn>' + #13#10 +
      '</RevitAddIn>';

    for I := 0 to 2 do
    begin
      RevitVersion := RevitVersions[I];
      AddinPath := 'C:\ProgramData\Autodesk\Revit\Addins\' + RevitVersion;
      if DirExists(AddinPath) then
      begin
        SaveStringToFile(AddinPath + '\RoomTileSystem.addin', AddinContent, False);
        Log('Registered RoomTileSystem.addin for Revit ' + RevitVersion);
      end;
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
      AddinPath := 'C:\ProgramData\Autodesk\Revit\Addins\' + RevitVersion + '\RoomTileSystem.addin';
      if FileExists(AddinPath) then
      begin
        DeleteFile(AddinPath);
      end;
    end;
  end;
end;
