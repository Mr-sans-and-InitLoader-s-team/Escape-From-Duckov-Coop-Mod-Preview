"""Resolve captured JIT return addresses in an isolated Mono test player."""
import argparse,ctypes,json,pathlib,struct
from functools import lru_cache
from ctypes import wintypes as W

@lru_cache(maxsize=8)
def exports(path):
    b=path.read_bytes();pe=struct.unpack_from('<I',b,60)[0];op=pe+24
    sections=[]
    for i in range(struct.unpack_from('<H',b,pe+6)[0]):
        p=op+struct.unpack_from('<H',b,pe+20)[0]+i*40
        size,rva,raw_size,raw=struct.unpack_from('<IIII',b,p+8);sections.append((rva,max(size,raw_size),raw))
    def at(rva):return next(raw+rva-start for start,size,raw in sections if start<=rva<start+size)
    ex=at(struct.unpack_from('<I',b,op+112)[0]);count=struct.unpack_from('<I',b,ex+24)[0]
    names=at(struct.unpack_from('<I',b,ex+32)[0]);funcs=at(struct.unpack_from('<I',b,ex+28)[0]);ords=at(struct.unpack_from('<I',b,ex+36)[0])
    result={}
    for i in range(count):
        p=at(struct.unpack_from('<I',b,names+i*4)[0]);name=b[p:b.index(b'\0',p)].decode()
        result[name]=struct.unpack_from('<I',b,funcs+struct.unpack_from('<H',b,ords+i*2)[0]*4)[0]
    return result

def main():
    parser=argparse.ArgumentParser();parser.add_argument('pid',type=int);parser.add_argument('stack',type=pathlib.Path);args=parser.parse_args()
    root=pathlib.Path(__file__).resolve().parents[2]/'artifacts/runtime-tests'
    records=json.loads((root/'processes.json').read_text(encoding='utf-8-sig'))
    record=next(r for r in records if r['id']==args.pid)
    k=ctypes.WinDLL('kernel32',use_last_error=True);ps=ctypes.WinDLL('psapi',use_last_error=True)
    def api(lib,name,restype,types):
        f=getattr(lib,name);f.restype=restype;f.argtypes=types;return f
    pointer=ctypes.c_void_p;size=ctypes.c_size_t
    open_process=api(k,'OpenProcess',pointer,[W.DWORD,W.BOOL,W.DWORD]);close=api(k,'CloseHandle',W.BOOL,[pointer])
    process=open_process(0x043A,False,args.pid)
    if not process:raise ctypes.WinError(ctypes.get_last_error())
    modules=(pointer*1024)();needed=W.DWORD()
    enum=api(ps,'EnumProcessModulesEx',W.BOOL,[pointer,pointer,W.DWORD,ctypes.POINTER(W.DWORD),W.DWORD])
    module_name=api(ps,'GetModuleFileNameExW',W.DWORD,[pointer,pointer,W.LPWSTR,W.DWORD])
    image=api(k,'QueryFullProcessImageNameW',W.BOOL,[pointer,W.DWORD,W.LPWSTR,ctypes.POINTER(W.DWORD)])
    name=ctypes.create_unicode_buffer(32768);length=W.DWORD(len(name))
    if not image(process,0,name,ctypes.byref(length)) or pathlib.Path(name.value).resolve()!=pathlib.Path(record['exe']).resolve():raise RuntimeError('Not the recorded isolated game executable')
    if not enum(process,modules,ctypes.sizeof(modules),ctypes.byref(needed),3):raise ctypes.WinError(ctypes.get_last_error())
    for mod in list(modules)[:needed.value//ctypes.sizeof(pointer)]:
        module_name(process,mod,name,len(name))
        if name.value.endswith('mono-2.0-bdwgc.dll'):base=mod;path=pathlib.Path(name.value);break
    else:raise RuntimeError('Mono runtime not loaded')
    symbols=exports(path)
    frames=json.loads(args.stack.read_text(encoding='utf-8-sig'))
    addresses=list(dict.fromkeys(int(f['Address'],16) for f in frames if not f.get('Module')))
    alloc=api(k,'VirtualAllocEx',pointer,[pointer,pointer,size,W.DWORD,W.DWORD]);free=api(k,'VirtualFreeEx',W.BOOL,[pointer,pointer,size,W.DWORD])
    write=api(k,'WriteProcessMemory',W.BOOL,[pointer,pointer,pointer,size,ctypes.POINTER(size)])
    read=api(k,'ReadProcessMemory',W.BOOL,[pointer,pointer,pointer,size,ctypes.POINTER(size)])
    thread_create=api(k,'CreateRemoteThread',pointer,[pointer,pointer,size,pointer,pointer,W.DWORD,pointer])
    wait=api(k,'WaitForSingleObject',W.DWORD,[pointer,W.DWORD])
    remote=alloc(process,None,2097152,0x3000,0x40)
    if not remote:raise ctypes.WinError(ctypes.get_last_error())
    output=remote+1048576;code=bytearray(b'\x48\x83\xec\x38')
    def call(name):code.extend(b'\x48\xb8'+struct.pack('<Q',base+symbols[name])+b'\xff\xd0')
    call('mono_get_root_domain');code.extend(b'\x48\x89\x44\x24\x20\x48\x89\xc1')
    call('mono_thread_attach');code.extend(b'\x48\x89\x44\x24\x28')
    for i,address in enumerate(addresses):
        code.extend(b'\x48\x8b\x4c\x24\x20\x48\xba'+struct.pack('<Q',address));call('mono_jit_info_table_find')
        code.extend(b'\x48\x85\xc0\x74');jump=len(code);code.append(0)
        code.extend(b'\x48\x89\xc1');call('mono_jit_info_get_method')
        code.extend(b'\x48\x89\xc1\xba\x01\x00\x00\x00');call('mono_method_full_name')
        code.extend(b'\x48\xba'+struct.pack('<Q',output+i*8)+b'\x48\x89\x02');code[jump]=len(code)-jump-1
    code.extend(b'\x48\x8b\x4c\x24\x28');call('mono_thread_detach');code.extend(b'\x48\x83\xc4\x38\x31\xc0\xc3')
    blob=ctypes.create_string_buffer(bytes(code));done=size()
    if not write(process,remote,blob,len(code),ctypes.byref(done)):raise ctypes.WinError(ctypes.get_last_error())
    thread=thread_create(process,None,0,remote,None,0,None)
    if not thread:raise ctypes.WinError(ctypes.get_last_error())
    if wait(thread,5000)!=0:raise RuntimeError('Diagnostic Mono thread did not complete; remote allocation retained')
    ptrs=(ctypes.c_uint64*len(addresses))()
    if not read(process,output,ptrs,ctypes.sizeof(ptrs),ctypes.byref(done)):raise ctypes.WinError(ctypes.get_last_error())
    result=[]
    for address,ptr in zip(addresses,ptrs):
        text=ctypes.create_string_buffer(2048)
        method=None
        if ptr and read(process,ptr,text,len(text),ctypes.byref(done)):method=text.value.decode('utf-8',errors='replace')
        result.append({'address':hex(address),'method':method})
    args.stack.with_suffix('.managed.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result,indent=2));free(process,remote,0,0x8000);close(thread);close(process)

if __name__=='__main__':main()
