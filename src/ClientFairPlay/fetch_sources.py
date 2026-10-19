"""Read only the installed release's public source contracts, never execute upstream code."""
import hashlib,json,urllib.request
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'artifacts/client-fairplay-20261017/upstream'
commit='fc10bd1cb2a1f8a98183118b10f91250ba1584f4'
rows=[]
for path in ['FikaServer/Controllers/RaidController.cs','FikaServer/Callbacks/RaidCallbacks.cs','FikaServer/Services/Headless/HeadlessService.cs','FikaServer/Models/Fika/Routes/Raid/Create/FikaRaidCreateRequestData.cs']:
    url='https://raw.githubusercontent.com/project-fika/Fika-Server-CSharp/'+commit+'/'+path
    with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'PrivateStack-source-review/1.0'}),timeout=30) as response:
        if not response.url.startswith('https://raw.githubusercontent.com/'):raise RuntimeError('Unexpected source redirect')
        content=response.read(1024*1024+1)
    if len(content)>1024*1024:raise RuntimeError('Source size cap')
    dest=OUT/path;dest.parent.mkdir(parents=True,exist_ok=True)
    if dest.exists():assert dest.read_bytes()==content
    else:dest.write_bytes(content)
    rows.append(dict(path=path,url=url,sha256=hashlib.sha256(content).hexdigest(),bytes=len(content)))
(OUT/'source-manifest.json').write_text(json.dumps(dict(commit=commit,files=rows),indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(status='FETCHED_SOURCE_ONLY',files=len(rows)),indent=2))
