"""Own human-client addon only. Dry run by default; no downloads or backend writes."""
from pathlib import Path
import argparse,hashlib,json,shutil,sys,uuid
from datetime import datetime,timezone
from stacklib import Blocked,inside,no_links,load,save,digest,verify_file,transaction,atomic_bytes,unique_paths
from client_guard import validate_target

BUNDLE=Path(__file__).resolve().parents[1]
DLL='BepInEx/plugins/ClientFairPlay/ClientFairPlay.dll'
LICENCE='BepInEx/plugins/ClientFairPlay/LICENSE.txt'
STATE='BepInEx/.clientfairplay/state.json'
PATHS={DLL,LICENCE,STATE}
CONFIGS=['com.fika.core.cfg','com.mpstark.dynamicmaps.cfg','com.tyfon.uifixes.cfg','com.zgfuedkx.allquestscheckmarks.cfg']

def package():
    manifest=load(BUNDLE/'client-manifest.json')
    if {r['path'] for r in manifest['files']}!={DLL,LICENCE}:raise Blocked('Unexpected addon file allowlist')
    unique_paths([r['path'] for r in manifest['files']])
    for path,sha in manifest['package_helpers'].items():verify_file(inside(BUNDLE,path),sha)
    for row in manifest['files']:verify_file(inside(BUNDLE,row['source']),row['sha256'],row['bytes'])
    return manifest

def execute(action,target,apply=False,journal=None):
    manifest=package();target=validate_target(target,manifest)
    if action!='rollback':
        for row in manifest['prerequisites']:verify_file(inside(target,row['path']),row['sha256'],row.get('bytes'))
    statepath=inside(target,STATE)
    old=load(statepath) if statepath.exists() else {'files':[]}
    if action=='verify':
        for row in manifest['files']:verify_file(inside(target,row['path']),row['sha256'],row['bytes'])
        if old.get('build_id')!=manifest['build_id']:raise Blocked('Addon ownership receipt absent or different')
        return dict(status='PASS_ADDON_FILES_ONLY_GAMEPLAY_PENDING',build_id=manifest['build_id'])
    if action=='rollback':
        if journal is None:raise Blocked('Exact committed transaction journal is required')
        journal=no_links(journal)
        root=inside(target,'BepInEx/.clientfairplay/transactions')
        if not journal.resolve().is_relative_to(root.resolve()) or journal.name!='transaction.json':raise Blocked('Foreign rollback journal')
        receipt=load(journal)
        if receipt['status']!='COMMITTED' or Path(receipt['root']).resolve()!=target.resolve() or any(r['path'] not in PATHS for r in receipt['changes']):raise Blocked('Invalid ownership receipt')
        changes=[]
        for row in receipt['changes']:
            dest=inside(target,row['path'])
            if (digest(dest) if dest.exists() else None)!=row['after']:raise Blocked('Changed file preserved during rollback')
            source=inside(journal.parent,'files/'+row['path'])
            if row['before'] is not None:verify_file(source,row['before'])
            changes.append(dict(path=row['path'],expected_sha256=row['after'],data=source.read_bytes() if row['before'] is not None else None))
        return transaction(target,changes,root,dry_run=not apply)
    previous={r['path']:r['sha256'] for r in old['files']}
    changes=[]
    for row in manifest['files']:
        dest=inside(target,row['path']);before=digest(dest) if dest.exists() else None
        accepted={None,row['sha256'],previous.get(row['path'])}
        accepted.update(manifest.get('previous_own_builds',{}).get(row['path'],[]))
        if before not in accepted:raise Blocked('Unmanaged addon file preserved: '+row['path'])
        changes.append(dict(path=row['path'],expected_sha256=before,data=inside(BUNDLE,row['source']).read_bytes()))
    state=dict(build_id=manifest['build_id'],files=[{k:r[k] for k in ('path','sha256')} for r in manifest['files']])
    changes.append(dict(path=STATE,expected_sha256=digest(statepath) if statepath.exists() else None,data=(json.dumps(state,indent=2)+'\n').encode()))
    root=inside(target,'BepInEx/.clientfairplay/transactions')
    plan=transaction(target,changes,root,dry_run=True)
    if not apply:return plan
    if plan['changes']:
        # Save cfg/state before installing the plugin. Config bytes are not changed here.
        snapshots=inside(root,datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S')+'-configs-'+uuid.uuid4().hex)
        records=[]
        for name in CONFIGS:
            source=inside(target,'BepInEx/config/'+name)
            if not source.exists():continue
            dest=inside(snapshots,name);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest);verify_file(dest,digest(source))
            records.append(dict(path='BepInEx/config/'+name,sha256=digest(dest)))
        save(snapshots/'snapshot.json',dict(files=records,automatic_config_restore=False))
    return transaction(target,changes,root)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['install','verify','rollback']);parser.add_argument('--target',type=Path,required=True);parser.add_argument('--apply',action='store_true');parser.add_argument('--journal',type=Path)
    args=parser.parse_args()
    try:print(json.dumps(execute(args.action,args.target,args.apply,args.journal),ensure_ascii=False,indent=2))
    except (Blocked,ValueError,OSError,KeyError) as error:print('BLOCKED: '+str(error),file=sys.stderr);sys.exit(2)
