import sys, struct, zlib
def h65599(s):
    h=len(s); c=65599
    for ch in s: h=(h+c*ord(ch))&0xffffffff; c=(c*65599)&0xffffffff
    return h
names = {}
for svc, methods in {
  'maestro_pw.Maestro': ['GetSoftwareInfo','GetHardwareInfo','SubscribeRuntimeInfo','SetWallClock','WriteSetting','ReadSetting','SubscribeToSettingsChanges','SubscribeToOobeActions'],
  'maestro_pw.Multipoint': ['SubscribeToQuietModeStatus','ForceMultipointSwitch'],
  'maestro_pw.EartipFitTest': ['StartTest','EndTest','SubscribeToResults'],
  'maestro_pw.JitterBuffer': ['SetJitterBufferSizePreference'],
  'maestro_pw.Dosimeter': ['FetchDailySummaries','SubscribeToLiveDb'],
}.items():
    names[h65599(svc)] = svc.split('.')[1]
    for m in methods: names[h65599(m)] = m
def varint(b,i):
    v=s=0
    while True:
        x=b[i]; i+=1; v|=(x&0x7f)<<s; s+=7
        if not x&0x80: return v,i
def fields(b):
    i=0; out=[]
    while i<len(b):
        k,i=varint(b,i); f,t=k>>3,k&7
        if t==0: v,i=varint(b,i)
        elif t==1: v=b[i:i+8]; i+=8
        elif t==5: v=struct.unpack('<I',b[i:i+4])[0]; i+=4
        elif t==2: n,i=varint(b,i); v=b[i:i+n]; i+=n
        else: raise ValueError('wt%d'%t)
        out.append((f,t,v))
    return out
def dump(b, depth=0):
    try: fs=fields(b)
    except Exception: return b.hex()
    parts=[]
    for f,t,v in fs:
        if t==2:
            inner = dump(v, depth+1) if v else ''
            parts.append('%d:{%s}'%(f,inner))
        elif t==5: parts.append('%d:f%s(%g)'%(f,hex(v),struct.unpack('<f',struct.pack('<I',v))[0]))
        else: parts.append('%d:%s'%(f,v if t==0 else v.hex()))
    return ' '.join(parts)
PT={0:'REQ',1:'RESP',2:'CLI_STREAM',3:'SRV_ERR',4:'CLI_ERR',5:'SRV_STREAM',6:'CLI_DONE'}
buf={'TX':b'','RX':b''}
for line in open(sys.argv[1]):
    p=line.split(' ',2)
    if len(p)<3: continue
    d,h=p[1],p[2].strip()
    if d=='MARK': print('\n=== '+h); continue
    if d not in buf: continue
    buf[d]+=bytes.fromhex(h)
    while True:
        b=buf[d]; s=b.find(0x7e)
        if s<0: buf[d]=b''; break
        e=b.find(0x7e,s+1)
        if e<0: buf[d]=b[s:]; break
        raw=b[s+1:e]; buf[d]=b[e:]
        if not raw: continue
        fr=bytearray(); j=0
        while j<len(raw):
            if raw[j]==0x7d: fr.append(raw[j+1]^0x20); j+=2
            else: fr.append(raw[j]); j+=1
        fr=bytes(fr)
        if len(fr)<6: continue
        crc_ok = zlib.crc32(fr[:-4])==struct.unpack('<I',fr[-4:])[0]
        a=0; k=0
        while True:
            x=fr[k]; a|=(x>>1)<<(7*k); k+=1
            if x&1: break
        body=fr[k+1:-4]
        try: pk={f:v for f,t,v in fields(body)}
        except Exception: print(d,'?',fr.hex()); continue
        typ=PT.get(pk.get(1,0),pk.get(1,0))
        print('%s %s ch=%s call=%s %s/%s st=%s%s  %s'%(d,typ,pk.get(2),pk.get(7),names.get(pk.get(3),hex(pk.get(3,0))),names.get(pk.get(4),hex(pk.get(4,0))),pk.get(6,0),'' if crc_ok else ' BADCRC',dump(pk.get(5,b''))))
