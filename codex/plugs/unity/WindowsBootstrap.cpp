#define WIN32_LEAN_AND_MEAN
#include <windows.h>

static HMODULE self_module;
static HMODULE mono_module;
static volatile LONG initialized;
typedef void* (__cdecl *InvokeFn)(void*, void*, void**, void**);
static InvokeFn original_invoke;
typedef const char* (__cdecl *NameFn)(void*);
typedef void* (__cdecl *ClassFn)(void*);
static NameFn method_name;
static ClassFn method_class;
static NameFn class_name;

static void log_line(const char* text) {
    wchar_t path[MAX_PATH];
    DWORD n = GetModuleFileNameW(self_module, path, MAX_PATH);
    if (!n || n >= MAX_PATH) return;
    while (n && path[n - 1] != L'\\') --n;
    path[n] = 0;
    if (n + 21 >= MAX_PATH) return;
    lstrcatW(path, L"prism-bootstrap.log");
    HANDLE f = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                          0, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, 0);
    if (f == INVALID_HANDLE_VALUE) return;
    DWORD written;
    WriteFile(f, text, (DWORD)lstrlenA(text), &written, 0);
    WriteFile(f, "\r\n", 2, &written, 0);
    CloseHandle(f);
}

static void initialize_managed() {
    typedef void* (__cdecl *OpenImageFn)(char*, DWORD, int, int*, int);
    typedef void* (__cdecl *LoadAssemblyFn)(void*, const char*, int*, int);
    typedef void* (__cdecl *GetImageFn)(void*);
    typedef void* (__cdecl *FindClassFn)(void*, const char*, const char*);
    typedef void* (__cdecl *FindMethodFn)(void*, const char*, int);
    OpenImageFn open_image = (OpenImageFn)GetProcAddress(mono_module, "mono_image_open_from_data_full");
    LoadAssemblyFn load_assembly = (LoadAssemblyFn)GetProcAddress(mono_module, "mono_assembly_load_from_full");
    GetImageFn get_image = (GetImageFn)GetProcAddress(mono_module, "mono_assembly_get_image");
    FindClassFn find_class = (FindClassFn)GetProcAddress(mono_module, "mono_class_from_name");
    FindMethodFn find_method = (FindMethodFn)GetProcAddress(mono_module, "mono_class_get_method_from_name");
    if (!open_image || !load_assembly || !get_image || !find_class || !find_method) {
        log_line("REFUSED missing Mono entry points"); return;
    }
    HRSRC resource = FindResourceW(self_module, MAKEINTRESOURCEW(101), RT_RCDATA);
    if (!resource) { log_line("REFUSED managed payload missing"); return; }
    HGLOBAL loaded = LoadResource(self_module, resource);
    char* data = (char*)LockResource(loaded);
    DWORD size = SizeofResource(self_module, resource);
    int status = 0;
    void* image = open_image(data, size, 1, &status, 0);
    if (!image || status) { log_line("REFUSED managed image"); return; }
    void* assembly = load_assembly(image, "PrismMod.dll", &status, 0);
    if (!assembly || status) { log_line("REFUSED managed assembly"); return; }
    void* klass = find_class(get_image(assembly), "", "PrismUnityEntry");
    void* method = klass ? find_method(klass, "Initialize", 0) : 0;
    if (!method) { log_line("REFUSED managed initializer missing"); return; }
    void* exception = 0;
    original_invoke(method, 0, 0, &exception);
    log_line(exception ? "FAILED managed initializer exception" : "PASS managed initializer returned");
}

static void* __cdecl invoke_hook(void* method, void* object, void** args, void** exception) {
    void* result = original_invoke(method, object, args, exception);
    if (initialized || !method_name || !method_class || !class_name) return result;
    const char* name = method_name(method);
    const char* owner = class_name(method_class(method));
    if (name && owner && (lstrcmpA(owner, "SceneLoader") == 0 || lstrcmpA(owner, "FejdStartup") == 0) && lstrcmpA(name, "Awake") == 0) {
        if (exception && *exception) return result;
        if (InterlockedCompareExchange(&initialized, 1, 0) == 0) {
            log_line("Prism entering managed initializer after scene Awake");
            initialize_managed();
        }
    }
    return result;
}

static FARPROC WINAPI get_proc_hook(HMODULE module, LPCSTR name) {
    FARPROC value = GetProcAddress(module, name);
    if ((ULONG_PTR)name > 65535 && lstrcmpA(name, "mono_runtime_invoke") == 0) {
        mono_module = module;
        log_line("Prism captured mono_runtime_invoke");
        original_invoke = (InvokeFn)value;
        method_name = (NameFn)GetProcAddress(module, "mono_method_get_name");
        method_class = (ClassFn)GetProcAddress(module, "mono_method_get_class");
        class_name = (NameFn)GetProcAddress(module, "mono_class_get_name");
        return value ? (FARPROC)invoke_hook : value;
    }
    return value;
}

static void install_import_hook(HMODULE module) {
    if (!module) return;
    BYTE* base = (BYTE*)module;
    IMAGE_DOS_HEADER* dos = (IMAGE_DOS_HEADER*)base;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return;
    IMAGE_NT_HEADERS* nt = (IMAGE_NT_HEADERS*)(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return;
    DWORD rva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
    if (!rva) return;
    IMAGE_IMPORT_DESCRIPTOR* desc = (IMAGE_IMPORT_DESCRIPTOR*)(base + rva);
    for (; desc->Name; ++desc) {
        if (!desc->OriginalFirstThunk) continue;
        IMAGE_THUNK_DATA* names = (IMAGE_THUNK_DATA*)(base + desc->OriginalFirstThunk);
        IMAGE_THUNK_DATA* slots = (IMAGE_THUNK_DATA*)(base + desc->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++slots) {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            IMAGE_IMPORT_BY_NAME* symbol = (IMAGE_IMPORT_BY_NAME*)(base + names->u1.AddressOfData);
            if (lstrcmpA((char*)symbol->Name, "GetProcAddress") != 0) continue;
            DWORD old;
            if (VirtualProtect(&slots->u1.Function, sizeof(ULONG_PTR), PAGE_READWRITE, &old)) {
                InterlockedExchangePointer((PVOID volatile*)&slots->u1.Function, (PVOID)get_proc_hook);
                DWORD ignored; VirtualProtect(&slots->u1.Function, sizeof(ULONG_PTR), old, &ignored);
            }
        }
    }
}

static INIT_ONCE system_once = INIT_ONCE_STATIC_INIT;
static HMODULE system_winhttp;
static BOOL CALLBACK load_system(PINIT_ONCE once, PVOID parameter, PVOID* context) {
    wchar_t path[MAX_PATH];
    UINT n = GetSystemDirectoryW(path, MAX_PATH);
    if (!n || n + 13 >= MAX_PATH) return FALSE;
    lstrcatW(path, L"\\winhttp.dll");
    system_winhttp = LoadLibraryExW(path, 0, LOAD_LIBRARY_SEARCH_SYSTEM32);
    return system_winhttp != 0;
}
static FARPROC system_export(const char* name) {
    if (!InitOnceExecuteOnce(&system_once, load_system, 0, 0)) return 0;
    return GetProcAddress(system_winhttp, name);
}
#include "WinHttpProxy.h"
extern "C" FARPROC prism_resolve_winhttp(DWORD index) {
    if(index >= sizeof(prism_export_names)/sizeof(prism_export_names[0])) { TerminateProcess(GetCurrentProcess(),127); return 0; }
    FARPROC value = system_export(prism_export_names[index]);
    if(!value) { log_line("REFUSED system WinHTTP export missing"); TerminateProcess(GetCurrentProcess(),127); return 0; }
    InterlockedExchangePointer((PVOID volatile*)&prism_winhttp_exports[index],(PVOID)value);
    return value;
}
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID reserved) {
    if (reason == DLL_PROCESS_ATTACH) {
        self_module = module;
        DisableThreadLibraryCalls(module);
        install_import_hook(GetModuleHandleW(L"UnityPlayer.dll"));
    }
    return TRUE;
}
