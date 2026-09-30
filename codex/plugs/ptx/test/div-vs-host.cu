// Build: nvcc -O2 div-vs-host.cu -lcuda -o div-vs-host.exe (under vcvars64).
// div-vs-host.exe <div-probe.ptx> : gpu_div_probe over operand pairs from both of the plug's
// divide paths (both in [0, 2^31), and a negative or wide operand), q and r against host s64.
#include <cuda.h>
#include <stdio.h>
#include <stdlib.h>
#define CK(x) do { CUresult r_ = (x); if (r_ != CUDA_SUCCESS) { const char *s_; cuGetErrorString(r_, &s_); printf("%s failed: %s\n", #x, s_); exit(1); } } while (0)
static char *slurp(const char *p) { FILE *f = fopen(p, "rb"); fseek(f, 0, SEEK_END); long n = ftell(f); fseek(f, 0, SEEK_SET); char *b = (char *)malloc(n + 1); fread(b, 1, n, f); b[n] = 0; fclose(f); return b; }
int main(int argc, char **argv) {
    CUdevice d; CUcontext c; CK(cuInit(0)); CK(cuDeviceGet(&d, 0)); CK(cuDevicePrimaryCtxRetain(&c, d)); CK(cuCtxSetCurrent(c));
    const long long n = 1 << 20;
    long long *a = (long long *)malloc(n * 8), *b = (long long *)malloc(n * 8), *q = (long long *)malloc(n * 8), *r = (long long *)malloc(n * 8);
    unsigned long long s = 99; long long fast = 0;
    for (long long i = 0; i < n; i++) {
        s = s * 6364136223846793005ULL + 1442695040888963407ULL; long long u = (long long)(s >> 1);
        s = s * 6364136223846793005ULL + 1442695040888963407ULL; long long v = (long long)(s >> 1);
        switch (i % 6) {
        case 0: a[i] = u % 2147483648LL; b[i] = 1 + v % 2147483647LL; break;          // both below 2^31
        case 1: a[i] = u % 5000; b[i] = 1 + v % 97; break;                              // small, both below 2^31
        case 2: a[i] = -(u % 2147483648LL); b[i] = 1 + v % 1000; break;                 // negative dividend
        case 3: a[i] = u % 2147483648LL; b[i] = -(1 + v % 1000); break;                 // negative divisor
        case 4: a[i] = 2147483648LL + u % 4000000000000LL; b[i] = 1 + v % 100000; break; // wide dividend
        default: a[i] = u; b[i] = 2147483648LL + v % 1000; break;                       // wide divisor
        }
        if (a[i] >= 0 && a[i] < 2147483648LL && b[i] >= 0 && b[i] < 2147483648LL) fast++;
    }
    CUdeviceptr da, db, dq, dr; CK(cuMemAlloc(&da, n * 8)); CK(cuMemAlloc(&db, n * 8)); CK(cuMemAlloc(&dq, n * 8)); CK(cuMemAlloc(&dr, n * 8));
    CK(cuMemcpyHtoD(da, a, n * 8)); CK(cuMemcpyHtoD(db, b, n * 8));
    CUmodule m; CUfunction f; CK(cuModuleLoadData(&m, slurp(argv[1]))); CK(cuModuleGetFunction(&f, m, "gpu_div_probe"));
    long long nn = n; void *p[] = { &da, &db, &dq, &dr, &nn };
    CK(cuLaunchKernel(f, (unsigned)(n / 256), 1, 1, 256, 1, 1, 0, 0, p, 0)); CK(cuCtxSynchronize());
    CK(cuMemcpyDtoH(q, dq, n * 8)); CK(cuMemcpyDtoH(r, dr, n * 8));
    long long bad = 0;
    for (long long i = 0; i < n; i++) if (q[i] != a[i] / b[i] || r[i] != a[i] % b[i]) { if (bad < 5) printf("  %lld / %lld: got q %lld r %lld\n", a[i], b[i], q[i], r[i]); bad++; }
    printf("div-probe: %lld of %lld pairs wrong (%lld on the 32-bit path, %lld on the 64-bit path)\n", bad, n, fast, n - fast);
    return bad ? 1 : 0;
}
