# 2026-11-12 — Files не запускается / «ничего не нажимается»

## Симптом
Запуск FilesDev — окно не появляется / сразу закрывается; «ничего не нажимается».

## Причины (цепочка)

1. **Win10 + WASDK 2.6.5**: `DeploymentManager` auto-init → `REGDB_E_CLASSNOTREG` до Main.
2. **Нет `Files.App.Server` в layout**: `msbuild` без `AppxPackage=true` не копировал OOP-сервер → `DirectoryNotFoundException` при `SettingsService`, процесс жил ~1 с и выходил.
3. (доп.) Онбординг associations мог бы блокировать UI ContentDialog — отложен/защищён.

## Исправления
- `Files.App.csproj`: `WindowsAppSdkDeploymentManagerInitialize=false`
- `Files.App.csproj`: target `CopyFilesAppServerToOutput` после Build
- `Program.cs`: не редиректить activation на мёртвый PID
- Онбординг associations: delay + ранний HasSeen + CloseAllDialogs
- AdvancedPage: Click + Mode=OneWay

## Проверка
После Register + Activate: процесс `Files` + `Files.App.Server`, окно title `Files`, Responding=True.
