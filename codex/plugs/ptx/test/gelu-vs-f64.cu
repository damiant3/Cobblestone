// Build: nvcc -O2 gelu-vs-f64.cu -lcuda -o gelu-vs-f64.exe (under vcvars64).
// Old and new PTX from codex/plugs/ptx/run.ps1 over LayerKernels.codex (the chapter's literal extracted to a .ptx).
// The template for grading a layer kernel against f64: swap the kernel name, arguments and reference.
// gelu.exe <old.ptx> <new.ptx> : kernel_gelu_tanh from two PTX files against f64 gelu_tanh, and timing.
#include <cuda.h>
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#define CK(x) do { CUresult r_ = (x); if (r_ != CUDA_SUCCESS) { const char *s_; cuGetErrorString(r_, &s_); printf("%s failed: %s\n", #x, s_); exit(1); } } while (0)
static char *slurp(const char *p) { FILE *f = fopen(p, "rb"); fseek(f, 0, SEEK_END); long n = ftell(f); fseek(f, 0, SEEK_SET); char *b = (char *)malloc(n + 1); fread(b, 1, n, f); b[n] = 0; fclose(f); return b; }
int main(int argc, char **argv) {
    CUdevice d; CUcontext c; CK(cuInit(0)); CK(cuDeviceGet(&d, 0)); CK(cuDevicePrimaryCtxRetain(&c, d)); CK(cuCtxSetCurrent(c));
    long long n = 4352LL * 12288; size_t bytes = (size_t)n * 4;
    float *hx = (float *)malloc(bytes), *hy = (float *)malloc(bytes);
    unsigned s = 5; for (long long i = 0; i < n; i++) { s = s * 1664525u + 1013904223u; double u = ((s >> 8) & 0xffffff) / 16777216.0; hx[i] = (float)((u * 2 - 1) * (i % 7 == 0 ? 12.0 : 4.0)); }
    CUdeviceptr x, y; CK(cuMemAlloc(&x, bytes)); CK(cuMemAlloc(&y, bytes)); CK(cuMemcpyHtoD(x, hx, bytes));
    for (int k = 0; k < 2; k++) {
        CUmodule m; CUfunction f; CK(cuModuleLoadData(&m, slurp(argv[1 + k]))); CK(cuModuleGetFunction(&f, m, "kernel_gelu_tanh"));
        void *p[] = { &x, &y, &n }; unsigned g = (unsigned)((n + 255) / 256);
        CK(cuLaunchKernel(f, g, 1, 1, 256, 1, 1, 0, 0, p, 0)); CK(cuCtxSynchronize());
        CUevent a, e; CK(cuEventCreate(&a, 0)); CK(cuEventCreate(&e, 0)); CK(cuEventRecord(a, 0));
        for (int r = 0; r < 10; r++) CK(cuLaunchKernel(f, g, 1, 1, 256, 1, 1, 0, 0, p, 0));
        CK(cuEventRecord(e, 0)); CK(cuEventSynchronize(e)); float ms; CK(cuEventElapsedTime(&ms, a, e));
        CK(cuMemcpyDtoH(hy, y, bytes));
        double worst = 0, worstabs = 0, sum = 0; long long ulp2 = 0;
        for (long long i = 0; i < n; i++) {
            double v = hx[i], u = 0.7978845608028654 * (v + 0.044715 * v * v * v), ref = 0.5 * v * (1.0 + tanh(u));
            double err = fabs(hy[i] - ref), rel = err / (fabs(ref) + 1e-30);
            if (fabs(ref) > 1e-3 && rel > worst) worst = rel;
            if (err > worstabs) worstabs = err;
            sum += rel * rel;
            if (fabs(ref) > 1e-3 && err > 2.0 * fabs(ref) * 5.96e-8) ulp2++;
        }
        printf("%s: %.3f ms per launch, worst rel %.3g (|ref| > 1e-3), worst abs %.3g, %lld of %lld beyond 2 f32 ulps\n", k ? "new" : "old", ms / 10, worst, worstabs, ulp2, n);
    }
    return 0;
}
