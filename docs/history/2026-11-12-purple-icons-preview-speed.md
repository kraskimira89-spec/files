# 2026-11-12 — Фиолетовые Dev-иконки + ускорение превью + apply associations

## Сделано
- Dev AppTiles / Logo.ico в фиолетовой палитре (HQ resize, targetsize без рамки)
- InfoPane show-first: контрол до завершения LoadAsync
- ImagePreview: thumbnail сразу, полный decode с DecodePixelWidth=2048
- Geocode адреса с Details — не блокирует возврат свойств
- Диалог associations: показ сразу + WhenAll + session cache
- IsDefaultAssociationApplyEnabled=true; apply только после Primary в preview

## Проверка
- msbuild Debug x64 OK после unregister/rename lockbak
- Files + Files.App.Server Responding=True
