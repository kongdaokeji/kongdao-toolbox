// Shared upgrade safeguard. Called before Inno copies any payload files.
function TubaBackupLegacyToolMetadata: String;
var
  SourcePath, BackupDir, BackupPath, BackupBase: String;
  Sequence: Integer;
begin
  Result := '';
  SourcePath := ExpandConstant('{app}\Metadata\tools.json');
  if not FileExists(SourcePath) then
    Exit;
  BackupDir := ExpandConstant('{app}\Data\LegacyToolMetadata');
  BackupBase := BackupDir + '\tools-' + GetDateTimeString('yyyymmdd-hhnnss', '-', ':');
  BackupPath := BackupBase + '.json';
  Sequence := 0;
  while FileExists(BackupPath) do
  begin
    Sequence := Sequence + 1;
    BackupPath := BackupBase + '-' + IntToStr(Sequence) + '.json';
  end;
  if not ForceDirectories(BackupDir) then
  begin
    Result := '无法备份已有工具登记，更新已取消。请检查安装目录的写入权限。';
    Exit;
  end;
  if not FileCopy(SourcePath, BackupPath, True) then
    Result := '无法备份已有工具登记，更新已取消。请关闭占用文件的程序后重试。';
end;
