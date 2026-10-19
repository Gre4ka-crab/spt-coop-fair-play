"""Explicit own-file addon package. QA only until human-client runtime acceptance."""
import hashlib,json,sys,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'tools'))
from stacklib import verify_file,digest,scan_secrets,unique_paths,inside
from pin_client import crc32c

def encoded(value):return (json.dumps(value,ensure_ascii=False,indent=2)+'\n').encode()
def build(apply=False):
    base=json.loads((ROOT/'client-files/qa/client-deploy-c3289b8fe888f225/deployment-manifest.json').read_text())
    binary=HERE/'bin/Release/netstandard2.1/ClientFairPlay.dll'
    pin=json.loads((ROOT/'src/FairPlayServer/client-pin.json').read_text())
    verify_file(binary,pin['sha256'],pin['bytes']);assert crc32c(binary.read_bytes())==pin['crc32c']
    manifest=dict(status='QA_CANDIDATE_UNITY_AND_REAL_COOP_PENDING',spt_version='4.1.6',eft_file_version='0.16.9.40743',fika_core='2.4.5',fika_server='2.4.2',stable_current_update_allowed=False,client_guid='private.spt.clientfairplay',required_role='human-only via native validation supplement; prohibited on headless',client_crc32c=pin['crc32c'])
    prerequisites={r['path']:{k:r[k] for k in ('path','bytes','sha256')} for r in base['prerequisites']}
    for row in base['files']:
        if row['path'].startswith('BepInEx/config/'):continue
        prerequisites[row['path']]={k:row[k] for k in ('path','bytes','sha256')}
    for name in ('BepInEx.dll','0Harmony.dll'):
        path='BepInEx/core/'+name;source=Path('C:/Games/SPT-Coop')/path
        prerequisites[path]=dict(path=path,sha256=digest(source),bytes=source.stat().st_size)
    for name in ('Unity.TextMeshPro.dll','UnityEngine.UI.dll','websocket-sharp.dll'):
        path='EscapeFromTarkov_Data/Managed/'+name;source=Path('C:/Games/SPT-Coop')/path
        prerequisites[path]=dict(path=path,sha256=digest(source),bytes=source.stat().st_size)
    manifest['prerequisites']=list(prerequisites.values())
    manifest['previous_own_builds']={'BepInEx/plugins/ClientFairPlay/ClientFairPlay.dll':['d05f9e11b758cbad331019ac8f187ff85e0d7c7f3e31d738d097e248fc70ab2a','36b72308e62dfb975c36164730ed3b819ed489d5f11acebb00f7ff89150e736e']}
    payload={
        'overlay/BepInEx/plugins/ClientFairPlay/ClientFairPlay.dll':binary.read_bytes(),
        'overlay/BepInEx/plugins/ClientFairPlay/LICENSE.txt':(HERE/'LICENSE.txt').read_bytes(),
        'Install-Addon.ps1':(HERE/'Install-Addon.ps1').read_bytes(),
        'README_RU.md':(HERE/'README_RU.md').read_bytes(),
        'runtime-policy.json':(HERE/'policy.json').read_bytes(),
        'tools/install_addon.py':(HERE/'install_addon.py').read_bytes(),
        'tools/stacklib.py':(ROOT/'tools/stacklib.py').read_bytes(),
        'tools/client_guard.py':(ROOT/'tools/client_guard.py').read_bytes(),
    }
    manifest['files']=[dict(path=name.removeprefix('overlay/'),source=name,sha256=hashlib.sha256(data).hexdigest(),bytes=len(data),component='client-fairplay',release='0.2.0',role='human',policy='replace-managed-only',licence='MIT') for name,data in payload.items() if name.startswith('overlay/')]
    assert len(manifest['files'])==2
    manifest['package_helpers']={name:hashlib.sha256(data).hexdigest() for name,data in payload.items()}
    ident=hashlib.sha256(encoded(manifest)).hexdigest()[:16];manifest['build_id']=ident
    payload['client-manifest.json']=encoded(manifest)
    payload['SHA256SUMS.txt']=''.join(hashlib.sha256(data).hexdigest()+'  '+name+'\n' for name,data in sorted(payload.items())).encode()
    unique_paths(payload)
    assert [n for n in payload if n.endswith('.dll')]==['overlay/BepInEx/plugins/ClientFairPlay/ClientFairPlay.dll']
    assert not any(n.endswith(('.exe','.pdb','.db','.key','.pfx')) for n in payload)
    for name,data in payload.items():
        if name.endswith(('.json','.cfg')):assert not scan_secrets(data.decode('utf-8-sig'))
    dest=ROOT/'client-files/qa'/('client-fairplay-'+ident)
    result=dict(status='QA_PACKAGE_PLAN',build_id=ident,path=str(dest.relative_to(ROOT)),files=len(payload),managed_files=2,prerequisites=len(prerequisites),third_party_binaries=0)
    if not apply:return result
    for name,data in payload.items():
        target=inside(dest,name);target.parent.mkdir(parents=True,exist_ok=True)
        if target.exists():assert target.read_bytes()==data
        else:target.write_bytes(data)
    archive=dest.with_suffix('.zip')
    if not archive.exists():
        with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED) as package:
            for name,data in sorted(payload.items()):package.writestr(name,data)
    with zipfile.ZipFile(archive) as package:
        assert set(package.namelist())==set(payload)
        assert all(package.read(name)==data for name,data in payload.items())
    archive.with_suffix('.zip.sha256').write_text(digest(archive)+'  '+archive.name+'\n',encoding='utf-8')
    result.update(status='QA_PACKAGE_BUILT_RUNTIME_PENDING',archive=str(archive.relative_to(ROOT)),sha256=digest(archive),bytes=archive.stat().st_size)
    return result
if __name__=='__main__':print(json.dumps(build('--apply' in sys.argv),ensure_ascii=False,indent=2))
