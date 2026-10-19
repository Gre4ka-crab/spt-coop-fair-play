"""Bounded evidence, own server artifact and backed-up project records. No runtime deployment."""
import hashlib,json,shutil,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'artifacts/client-fairplay-20261017'
sys.path.insert(0,str(ROOT/'tools'))
sys.path.insert(0,str(Path(__file__).resolve().parent))
from stacklib import digest,verify_file,scan_secrets
from package import build

def main():
    records=[]
    def run(label,command):
        result=subprocess.run(command,cwd=ROOT,capture_output=True,encoding='utf-8',errors='replace',timeout=90)
        (OUT/(label+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8')
        records.append(dict(label=label,command=command,environment='Windows native',exit_code=result.returncode))
        if result.returncode:raise RuntimeError(label+' failed; evidence retained')
    run('framework-final',['pwsh','-NoProfile','-File','tests/Run-ClientFairPlayFrameworkTests.ps1','-Output',str(OUT/'framework-supported-final')])
    run('policy-final',[str(ROOT/'.local/dotnet-sdk-10.0.301/dotnet.exe'),'run','--project','tests/FairPlayServer.Tests','-c','Release','--no-build','--no-restore'])
    run('native-hooks-final',[str(ROOT/'.local/dotnet-sdk-10.0.301/dotnet.exe'),'run','--project','tests/FairPlayNative.Tests','-c','Release','--no-build','--no-restore'])
    pin=json.loads((ROOT/'src/FairPlayServer/client-pin.json').read_text())
    run('crc32c-final',['dotnet','src/ClientFairPlayAudit/bin/Release/net8.0/ClientFairPlayAudit.dll','crc32c','C:/Games/SPT-Coop/BepInEx/plugins/Fika/Fika.Core.dll','src/ClientFairPlay/bin/Release/netstandard2.1/ClientFairPlay.dll',str(pin['crc32c'])])
    delivery=build(True)
    install=json.loads((OUT/'deployment-test-r2/RESULT.json').read_text())
    assert install['status']=='PASS_REAL_NATIVE_FILES_INSTALL_VERIFY_ROLLBACK_NOT_GAMEPLAY'
    assert install['original_files_restored']==145
    server=ROOT/'src/FairPlayServer/bin/Release/net10.0/FairPlayServer.dll'
    server_dir=OUT/('backend-addon-'+digest(server)[:16]);server_dir.mkdir(exist_ok=True)
    for name,source in {'FairPlayServer.dll':server,'LICENSE.txt':ROOT/'src/ClientFairPlay/LICENSE.txt','client-pin.json':ROOT/'src/FairPlayServer/client-pin.json'}.items():
        target=server_dir/name
        if target.exists():assert target.read_bytes()==source.read_bytes()
        else:shutil.copyfile(source,target)
    (server_dir/'README_RU.md').write_text('''# Fair Play Server 0.1.0 — QA, не развёрнут

SPT 4.1.6 / Fika Server 2.4.2, только закреплённые бинарники.
Сначала LAB с отдельными mutable данными и backup, затем human/co-op приёмка.
При остановленном backend поместить только FairPlayServer.dll и LICENSE.txt в
SPT_Runtime/user/mods/PrivateStackFairPlay/. client-pin.json встроен в DLL и дан
здесь для аудита. В human-client и headless plugin directories серверную DLL
не помещать. Остальные DLL остаются в штатных местах Fika/SPT, не копировать их.

Установка здесь не выполнена. На production DLL не переносить до live acceptance.
Native .NET fixtures не доказывают DI startup, HTTP, websocket и рейд.
Для отката после теста: остановить только отслеживаемый LAB backend, вернуть
предыдущую managed DLL из backup либо убрать только впервые установленный own
file, затем запустить штатно. Моды/профили/конфиги рекурсивно не удалять.

Связанный human QA пакет и полный smoke checklist: docs/CLIENT_FAIRPLAY_RU.md.
''',encoding='utf-8')
    source_files=[*sorted((ROOT/'src/ClientFairPlay').glob('*.py')),*sorted((ROOT/'src/ClientFairPlay').glob('*.cs')),
                  ROOT/'src/ClientFairPlay/ClientFairPlay.csproj',ROOT/'src/ClientFairPlay/policy.json',
                  *sorted((ROOT/'src/FairPlayServer').glob('*.cs')),ROOT/'src/FairPlayServer/FairPlayServer.csproj',ROOT/'src/FairPlayServer/client-pin.json',
                  ROOT/'src/ClientFairPlay/install_addon.py',ROOT/'src/ClientFairPlay/Install-Addon.ps1']
    sources={}
    for source in set(source_files):
        relative=source.relative_to(ROOT);dest=OUT/'source'/relative;dest.parent.mkdir(parents=True,exist_ok=True)
        if dest.exists():assert dest.read_bytes()==source.read_bytes()
        else:shutil.copyfile(source,dest)
        sources[relative.as_posix()]=dict(sha256=digest(source),bytes=source.stat().st_size)
    policy=json.loads((ROOT/'src/ClientFairPlay/policy.json').read_text())
    mapping=[]
    config_paths={'com.mpstark.dynamicmaps':'com.mpstark.dynamicmaps.cfg','com.fika.core':'com.fika.core.cfg','com.tyfon.uifixes':'com.tyfon.uifixes.cfg','com.zgfuedkx.allquestscheckmarks':'com.zgfuedkx.allquestscheckmarks.cfg'}
    for guid,rows in policy['settings'].items():
        for row in rows:
            mapping.append(dict(owner='private.spt.clientfairplay',guid=guid,release={'com.mpstark.dynamicmaps':'1.2.1','com.fika.core':'2.4.5','com.tyfon.uifixes':'6.0.4','com.zgfuedkx.allquestscheckmarks':'1.4.0'}[guid],dll_sha256=policy['plugin_pins'][guid],file='BepInEx/config/'+config_paths[guid],section=row['section'],key=row['key'],unit='native ConfigEntry type / TOML serialization',old_value='Target cfg is untrusted; not sampled on gaming PC',new_value=row['value'] if row['value'] is not None else {'rule':'ConfigEntryBase.DefaultValue from pinned DLL','concrete_value_status':'PENDING_HUMAN_RUNTIME_MAPPING'},source='embedded policy.json and pinned upstream Settings/FikaConfig or previously frozen UI cfg',status='SOURCE_RESOLVED_RUNTIME_MAPPING_PENDING'))
    result=dict(status='QA_BUILT_SCOPED_FIXTURES_PASS_RUNTIME_BLOCKED',delivery=delivery,server_artifact=str(server_dir.relative_to(ROOT)),server_sha256=digest(server),client_pin=pin,
                verification=records,settings_lock_checks=9,server_policy_checks=28,native_server_hook_checks=4,native_crc32c='PASS',deployment=install,
                earlier_failures=['Old client Harmony is unsupported under .NET8: AccessTools initializer failed; .NET Framework fixture passes; this is not Unity Mono proof',
                    'Initial installer test parser did not handle WhatIf prose; installer exit0/file safety unaffected, corrected harness r2 passes',
                    'CRC probe in .NET Framework cannot load netstandard2.1 Fika; separate .NET8 CRC probe passes'],
                blockers=['Unity human-client load/GUI/settings lock NOT_RUN','Real backend DI/HTTP/headless startup with these new DLLs NOT_RUN','Real headless raid/PMC/scav/day-night/transit/2-4 coop NOT_RUN','Per-client concrete default mapping PENDING','Map Player:x/y/z text remains visible','Arbitrary modified clients can forge mod reports; no protected attestation'],
                production_changed=False,stable_current_changed=False,source=sources)
    for name,data in [('RESULT.json',result),('mapping.candidate.json',dict(status='QA_MAPPING_SOURCE_RESOLVED_NOT_DEPLOYED',fields=mapping))]:
        content=json.dumps(data,ensure_ascii=False,indent=2)+'\n'
        assert not scan_secrets(content)
        (OUT/name).write_text(content,encoding='utf-8')
    docs=['docs/STATUS.md','docs/DECISIONS.md','docs/TEST_RESULTS.md','docs/06_SOURCES.md']
    if '--write-docs' in sys.argv:
        backup=ROOT/'backups/client-fairplay-docs-20261017';backup.mkdir(exist_ok=False)
        for name in docs:
            target=backup/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(ROOT/name,target);verify_file(target,digest(ROOT/name))
        (ROOT/'docs/CLIENT_FAIRPLAY_RU.md').write_bytes((ROOT/'src/ClientFairPlay/README_RU.md').read_bytes())
        def prepend(name,text):
            path=ROOT/name;first,rest=path.read_text(encoding='utf-8-sig').split('\n',1);path.write_text(first+'\n\n'+text+'\n'+rest,encoding='utf-8')
        prepend('docs/STATUS.md',f'''## Fair Play — 17 октября 2026, QA-кандидат

По запросу владельца собраны human ClientFairPlay0.1.0 и backend FairPlayServer0.1.0.
Этап: отдельный QA adapter, не production acceptance. DLL человеческого клиента
запрещает штатный локальный хост и скрывает/фиксирует302поля4модов;
backend проверяет configured+connected headless identity и закрепляет raid DTO.
Точная native Fika CRC32C проверка human-only, без глобального выключения validation.
Пакет `{delivery['archive']}`, SHA256 `{delivery['sha256']}`;
backend отдельно: `{server_dir.relative_to(ROOT)}`. В клиентском ZIP только своя DLL,
2managedfiles и10packagefiles,139точных prerequisites; чужих DLL/игры/профилей нет.

Проверено:9 actual BepInEx/Harmony .NET Framework cases;28 nativeDTO/mod policy;
4actual Fika backend Harmony denial/config hooks; actual Fika CRC32C equality;
9installer commands на native-filefixture,145старыхфайлов сохранены.
Ограничения: это не Unity/GUI/DI/HTTP/рейд/co-op PASS. Default mapping ожидает
конкретных ConfigEntry на игровомПК. Player:x/y/z текст карты не скрыт;
полностью серверная player симуляция и защищённая аттестация не реализованы.
Произвольную подмену клиента такой аддон не предотвращает.

Production/LAB не мутировали и не перезапускали, stable/current не переключён.
Следующий исполнимый шаг: read-only review docs/CLIENT_FAIRPLAY_RU.md →
изолированныйLAB serverstartup+настоящий human smoke; лишь затем stable.
Полный RESULT/исходники/mapping/ранние failures: artifacts/client-fairplay-20261017/.
''')
        with (ROOT/'docs/DECISIONS.md').open('a',encoding='utf-8') as stream:stream.write('''
- 2026-10-17: запрос блокировки читерских клиентских функций реализован отдельным
  Fair Play QA-кандидатом; не интегрирован в production baseline. Backend обеспечивает
  headless-only create и server-owned raid settings, human DLL скрывает/фиксирует
  известные настройки. Role-specific native CRC supplement сохраняет прочую Fika
  validation и не требует human DLL на headless. Client report не attestation,
  player simulation остаётся архитектурой Fika. Координатный текст карты и real
  Unity/DI/HTTP/co-op acceptance остаются незакрытыми. Concrete default mapping
  PENDING; stable export до этого не разрешён. См. artifacts/client-fairplay-20261017.
''')
        prepend('docs/TEST_RESULTS.md','''## Fair Play QA — 17 октября 2026

Среда Windows native; SDK9.0.319(client netstandard2.1)/10.0.301(backend net10),
PS7.6.6/Python3.13.7; SPT4.1.6/FikaCore2.4.5/Server2.4.2 exact hashes в RESULT.
Обе DLL build exit0 без warnings/errors; framework runner exit0/9cases,
policy exit0/28cases, actual backend hooks exit0/4cases, actual CRC probe exit0.
Команды, версии, SHA и логи: artifacts/client-fairplay-20261017/RESULT.json,
{framework,policy,native-hooks,crc32c}-final.log. Framework test компилируется
tests/Run-ClientFairPlayFrameworkTests.ps1; старый Harmony .NET8FAIL сохранён.
Installer test: python -B tests/client_fairplay_install_test.py
client-files/qa/client-fairplay-418e9334bc3b87ff r2; exit0,9commands,
2ожидаемых BLOCKED exit2,145nativefiles preserved; deployment-test-r2/RESULT.json.
Ранний WhatIf parser FAIL не являлся installer mutation; r2 исправляет harness.
Backend hooks вызваны на настоящих типах Fika в отдельном .NET процессе с null
неисполняемыми downstream dependencies; это не HTTP/DI/Unity lifecycle acceptance.
Unity human/GUI/raid/day-night/PMC/scav/transit/real2-4: NOT_RUN/BLOCKED.
Production/stable unchanged; synthetic evidence не называется игровым результатом.
''')
        with (ROOT/'docs/06_SOURCES.md').open('a',encoding='utf-8') as stream:stream.write('''
## Fair Play QA, 17 октября 2026
- Installed Fika Core2.4.5 source: project-fika/Fika-Plugin commit
  cd97a87368ec33b2d8ffe226c5c2e0d5a6838beb, локальный SOURCE_MANIFEST.
- Fika Server2.4.2 commit fc10bd1cb2a1f8a98183118b10f91250ba1584f4:
  четыре public source contracts fetched without execution, exact URLs/SHA256
  в artifacts/client-fairplay-20261017/upstream/source-manifest.json.
- DynamicMaps1.2.1 binary source99200c60f084d1f64491dee93ceb09351bb8a2cd,
  Plugin/Config/Settings.cs SHAfa50875563839e53310437c6426e1a8770798f26f5718fe76eb2ba675ea48254.
- Actual installed DLL metadata/IL contracts are private local evidence in
  artifacts/client-fairplay-20261017/*.private.*. Никакие game DLL не экспортируются.
''')
    print(json.dumps({k:result[k] for k in ['status','delivery','server_artifact','production_changed','stable_current_changed']},ensure_ascii=False,indent=2))
if __name__=='__main__':main()
