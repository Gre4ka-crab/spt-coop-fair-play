# Coop Fair Play

Приватный модуль SPT/Fika-сборки **PrivateStack Coop 0.1.0**.

Headless admission, серверные правила рейда и ограничения поддерживаемых клиентских настроек.

Роли: backend, human. Точные текущие DLL/версии и hashes фиксируются
в общем release manifest репозитория `spt-coop-operations`.
Версия всей сборки0.1.0 не заменяет версии совместимости отдельных DLL.

## Исходники и совместимость

Сохраняются существующие GUID, assembly names, protocol2, API routes и пути конфигов.
Активные исходники: ClientFairPlay, FairPlayServer.
Остальные исходники сохранены как dev-only/legacy; наличие в Git не разрешает установку в prod.
Проверенные C#-исходники соответствуют прежнему runtime; изменены только build-time HintPath.
Жёсткие runtime root guards пока сохранены: перенос установки требует отдельной миграции.

## Сборка

Windows, .NET SDK10 и своя легитимная SPT4.1.6/EFT40743.
Установить `SPT_GAME_ROOT` или передать `-p:GameRoot=C:/Games/SPT-Coop`.
Собирать только нужный `.csproj` из `src`; target framework каждого проекта сохранён.
Тесты с native DTO требуют локальные DLL и подтверждают только свой scope.
`dotnet build <project.csproj> -c Release -p:GameRoot=<SPT-root>`.
Папки bin/obj, игровые DLL и runtime assets не коммитить.

## Приёмка

Владелец условно принял реализованные функции для первой частной версии.
GUI/реальные2–4 клиента остаются NOT_TESTED; это не утверждение об их прохождении.
Mono C0000005 — известный нерешённый дефект, расследование приостановлено.
Перед deployment: dry-run, остановка owned idle роли, backup, точные hashes,
затем проверка startup/runtime необходимого уровня и согласованный rollback.

## Владение и лицензии

Исходники частного проекта. Существующие LICENSE и upstream notices сохранены.
Proprietary game assemblies, профили, ключи и серверные БД в репозиторий не входят.
Общие исходники описаны в `source-provenance.json`; менять их в репозитории-владельце.
