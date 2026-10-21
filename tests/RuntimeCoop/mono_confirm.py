"""Read/confirm an already visible native dialogue in recorded isolated players."""
import argparse,ctypes,json,pathlib,struct
from ctypes import wintypes as W
from mono_stack_symbols import exports

def query(role,confirm=False,scene=False,island=False):
    root=pathlib.Path(__file__).resolve().parents[2]/'artifacts/runtime-tests'
    record=next(r for r in json.loads((root/'processes.json').read_text(encoding='utf-8-sig')) if r['role']==role)
    k=ctypes.WinDLL('kernel32',use_last_error=True);ps=ctypes.WinDLL('psapi',use_last_error=True)
    ptr=ctypes.c_void_p;size=ctypes.c_size_t
    def api(lib,name,result,types):
        f=getattr(lib,name);f.restype=result;f.argtypes=types;return f
    process=api(k,'OpenProcess',ptr,[W.DWORD,W.BOOL,W.DWORD])(0x043A,False,record['id'])
    if not process:raise ctypes.WinError(ctypes.get_last_error())
    buf=ctypes.create_unicode_buffer(32768);length=W.DWORD(len(buf))
    if not api(k,'QueryFullProcessImageNameW',W.BOOL,[ptr,W.DWORD,W.LPWSTR,ctypes.POINTER(W.DWORD)])(process,0,buf,ctypes.byref(length)) or pathlib.Path(buf.value).resolve()!=pathlib.Path(record['exe']).resolve():raise RuntimeError('Not a recorded isolated player')
    modules=(ptr*1024)();needed=W.DWORD()
    api(ps,'EnumProcessModulesEx',W.BOOL,[ptr,ptr,W.DWORD,ctypes.POINTER(W.DWORD),W.DWORD])(process,modules,ctypes.sizeof(modules),ctypes.byref(needed),3)
    module_name=api(ps,'GetModuleFileNameExW',W.DWORD,[ptr,ptr,W.LPWSTR,W.DWORD])
    for module in list(modules)[:needed.value//ctypes.sizeof(ptr)]:
        module_name(process,module,buf,len(buf))
        if buf.value.endswith('mono-2.0-bdwgc.dll'):base=module;symbols=exports(pathlib.Path(buf.value));break
    else:raise RuntimeError('No Mono runtime')
    remote=api(k,'VirtualAllocEx',ptr,[ptr,ptr,size,W.DWORD,W.DWORD])(process,None,65536,0x3000,0x40)
    if not remote:raise ctypes.WinError(ctypes.get_last_error())
    output=remote+32768;strings=bytearray();code=bytearray(b'\x48\x83\xec\x38');jumps=[]
    def cstring(text):
        address=remote+16384+len(strings);strings.extend(text.encode()+b'\0');return address
    def argument(register,value,slot=False):
        code.extend({'c':b'\x48\xb9','d':b'\x48\xba','8':b'\x49\xb8'}[register]+struct.pack('<Q',value))
        if slot:code.extend({'c':b'\x48\x8b\x09','d':b'\x48\x8b\x12','8':b'\x4d\x8b\x00'}[register])
    def call(name):code.extend(b'\x48\xb8'+struct.pack('<Q',base+symbols[name])+b'\xff\xd0')
    def store(offset,required=True):
        code.extend(b'\x48\xba'+struct.pack('<Q',output+offset)+b'\x48\x89\x02')
        if required:code.extend(b'\x48\x85\xc0\x0f\x84');jumps.append(len(code));code.extend(b'\0'*4)
    def field(class_slot,name,destination):
        argument('c',output+class_slot,True);argument('d',cstring(name));call('mono_class_get_field_from_name');store(destination)
    def value(object_slot,field_slot,destination):
        argument('c',output+object_slot,True);argument('d',output+field_slot,True);argument('8',output+destination);call('mono_field_get_value')
    call('mono_get_root_domain');store(0)
    argument('c',output,True);call('mono_thread_attach');code.extend(b'\x48\x89\x44\x24\x28')
    assembly=pathlib.Path(record['exe']).parent/('Duckov_Data/Mods/3591341282/EscapeFromDuckovCoopMod.dll' if scene else 'Duckov_Data/Managed/TeamSoda.Duckov.Core.dll')
    argument('c',output,True);argument('d',cstring(str(assembly)));call('mono_domain_assembly_open');store(16)
    argument('c',output+16,True);call('mono_assembly_get_image');store(24)
    argument('c',output+24,True);argument('d',cstring('EscapeFromDuckovCoopMod' if scene else 'Duckov.UI'));argument('8',cstring('SceneNet' if scene else 'ClosureView' if island else 'ConfirmDialogue'));call('mono_class_from_name');store(32)
    field(32,'Instance' if scene else 'OverrideClosureView' if island else 'instance',40)
    argument('c',output,True);argument('d',output+32,True);call('mono_class_vtable');store(48)
    argument('c',output+48,True);argument('d',output+40,True);argument('8',output+56);call('mono_field_static_get_value')
    argument('c',output+56,True);code.extend(b'\x48\x85\xc9\x0f\x84');jumps.append(len(code));code.extend(b'\0'*4)
    if island:argument('c',output+56,True);call('mono_object_get_class');store(32)
    scene_fields=['sceneVoteActive','sceneNotifyEvac','sceneSaveToFile','allowLocalSceneLoad','sceneUseLocation','IsMapSelectionEntry','IsDoteleportMap','_localSceneLoadLaunched','_srvPendingSceneLoad','_srvPendingBeginLoad','_srvLoadInProgress','_cliSceneGateReleased']
    if scene:
        for i,name in enumerate(scene_fields):field(32,name,64);value(56,64,512+i*8)
        for i,name in enumerate(['sceneTargetId','sceneLocationName']):
            field(32,name,64);value(56,64,72);argument('c',output+72,True);call('mono_string_to_utf8');store(640+i*8,False)
    else:
        field(32,'mainFadeGroup' if island else 'fadeGroup',64);value(56,64,72)
        argument('c',output+72,True);call('mono_object_get_class');store(80)
        field(80,'isShown',88);value(72,88,256)
        field(32,'titleText' if island else 'text',96);value(56,96,104)
        argument('c',output+104,True);call('mono_object_get_class');store(112)
        field(112,'m_text',120);value(104,120,128)
        argument('c',output+128,True);call('mono_string_to_utf8');store(136,False)
    if confirm:
        argument('c',output+256);code.extend(b'\x80\x39\x00\x0f\x84');jumps.append(len(code));code.extend(b'\0'*4)
        field(32,'confirmed' if island else 'confirmClicked',144)
        argument('c',output+56,True);argument('d',output+144,True);argument('8',output+264);call('mono_field_set_value')
    finish=len(code)
    for jump in jumps:struct.pack_into('<i',code,jump,finish-jump-4)
    code.extend(b'\x48\x8b\x4c\x24\x28');call('mono_thread_detach');code.extend(b'\x48\x83\xc4\x38\x31\xc0\xc3')
    write=api(k,'WriteProcessMemory',W.BOOL,[ptr,ptr,ptr,size,ctypes.POINTER(size)]);read=api(k,'ReadProcessMemory',W.BOOL,[ptr,ptr,ptr,size,ctypes.POINTER(size)]);done=size()
    for address,data in [(remote,bytes(code)),(remote+16384,bytes(strings)),(output+264,b'\x01')]:
        payload=ctypes.create_string_buffer(data)
        if not write(process,address,payload,len(data),ctypes.byref(done)):raise ctypes.WinError(ctypes.get_last_error())
    thread=api(k,'CreateRemoteThread',ptr,[ptr,ptr,size,ptr,ptr,W.DWORD,ptr])(process,None,0,remote,None,0,None)
    if not thread:raise ctypes.WinError(ctypes.get_last_error())
    if api(k,'WaitForSingleObject',W.DWORD,[ptr,W.DWORD])(thread,5000)!=0:raise RuntimeError('Native dialogue metadata query timed out')
    result=ctypes.create_string_buffer(672)
    if not read(process,output,result,len(result),ctypes.byref(done)):raise ctypes.WinError(ctypes.get_last_error())
    text_ptr=struct.unpack_from('<Q',result.raw,136)[0];text=''
    if text_ptr:
        content=ctypes.create_string_buffer(4096)
        if read(process,text_ptr,content,len(content),ctypes.byref(done)):text=content.value.decode('utf-8',errors='replace')
    value={'role':role,'shown':bool(result.raw[256]),'text':text,'confirmed':confirm and bool(result.raw[256])}
    if scene:
        value={'role':role,**{name:bool(result.raw[512+i*8]) for i,name in enumerate(scene_fields)}}
        for i,name in enumerate(['sceneTargetId','sceneLocationName']):
            content=ctypes.create_string_buffer(4096);p=struct.unpack_from('<Q',result.raw,640+i*8)[0]
            value[name]=content.value.decode('utf-8',errors='replace') if p and read(process,p,content,len(content),ctypes.byref(done)) else None
    api(k,'VirtualFreeEx',W.BOOL,[ptr,ptr,size,W.DWORD])(process,remote,0,0x8000)
    close=api(k,'CloseHandle',W.BOOL,[ptr]);close(thread);close(process);return value

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('role');p.add_argument('--confirm',action='store_true');p.add_argument('--scene',action='store_true');p.add_argument('--island',action='store_true');a=p.parse_args()
    print(json.dumps(query(a.role,a.confirm,a.scene,a.island),ensure_ascii=True))
