"""Pin own client binary for Fika's native CRC32C exchange; no first-client trust."""
from pathlib import Path
import hashlib,json,sys
ROOT=Path(__file__).resolve().parents[2]
def crc32c(data):
    value=0xffffffff
    for byte in data:
        value^=byte
        for _ in range(8):value=(value>>1)^(0x82f63b78 if value&1 else 0)
    return value^0xffffffff
assert crc32c(b'123456789')==0xe3069283
if __name__=='__main__':
    data=(ROOT/'src/ClientFairPlay/bin/Release/netstandard2.1/ClientFairPlay.dll').read_bytes()
    result=dict(sha256=hashlib.sha256(data).hexdigest(),crc32c=crc32c(data),bytes=len(data))
    if '--apply' in sys.argv:
        dest=ROOT/'src/FairPlayServer/client-pin.json'
        dest.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result,indent=2))
