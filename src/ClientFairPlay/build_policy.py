"""Freeze known, pinned configuration schema; never trust a player's cfg as baseline."""
import configparser
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
SOURCE = ROOT / 'artifacts/requested-addons/upstream/source/acidphantasm__SPT-DynamicMaps.binary/Plugin/Config/Settings.cs'
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == 'fa50875563839e53310437c6426e1a8770798f26f5718fe76eb2ba675ea48254'
source = SOURCE.read_text()
constants = dict(re.findall(r'const string (\w+)\s*=\s*"([^"]+)"', source))
entries = []
for section, key in re.findall(r'ConfigEntries.Add\(\w+ = config.Bind\(\s*(\w+),\s*"([^"]+)"', source):
    entries.append(dict(section=constants[section], key=key, value=None))
assert len(entries) > 70 and len({(e['section'], e['key']) for e in entries}) == len(entries)
import sys
sys.path.insert(0, str(ROOT/'tools'))
from map_privacy_policy import SETTINGS
overrides = {(s, k): str(v).lower() if type(v) is bool else v for s, k, v in SETTINGS}
overrides[('1. General','Dump Info Hotkey')]='None'
for entry in entries:
    entry['value'] = overrides.get((entry['section'], entry['key']))
assert len(overrides) == 10 and all(any((e['section'],e['key']) == pair for e in entries) for pair in overrides)

def cfg(name):
    parser = configparser.ConfigParser(interpolation=None, strict=True, delimiters=('=',))
    parser.optionxform = str
    parser.read(ROOT/'src/ClientDeployment/config'/name, encoding='utf-8-sig')
    return [dict(section=s, key=k, value=v) for s in parser.sections() for k,v in parser.items(s)]

fika = [
    ('Advanced', 'Developer Mode', 'false'), ('Advanced', 'No AI', 'false'), ('Advanced', 'No Loot', 'false'),
    ('Coop', 'Auto Use Headless', 'true'), ('Coop', 'Auto Extract', 'false'),
    ('Coop | Quest Sharing', 'Easy Kill Conditions', 'false'),
    ('Coop | Quest Sharing', 'Shared Kill Experience', 'false'), ('Coop | Quest Sharing', 'Shared Boss Experience', 'false'),
    ('Gameplay', 'Disable Bot Metabolism', 'false'), ('Gameplay', 'Strict Inventory Sync', 'true'),
]
fika_source=ROOT/'vendor-src/project-fika__Fika-Plugin/cd97a87368ec33b2d8ffe226c5c2e0d5a6838beb/Fika.Core/FikaConfig.cs'
fika_text=fika_source.read_text()
fika_constants=dict(re.findall(r'const string (\w+)\s*=\s*"([^"]+)"',fika_text))
fika_entries=[]
for section,key in re.findall(r'\w+ = SetupSetting\(\s*(\w+|"[^"]+"),\s*"([^"]+)"',fika_text):
    section=section[1:-1] if section.startswith('"') else fika_constants[section]
    if section=='Network':continue
    fika_entries.append(dict(section=section,key=key,value=next((v for s,k,v in fika if (s,k)==(section,key)),None)))
assert len(fika_entries)>40 and all(any((e['section'],e['key'])==(s,k) for e in fika_entries) for s,k,v in fika)
pins = {'BepInEx/plugins/Fika/Fika.Core.dll':'5e62bc9c0adebd26e738dc9fd429ae24e8ecdf6f8b4ad65a2ad4ee323246395a'}
manifest = json.loads((ROOT/'client-files/qa/client-deploy-c3289b8fe888f225/deployment-manifest.json').read_text())
for row in manifest['files']:
    if row['path'].endswith('.dll') and row['component'] in {'dynamic-maps','all-quests-checkmarks','ui-fixes'}:
        pins[row['path']] = row['sha256']
plugin_pins={
 'com.fika.core':pins['BepInEx/plugins/Fika/Fika.Core.dll'],
 'com.mpstark.dynamicmaps':pins['BepInEx/plugins/mpstark-dynamicmaps/DynamicMaps.dll'],
 'com.tyfon.uifixes':pins['BepInEx/plugins/Tyfon.UIFixes.dll'],
 'com.zgfuedkx.allquestscheckmarks':pins['BepInEx/plugins/AllQuestsCheckmarks/AllQuestsCheckmarks-Client.dll'],
}
policy = dict(version=1, pins=pins, plugin_pins=plugin_pins, settings={
    'com.mpstark.dynamicmaps': entries,
    'com.fika.core': fika_entries,
    'com.tyfon.uifixes': cfg('com.tyfon.uifixes.cfg'),
    'com.zgfuedkx.allquestscheckmarks': cfg('com.zgfuedkx.allquestscheckmarks.cfg'),
}, freeze_defaults=['com.mpstark.dynamicmaps','com.fika.core','com.tyfon.uifixes','com.zgfuedkx.allquestscheckmarks'], unlocked_sections={'com.fika.core':['Network','Hidden']})
content=(json.dumps(policy,ensure_ascii=False,indent=2)+'\n').encode()
if '--apply' in sys.argv:
    target=HERE/'policy.json'
    if target.exists() and target.read_bytes()!=content:
        if '--replace-reviewed' not in sys.argv:raise RuntimeError('Existing policy differs; review change and back it up before replacing')
        import shutil
        backup=ROOT/'backups/client-fairplay-source-20261017'/hashlib.sha256(target.read_bytes()).hexdigest()/'policy.json'
        backup.parent.mkdir(parents=True,exist_ok=True)
        if not backup.exists():shutil.copyfile(target,backup)
        assert backup.read_bytes()==target.read_bytes()
    target.write_bytes(content)
print(json.dumps(dict(status='WRITTEN' if '--apply' in sys.argv else 'DRY_RUN',entries={k:len(v) for k,v in policy['settings'].items()},sha256=hashlib.sha256(content).hexdigest()),indent=2))
