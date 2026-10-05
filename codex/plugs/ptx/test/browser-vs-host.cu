// Build: nvcc -O2 browser-vs-host.cu -lcuda -o browser-vs-host.exe (under vcvars64).
// browser-vs-host.exe <browser.ptx> : BrowserKernels' bk-linear (f32 w) and bk-linear-h (packed f16 w),
// emitted by the PTX plug, against an f64 host y = x w^T + b on a shape that is no multiple of 64.
// Inputs are multiples of 1/64 in [-2, 2), exact in f16 and f32. Sabotage arms: kk - 1 for bk-linear,
// the f32 w buffer read as f16 for bk-linear-h; each must miss nearly every element. bk-row-rstd takes eps as a Real through the gid entry wrapper; its sabotage passes eps 1.0.
#include <cuda.h>
#include <cuda_fp16.h>
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#define CK(x) do { CUresult r_ = (x); if (r_ != CUDA_SUCCESS) { const char *s_; cuGetErrorString(r_, &s_); printf("%s failed: %s\n", #x, s_); exit(1); } } while (0)
static char *slurp(const char *p) { FILE *f = fopen(p, "rb"); fseek(f, 0, SEEK_END); long n = ftell(f); fseek(f, 0, SEEK_SET); char *b = (char *)malloc(n + 1); fread(b, 1, n, f); b[n] = 0; fclose(f); return b; }
static float wave(long long i, int seed) { return (float)((int)((i * 37 + seed) % 257) - 128) / 64.0f; }
static long long run(CUmodule m, const char *name, CUdeviceptr x, CUdeviceptr w, CUdeviceptr b, CUdeviceptr y, long long mm, long long nn, long long kk, float *hy, const double *ref) {
    CUfunction f; CK(cuModuleGetFunction(&f, m, name));
    unsigned tiles = (unsigned)(((mm + 63) / 64) * ((nn + 63) / 64)); long long px = (long long)tiles * 256;
    CK(cuMemsetD8(y, 0, mm * nn * 4));
    void *p[] = { &x, &w, &b, &y, &mm, &nn, &kk, &px };
    CK(cuLaunchKernel(f, tiles, 1, 1, 256, 1, 1, 2080 * 4, 0, p, 0)); CK(cuCtxSynchronize());
    CK(cuMemcpyDtoH(hy, y, mm * nn * 4));
    long long good = 0;
    for (long long i = 0; i < mm * nn; i++) if (fabs(hy[i] - ref[i]) <= 1e-5 * (1.0 + fabs(ref[i]))) good++;
    return good;
}
int main(int argc, char **argv) {
    CUdevice d; CUcontext c; CK(cuInit(0)); CK(cuDeviceGet(&d, 0)); CK(cuDevicePrimaryCtxRetain(&c, d)); CK(cuCtxSetCurrent(c));
    const long long mm = 137, nn = 145, kk = 70, n = mm * nn;
    float *hx = (float *)malloc(mm * kk * 4), *hw = (float *)malloc(nn * kk * 4), *hb = (float *)malloc(nn * 4), *hy = (float *)malloc(n * 4);
    __half *hh = (__half *)malloc(nn * kk * 2); double *ref = (double *)malloc(n * 8);
    for (long long i = 0; i < mm * kk; i++) hx[i] = wave(i, 5);
    for (long long i = 0; i < nn * kk; i++) { hw[i] = wave(i, 29); hh[i] = __float2half(hw[i]); }
    for (long long i = 0; i < nn; i++) hb[i] = wave(i, 71);
    for (long long r = 0; r < mm; r++) for (long long q = 0; q < nn; q++) { double s = hb[q]; for (long long k = 0; k < kk; k++) s += (double)hx[r * kk + k] * hw[q * kk + k]; ref[r * nn + q] = s; }
    CUdeviceptr x, w, wh, b, y; CK(cuMemAlloc(&x, mm * kk * 4)); CK(cuMemAlloc(&w, nn * kk * 4)); CK(cuMemAlloc(&wh, nn * kk * 2)); CK(cuMemAlloc(&b, nn * 4)); CK(cuMemAlloc(&y, n * 4));
    CK(cuMemcpyHtoD(x, hx, mm * kk * 4)); CK(cuMemcpyHtoD(w, hw, nn * kk * 4)); CK(cuMemcpyHtoD(wh, hh, nn * kk * 2)); CK(cuMemcpyHtoD(b, hb, nn * 4));
    CUmodule m; CK(cuModuleLoadData(&m, slurp(argv[1])));
    long long g1 = run(m, "bk_linear_kernel", x, w, b, y, mm, nn, kk, hy, ref);
    long long s1 = run(m, "bk_linear_kernel", x, w, b, y, mm, nn, kk - 1, hy, ref);
    long long g2 = run(m, "bk_linear_h_kernel", x, wh, b, y, mm, nn, kk, hy, ref);
    long long s2 = run(m, "bk_linear_h_kernel", x, w, b, y, mm, nn, kk, hy, ref);
    long long rows = 29, len = 300, g3 = 0, s3 = 0; float *hs = (float *)malloc(rows * 2 * 4); float *hr = (float *)malloc(rows * len * 4);
    for (long long i = 0; i < rows * len; i++) hr[i] = wave(i, 11);
    CUdeviceptr xr, sr; CK(cuMemAlloc(&xr, rows * len * 4)); CK(cuMemAlloc(&sr, rows * 2 * 4)); CK(cuMemcpyHtoD(xr, hr, rows * len * 4));
    for (int arm = 0; arm < 2; arm++) {
        double eps = arm ? 1.0 : 1e-5; CUfunction fr; CK(cuModuleGetFunction(&fr, m, "bk_row_rstd_kernel"));
        long long px = rows * 256; void *pr[] = { &xr, &sr, &len, &eps, &rows, &px };
        CK(cuMemsetD8(sr, 0, rows * 8)); CK(cuLaunchKernel(fr, (unsigned)rows, 1, 1, 256, 1, 1, 256 * 4, 0, pr, 0)); CK(cuCtxSynchronize()); CK(cuMemcpyDtoH(hs, sr, rows * 8));
        for (long long r = 0; r < rows; r++) { double s = 0, q = 0; for (long long j = 0; j < len; j++) s += hr[r * len + j]; s /= len; for (long long j = 0; j < len; j++) q += (hr[r * len + j] - s) * (hr[r * len + j] - s); double want = 1.0 / sqrt(q / len + 1e-5); if (fabs(hs[r * 2 + 1] - want) <= 1e-5 * want) { if (arm) s3++; else g3++; } }
    }
    printf("bk-row-rstd %lld rows of %lld, eps through the wrapper: %lld/%lld, sabotage eps 1.0 %lld/%lld\n", rows, len, g3, rows, s3, rows);
    printf("bk-linear %lldx%lld k %lld: %lld/%lld, sabotage kk-1 %lld/%lld\n", mm, nn, kk, g1, n, s1, n);
    printf("bk-linear-h %lldx%lld k %lld: %lld/%lld, sabotage f32 w as f16 %lld/%lld\n", mm, nn, kk, g2, n, s2, n);
    return (g1 == n && g2 == n && s1 < n / 10 && s2 < n / 10 && g3 == rows && s3 == 0) ? 0 : 1;
}
