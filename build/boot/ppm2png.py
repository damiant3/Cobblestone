import sys, zlib, struct

def read_ppm(path):
    d = open(path,'rb').read()
    assert d[:2]==b'P6', "not P6 PPM"
    i=2; vals=[]
    while len(vals)<3:
        while i<len(d) and d[i] in b' \t\n\r': i+=1
        if d[i:i+1]==b'#':
            while i<len(d) and d[i] not in b'\n': i+=1
            continue
        j=i
        while j<len(d) and d[j] not in b' \t\n\r': j+=1
        vals.append(int(d[i:j])); i=j
    w,h,mx=vals
    i+=1
    return w,h,d[i:i+w*h*3]

def write_png(path,w,h,rgb):
    def chunk(t,data):
        c=t+data
        return struct.pack('>I',len(data))+c+struct.pack('>I',zlib.crc32(c)&0xffffffff)
    raw=bytearray()
    for y in range(h):
        raw.append(0)
        raw+=rgb[y*w*3:(y+1)*w*3]
    png=b'\x89PNG\r\n\x1a\n'
    png+=chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,2,0,0,0))
    png+=chunk(b'IDAT',zlib.compress(bytes(raw),9))
    png+=chunk(b'IEND',b'')
    open(path,'wb').write(png)

w,h,rgb=read_ppm(sys.argv[1])
write_png(sys.argv[2],w,h,rgb)
print(f"  {w}x{h} -> {sys.argv[2]}")
