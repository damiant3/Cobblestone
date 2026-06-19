// gpu-dispatch.cu — cuBLAS/CUDA dispatch for the Codex GPU proxy.
// Reads a shared-memory command file, executes on GPU, writes results.
// Build: nvcc -O2 -lcublas -o gpu-dispatch.exe gpu-dispatch.cu
// Usage: gpu-dispatch.exe <shared-file> [--once]

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cmath>
#include <cuda_runtime.h>
#include <cuda.h>
#include <cublas_v2.h>

#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#endif

enum GpuStatus {
    GPU_IDLE     = 0,
    GPU_PENDING  = 1,
    GPU_COMPLETE = 2,
    GPU_ERROR    = 3
};

static const int OP_MATMUL  = 0;
static const int OP_MATVEC  = 1;
static const int OP_RELU    = 2;
static const int OP_SOFTMAX = 3;
static const int OP_CONV1D  = 4;
static const int OP_ELEMWISE_ADD = 5;
static const int OP_ELEMWISE_MUL = 6;
static const int OP_TRANSPOSE = 7;
static const int OP_LAYER_NORM = 8;
static const int OP_MAX_POOL = 9;
static const int OP_GELU = 10;
static const int OP_SCALE = 11;
static const int OP_CONV2D = 12;
static const int OP_GROUP_NORM = 13;
static const int OP_SILU = 14;
static const int OP_UPSAMPLE2X = 15;
static const int OP_CLAMP = 16;
static const int OP_LAUNCH_PTX = 32;

static const int HEADER_SIZE = 256;
static const int BUF_SIZE = 1048576; // 1 MB

struct CmdHeader {
    uint32_t status;
    uint32_t op;
    uint32_t rows_a;
    uint32_t cols_a;
    uint32_t cols_b;
    uint32_t off_a;
    uint32_t off_b;
    uint32_t off_out;
    uint32_t result_rows;
    uint32_t result_cols;
};

static uint32_t read_u32(const uint8_t* buf, int off) {
    return buf[off] | (buf[off+1] << 8) | (buf[off+2] << 16) | (buf[off+3] << 24);
}

static void write_u32(uint8_t* buf, int off, uint32_t v) {
    buf[off]   = v & 0xFF;
    buf[off+1] = (v >> 8) & 0xFF;
    buf[off+2] = (v >> 16) & 0xFF;
    buf[off+3] = (v >> 24) & 0xFF;
}

static CmdHeader read_header(const uint8_t* buf) {
    CmdHeader h;
    h.status  = read_u32(buf, 0);
    h.op      = read_u32(buf, 4);
    h.rows_a  = read_u32(buf, 8);
    h.cols_a  = read_u32(buf, 12);
    h.cols_b  = read_u32(buf, 16);
    h.off_a   = read_u32(buf, 20);
    h.off_b   = read_u32(buf, 24);
    h.off_out = read_u32(buf, 28);
    return h;
}

static float* buf_to_floats(const uint8_t* buf, uint32_t offset, int count) {
    float* out = (float*)malloc(count * sizeof(float));
    memcpy(out, buf + offset, count * sizeof(float));
    return out;
}

static void floats_to_buf(uint8_t* buf, uint32_t offset, const float* data, int count) {
    memcpy(buf + offset, data, count * sizeof(float));
}

__global__ void relu_kernel(const float* in, float* out, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) out[i] = in[i] > 0.0f ? in[i] : 0.0f;
}

__global__ void softmax_max_kernel(const float* in, float* max_val, int n) {
    float m = -1e30f;
    for (int i = 0; i < n; i++) {
        if (in[i] > m) m = in[i];
    }
    *max_val = m;
}

__global__ void softmax_exp_kernel(const float* in, float* out, const float* max_val, float* sum, int n) {
    *sum = 0.0f;
    for (int i = 0; i < n; i++) {
        out[i] = expf(in[i] - *max_val);
        *sum += out[i];
    }
}

__global__ void softmax_div_kernel(float* out, const float* sum, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n && *sum > 0.0f) out[i] /= *sum;
}

__global__ void elemwise_add_kernel(const float* a, const float* b, float* out, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) out[i] = a[i] + b[i];
}

__global__ void elemwise_mul_kernel(const float* a, const float* b, float* out, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) out[i] = a[i] * b[i];
}

__global__ void gelu_kernel(const float* in, float* out, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) {
        float x = in[i];
        out[i] = 0.5f * x * (1.0f + tanhf(0.7978845608f * (x + 0.044715f * x * x * x)));
    }
}

__global__ void scale_kernel(const float* in, float* out, float scale, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) out[i] = in[i] * scale;
}

__global__ void transpose_kernel(const float* in, float* out, int rows, int cols) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < rows * cols) {
        int r = i / cols;
        int c = i - r * cols;
        out[c * rows + r] = in[i];
    }
}

__global__ void layer_norm_kernel(const float* in, float* out, int n, float eps) {
    float mean = 0.0f;
    for (int i = 0; i < n; i++) mean += in[i];
    mean /= n;
    float var = 0.0f;
    for (int i = 0; i < n; i++) { float d = in[i] - mean; var += d * d; }
    var /= n;
    float inv_std = 1.0f / sqrtf(var + eps);
    for (int i = 0; i < n; i++) out[i] = (in[i] - mean) * inv_std;
}

__global__ void max_pool_kernel(const float* in, float* out, int in_len, int pool_size) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    int out_len = in_len / pool_size;
    if (i < out_len) {
        float mx = -1e30f;
        for (int j = 0; j < pool_size; j++) {
            float v = in[i * pool_size + j];
            if (v > mx) mx = v;
        }
        out[i] = mx;
    }
}

__global__ void silu_kernel(const float* in, float* out, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) out[i] = in[i] / (1.0f + expf(-in[i]));
}

__global__ void clamp_kernel(const float* in, float* out, float lo, float hi, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) {
        float v = in[i];
        out[i] = v < lo ? lo : (v > hi ? hi : v);
    }
}

__global__ void upsample2x_kernel(const float* in, float* out, int w, int h) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    int out_w = w * 2, out_h = h * 2;
    if (i < out_w * out_h) {
        int oy = i / out_w;
        int ox = i - oy * out_w;
        int sx = ox / 2;
        int sy = oy / 2;
        out[i] = in[sy * w + sx];
    }
}

__global__ void conv2d_kernel(const float* in, const float* kernel, float* out,
                               int in_w, int in_h, int k_size, int out_w, int out_h) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < out_w * out_h) {
        int oy = i / out_w;
        int ox = i - oy * out_w;
        float sum = 0.0f;
        for (int ky = 0; ky < k_size; ky++) {
            for (int kx = 0; kx < k_size; kx++) {
                sum += in[(oy + ky) * in_w + (ox + kx)] * kernel[ky * k_size + kx];
            }
        }
        out[i] = sum;
    }
}

__global__ void group_norm_kernel(const float* in, float* out, int group_size, int num_groups, float eps) {
    int g = blockIdx.x;
    if (g < num_groups) {
        int offset = g * group_size;
        float mean = 0.0f;
        for (int i = 0; i < group_size; i++) mean += in[offset + i];
        mean /= group_size;
        float var = 0.0f;
        for (int i = 0; i < group_size; i++) { float d = in[offset + i] - mean; var += d * d; }
        var /= group_size;
        float inv_std = 1.0f / sqrtf(var + eps);
        for (int i = 0; i < group_size; i++) out[offset + i] = (in[offset + i] - mean) * inv_std;
    }
}

static bool do_silu(uint8_t* buf, const CmdHeader& h) {
    int n = h.rows_a * h.cols_a;
    float* h_in = buf_to_floats(buf, h.off_a, n);
    float *d_in, *d_out;
    cudaMalloc(&d_in, n * sizeof(float)); cudaMalloc(&d_out, n * sizeof(float));
    cudaMemcpy(d_in, h_in, n * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (n + threads - 1) / threads;
    silu_kernel<<<blocks, threads>>>(d_in, d_out, n);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(n * sizeof(float));
    cudaMemcpy(h_out, d_out, n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, n);
    write_u32(buf, 32, h.rows_a); write_u32(buf, 36, h.cols_a);
    free(h_in); free(h_out); cudaFree(d_in); cudaFree(d_out);
    return true;
}

static bool do_conv2d(uint8_t* buf, const CmdHeader& h) {
    int in_w = h.rows_a, in_h = h.cols_a, k_size = h.cols_b;
    int out_w = in_w - k_size + 1, out_h = in_h - k_size + 1;
    if (out_w <= 0 || out_h <= 0) return false;
    int in_n = in_w * in_h, k_n = k_size * k_size, out_n = out_w * out_h;
    float* h_in = buf_to_floats(buf, h.off_a, in_n);
    float* h_k = buf_to_floats(buf, h.off_b, k_n);
    float *d_in, *d_k, *d_out;
    cudaMalloc(&d_in, in_n * sizeof(float)); cudaMalloc(&d_k, k_n * sizeof(float));
    cudaMalloc(&d_out, out_n * sizeof(float));
    cudaMemcpy(d_in, h_in, in_n * sizeof(float), cudaMemcpyHostToDevice);
    cudaMemcpy(d_k, h_k, k_n * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (out_n + threads - 1) / threads;
    conv2d_kernel<<<blocks, threads>>>(d_in, d_k, d_out, in_w, in_h, k_size, out_w, out_h);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(out_n * sizeof(float));
    cudaMemcpy(h_out, d_out, out_n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, out_n);
    write_u32(buf, 32, out_w); write_u32(buf, 36, out_h);
    free(h_in); free(h_k); free(h_out);
    cudaFree(d_in); cudaFree(d_k); cudaFree(d_out);
    return true;
}

static bool do_group_norm(uint8_t* buf, const CmdHeader& h) {
    int total = h.rows_a;
    int num_groups = h.cols_a;
    int group_size = total / num_groups;
    float* h_in = buf_to_floats(buf, h.off_a, total);
    float *d_in, *d_out;
    cudaMalloc(&d_in, total * sizeof(float)); cudaMalloc(&d_out, total * sizeof(float));
    cudaMemcpy(d_in, h_in, total * sizeof(float), cudaMemcpyHostToDevice);
    group_norm_kernel<<<num_groups, 1>>>(d_in, d_out, group_size, num_groups, 1e-5f);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(total * sizeof(float));
    cudaMemcpy(h_out, d_out, total * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, total);
    write_u32(buf, 32, total); write_u32(buf, 36, 1);
    free(h_in); free(h_out); cudaFree(d_in); cudaFree(d_out);
    return true;
}

static bool do_upsample2x(uint8_t* buf, const CmdHeader& h) {
    int w = h.rows_a, ht = h.cols_a;
    int out_w = w * 2, out_h = ht * 2, out_n = out_w * out_h;
    float* h_in = buf_to_floats(buf, h.off_a, w * ht);
    float *d_in, *d_out;
    cudaMalloc(&d_in, w * ht * sizeof(float)); cudaMalloc(&d_out, out_n * sizeof(float));
    cudaMemcpy(d_in, h_in, w * ht * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (out_n + threads - 1) / threads;
    upsample2x_kernel<<<blocks, threads>>>(d_in, d_out, w, ht);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(out_n * sizeof(float));
    cudaMemcpy(h_out, d_out, out_n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, out_n);
    write_u32(buf, 32, out_w); write_u32(buf, 36, out_h);
    free(h_in); free(h_out); cudaFree(d_in); cudaFree(d_out);
    return true;
}

static bool do_clamp(uint8_t* buf, const CmdHeader& h) {
    int n = h.rows_a * h.cols_a;
    float lo = -1.0f, hi = 1.0f; // default clamp range
    float* h_in = buf_to_floats(buf, h.off_a, n);
    float *d_in, *d_out;
    cudaMalloc(&d_in, n * sizeof(float)); cudaMalloc(&d_out, n * sizeof(float));
    cudaMemcpy(d_in, h_in, n * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (n + threads - 1) / threads;
    clamp_kernel<<<blocks, threads>>>(d_in, d_out, lo, hi, n);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(n * sizeof(float));
    cudaMemcpy(h_out, d_out, n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, n);
    write_u32(buf, 32, h.rows_a); write_u32(buf, 36, h.cols_a);
    free(h_in); free(h_out); cudaFree(d_in); cudaFree(d_out);
    return true;
}

static bool do_elemwise(uint8_t* buf, const CmdHeader& h, bool is_add) {
    int n = h.rows_a * h.cols_a;
    float* h_a = buf_to_floats(buf, h.off_a, n);
    float* h_b = buf_to_floats(buf, h.off_b, n);
    float *d_a, *d_b, *d_out;
    cudaMalloc(&d_a, n * sizeof(float));
    cudaMalloc(&d_b, n * sizeof(float));
    cudaMalloc(&d_out, n * sizeof(float));
    cudaMemcpy(d_a, h_a, n * sizeof(float), cudaMemcpyHostToDevice);
    cudaMemcpy(d_b, h_b, n * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (n + threads - 1) / threads;
    if (is_add) elemwise_add_kernel<<<blocks, threads>>>(d_a, d_b, d_out, n);
    else elemwise_mul_kernel<<<blocks, threads>>>(d_a, d_b, d_out, n);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(n * sizeof(float));
    cudaMemcpy(h_out, d_out, n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, n);
    write_u32(buf, 32, h.rows_a); write_u32(buf, 36, h.cols_a);
    free(h_a); free(h_b); free(h_out);
    cudaFree(d_a); cudaFree(d_b); cudaFree(d_out);
    return true;
}

static bool do_gelu(uint8_t* buf, const CmdHeader& h) {
    int n = h.rows_a * h.cols_a;
    float* h_in = buf_to_floats(buf, h.off_a, n);
    float *d_in, *d_out;
    cudaMalloc(&d_in, n * sizeof(float)); cudaMalloc(&d_out, n * sizeof(float));
    cudaMemcpy(d_in, h_in, n * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (n + threads - 1) / threads;
    gelu_kernel<<<blocks, threads>>>(d_in, d_out, n);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(n * sizeof(float));
    cudaMemcpy(h_out, d_out, n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, n);
    write_u32(buf, 32, h.rows_a); write_u32(buf, 36, h.cols_a);
    free(h_in); free(h_out); cudaFree(d_in); cudaFree(d_out);
    return true;
}

static bool do_transpose(uint8_t* buf, const CmdHeader& h) {
    int rows = h.rows_a, cols = h.cols_a, n = rows * cols;
    float* h_in = buf_to_floats(buf, h.off_a, n);
    float *d_in, *d_out;
    cudaMalloc(&d_in, n * sizeof(float)); cudaMalloc(&d_out, n * sizeof(float));
    cudaMemcpy(d_in, h_in, n * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (n + threads - 1) / threads;
    transpose_kernel<<<blocks, threads>>>(d_in, d_out, rows, cols);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(n * sizeof(float));
    cudaMemcpy(h_out, d_out, n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, n);
    write_u32(buf, 32, cols); write_u32(buf, 36, rows);
    free(h_in); free(h_out); cudaFree(d_in); cudaFree(d_out);
    return true;
}

static bool do_layer_norm(uint8_t* buf, const CmdHeader& h) {
    int n = h.rows_a;
    float* h_in = buf_to_floats(buf, h.off_a, n);
    float *d_in, *d_out;
    cudaMalloc(&d_in, n * sizeof(float)); cudaMalloc(&d_out, n * sizeof(float));
    cudaMemcpy(d_in, h_in, n * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (n + threads - 1) / threads;
    layer_norm_kernel<<<1, 1>>>(d_in, d_out, n, 1e-5f);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(n * sizeof(float));
    cudaMemcpy(h_out, d_out, n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, n);
    write_u32(buf, 32, n); write_u32(buf, 36, 1);
    free(h_in); free(h_out); cudaFree(d_in); cudaFree(d_out);
    return true;
}

static bool do_max_pool(uint8_t* buf, const CmdHeader& h) {
    int in_len = h.rows_a, pool_size = h.cols_a;
    int out_len = in_len / pool_size;
    float* h_in = buf_to_floats(buf, h.off_a, in_len);
    float *d_in, *d_out;
    cudaMalloc(&d_in, in_len * sizeof(float)); cudaMalloc(&d_out, out_len * sizeof(float));
    cudaMemcpy(d_in, h_in, in_len * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (out_len + threads - 1) / threads;
    max_pool_kernel<<<blocks, threads>>>(d_in, d_out, in_len, pool_size);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(out_len * sizeof(float));
    cudaMemcpy(h_out, d_out, out_len * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, out_len);
    write_u32(buf, 32, out_len); write_u32(buf, 36, 1);
    free(h_in); free(h_out); cudaFree(d_in); cudaFree(d_out);
    return true;
}

static bool do_scale(uint8_t* buf, const CmdHeader& h) {
    int n = h.rows_a * h.cols_a;
    float scale_val = *((float*)(buf + h.off_b));
    float* h_in = buf_to_floats(buf, h.off_a, n);
    float *d_in, *d_out;
    cudaMalloc(&d_in, n * sizeof(float)); cudaMalloc(&d_out, n * sizeof(float));
    cudaMemcpy(d_in, h_in, n * sizeof(float), cudaMemcpyHostToDevice);
    int threads = 256, blocks = (n + threads - 1) / threads;
    scale_kernel<<<blocks, threads>>>(d_in, d_out, scale_val, n);
    cudaDeviceSynchronize();
    float* h_out = (float*)malloc(n * sizeof(float));
    cudaMemcpy(h_out, d_out, n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, n);
    write_u32(buf, 32, h.rows_a); write_u32(buf, 36, h.cols_a);
    free(h_in); free(h_out); cudaFree(d_in); cudaFree(d_out);
    return true;
}

static bool do_matmul(uint8_t* buf, const CmdHeader& h, cublasHandle_t cublas) {
    int M = h.rows_a, K = h.cols_a, N = h.cols_b;
    if (N == 0) N = 1; // matvec

    float* h_a = buf_to_floats(buf, h.off_a, M * K);
    float* h_b = buf_to_floats(buf, h.off_b, K * N);
    float* h_c = (float*)calloc(M * N, sizeof(float));

    float *d_a, *d_b, *d_c;
    cudaMalloc(&d_a, M * K * sizeof(float));
    cudaMalloc(&d_b, K * N * sizeof(float));
    cudaMalloc(&d_c, M * N * sizeof(float));

    // cuBLAS uses column-major. Our data is row-major.
    // C = A * B  (row-major) is equivalent to C^T = B^T * A^T (col-major).
    // So we call sgemm with swapped A/B and transposed dimensions.
    cudaMemcpy(d_a, h_a, M * K * sizeof(float), cudaMemcpyHostToDevice);
    cudaMemcpy(d_b, h_b, K * N * sizeof(float), cudaMemcpyHostToDevice);

    float alpha = 1.0f, beta = 0.0f;
    // sgemm: C = alpha * op(A) * op(B) + beta * C
    // For row-major: treat as col-major with B^T * A^T = C^T
    cublasStatus_t stat = cublasSgemm(cublas,
        CUBLAS_OP_N, CUBLAS_OP_N,
        N, M, K,
        &alpha,
        d_b, N,   // B treated as col-major N x K
        d_a, K,   // A treated as col-major K x M
        &beta,
        d_c, N);  // C is col-major N x M = row-major M x N

    if (stat != CUBLAS_STATUS_SUCCESS) {
        fprintf(stderr, "cuBLAS sgemm failed: %d\n", stat);
        free(h_a); free(h_b); free(h_c);
        cudaFree(d_a); cudaFree(d_b); cudaFree(d_c);
        return false;
    }

    cudaMemcpy(h_c, d_c, M * N * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_c, M * N);
    write_u32(buf, 32, M);
    write_u32(buf, 36, N);

    free(h_a); free(h_b); free(h_c);
    cudaFree(d_a); cudaFree(d_b); cudaFree(d_c);
    return true;
}

static bool do_relu(uint8_t* buf, const CmdHeader& h) {
    int n = h.rows_a * h.cols_a;
    float* h_in = buf_to_floats(buf, h.off_a, n);

    float *d_in, *d_out;
    cudaMalloc(&d_in, n * sizeof(float));
    cudaMalloc(&d_out, n * sizeof(float));
    cudaMemcpy(d_in, h_in, n * sizeof(float), cudaMemcpyHostToDevice);

    int threads = 256;
    int blocks = (n + threads - 1) / threads;
    relu_kernel<<<blocks, threads>>>(d_in, d_out, n);
    cudaDeviceSynchronize();

    float* h_out = (float*)malloc(n * sizeof(float));
    cudaMemcpy(h_out, d_out, n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, n);
    write_u32(buf, 32, h.rows_a);
    write_u32(buf, 36, h.cols_a);

    free(h_in); free(h_out);
    cudaFree(d_in); cudaFree(d_out);
    return true;
}

__global__ void conv1d_kernel(const float* input, const float* kernel_data, float* output,
                              int in_len, int k_len, int out_len) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < out_len) {
        float sum = 0.0f;
        for (int j = 0; j < k_len; j++) {
            sum += input[i + j] * kernel_data[j];
        }
        output[i] = sum;
    }
}

static bool do_conv1d(uint8_t* buf, const CmdHeader& h) {
    int in_len = h.rows_a;
    int k_len = h.cols_a;
    int out_len = in_len - k_len + 1;
    if (out_len <= 0) return false;

    float* h_in = buf_to_floats(buf, h.off_a, in_len);
    float* h_k = buf_to_floats(buf, h.off_b, k_len);

    float *d_in, *d_k, *d_out;
    cudaMalloc(&d_in, in_len * sizeof(float));
    cudaMalloc(&d_k, k_len * sizeof(float));
    cudaMalloc(&d_out, out_len * sizeof(float));
    cudaMemcpy(d_in, h_in, in_len * sizeof(float), cudaMemcpyHostToDevice);
    cudaMemcpy(d_k, h_k, k_len * sizeof(float), cudaMemcpyHostToDevice);

    int threads = 256;
    int blocks = (out_len + threads - 1) / threads;
    conv1d_kernel<<<blocks, threads>>>(d_in, d_k, d_out, in_len, k_len, out_len);
    cudaDeviceSynchronize();

    float* h_out = (float*)malloc(out_len * sizeof(float));
    cudaMemcpy(h_out, d_out, out_len * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, out_len);
    write_u32(buf, 32, out_len);
    write_u32(buf, 36, 1);

    free(h_in); free(h_k); free(h_out);
    cudaFree(d_in); cudaFree(d_k); cudaFree(d_out);
    return true;
}

static bool do_softmax(uint8_t* buf, const CmdHeader& h) {
    int n = h.rows_a;
    float* h_in = buf_to_floats(buf, h.off_a, n);

    float *d_in, *d_out, *d_max, *d_sum;
    cudaMalloc(&d_in, n * sizeof(float));
    cudaMalloc(&d_out, n * sizeof(float));
    cudaMalloc(&d_max, sizeof(float));
    cudaMalloc(&d_sum, sizeof(float));
    cudaMemcpy(d_in, h_in, n * sizeof(float), cudaMemcpyHostToDevice);

    softmax_max_kernel<<<1, 1>>>(d_in, d_max, n);
    softmax_exp_kernel<<<1, 1>>>(d_in, d_out, d_max, d_sum, n);
    int threads = 256;
    int blocks = (n + threads - 1) / threads;
    softmax_div_kernel<<<blocks, threads>>>(d_out, d_sum, n);
    cudaDeviceSynchronize();

    float* h_out = (float*)malloc(n * sizeof(float));
    cudaMemcpy(h_out, d_out, n * sizeof(float), cudaMemcpyDeviceToHost);
    floats_to_buf(buf, h.off_out, h_out, n);
    write_u32(buf, 32, n);
    write_u32(buf, 36, 1);

    free(h_in); free(h_out);
    cudaFree(d_in); cudaFree(d_out); cudaFree(d_max); cudaFree(d_sum);
    return true;
}

static bool do_launch_ptx(uint8_t* buf, const CmdHeader& h) {
    uint32_t name_len = read_u32(buf, 64);
    uint32_t ptx_len = read_u32(buf, 68);
    uint32_t sm_target = read_u32(buf, 72);
    uint32_t grid_x = read_u32(buf, 76);
    uint32_t grid_y = read_u32(buf, 80);
    uint32_t grid_z = read_u32(buf, 84);
    uint32_t block_x = read_u32(buf, 88);
    uint32_t block_y = read_u32(buf, 92);
    uint32_t block_z = read_u32(buf, 96);
    uint32_t shared_bytes = read_u32(buf, 100);
    uint32_t arg_count = read_u32(buf, 104);

    if (name_len == 0 || ptx_len == 0 || name_len > 256 || ptx_len > 65536) {
        fprintf(stderr, "  PTX launch: invalid name_len=%u ptx_len=%u\n", name_len, ptx_len);
        return false;
    }

    char kernel_name[257];
    memcpy(kernel_name, buf + HEADER_SIZE, name_len);
    kernel_name[name_len] = '\0';

    const char* ptx_src = (const char*)(buf + HEADER_SIZE + name_len);

    CUmodule module;
    CUresult res = cuModuleLoadData(&module, ptx_src);
    if (res != CUDA_SUCCESS) {
        const char* err_str;
        cuGetErrorString(res, &err_str);
        fprintf(stderr, "  PTX load failed: %s\n", err_str);
        return false;
    }

    CUfunction func;
    res = cuModuleGetFunction(&func, module, kernel_name);
    if (res != CUDA_SUCCESS) {
        fprintf(stderr, "  kernel '%s' not found in PTX module\n", kernel_name);
        cuModuleUnload(module);
        return false;
    }

    res = cuLaunchKernel(func, grid_x, grid_y, grid_z,
                         block_x, block_y, block_z,
                         shared_bytes, NULL, NULL, NULL);
    if (res != CUDA_SUCCESS) {
        const char* err_str;
        cuGetErrorString(res, &err_str);
        fprintf(stderr, "  cuLaunchKernel failed: %s\n", err_str);
        cuModuleUnload(module);
        return false;
    }

    cuCtxSynchronize();
    printf("  PTX kernel '%s' launched (%ux%ux%u blocks, %ux%ux%u threads)\n",
           kernel_name, grid_x, grid_y, grid_z, block_x, block_y, block_z);

    write_u32(buf, 32, 1);
    write_u32(buf, 36, 1);
    cuModuleUnload(module);
    return true;
}

static const char* op_name(uint32_t op) {
    switch (op) {
        case 0: return "MATMUL";
        case 1: return "MATVEC";
        case 2: return "RELU";
        case 3: return "SOFTMAX";
        case 4: return "CONV1D";
        case 5: return "ELEM_ADD";
        case 6: return "ELEM_MUL";
        case 7: return "TRANSPOSE";
        case 8: return "LAYER_NORM";
        case 9: return "MAX_POOL";
        case 10: return "GELU";
        case 11: return "SCALE";
        case 12: return "CONV2D";
        case 13: return "GROUP_NORM";
        case 14: return "SILU";
        case 15: return "UPSAMPLE2X";
        case 16: return "CLAMP";
        case 32: return "LAUNCH_PTX";
        default: return "UNKNOWN";
    }
}

static bool read_file(const char* path, uint8_t* buf, int size) {
    FILE* f = fopen(path, "rb");
    if (!f) return false;
    size_t n = fread(buf, 1, size, f);
    fclose(f);
    return n > 0;
}

static bool write_file(const char* path, const uint8_t* buf, int size) {
    FILE* f = fopen(path, "wb");
    if (!f) return false;
    fwrite(buf, 1, size, f);
    fclose(f);
    return true;
}

int main(int argc, char** argv) {
    if (argc < 2) {
        fprintf(stderr, "Usage: gpu-dispatch.exe <shared-file> [--once]\n");
        return 1;
    }

    const char* shared_file = argv[1];
    bool once = (argc >= 3 && strcmp(argv[2], "--once") == 0);
    int poll_ms = 10;

    int dev_count = 0;
    cudaGetDeviceCount(&dev_count);
    if (dev_count == 0) {
        fprintf(stderr, "No CUDA devices found\n");
        return 1;
    }

    cudaDeviceProp prop;
    cudaGetDeviceProperties(&prop, 0);
    printf("GPU Dispatch: %s (compute %d.%d, %d MB)\n",
        prop.name, prop.major, prop.minor,
        (int)(prop.totalGlobalMem / (1024 * 1024)));

    cublasHandle_t cublas;
    cublasCreate(&cublas);

    uint8_t* buf = (uint8_t*)malloc(BUF_SIZE);
    int dispatched = 0;

    printf("Watching: %s (poll %dms)%s\n", shared_file, poll_ms, once ? " [once]" : "");

    while (true) {
        if (!read_file(shared_file, buf, BUF_SIZE)) {
            Sleep(poll_ms);
            continue;
        }

        uint32_t status = read_u32(buf, 0);
        if (status != GPU_PENDING) {
            if (once && dispatched > 0) break;
            Sleep(poll_ms);
            continue;
        }

        CmdHeader h = read_header(buf);
        dispatched++;
        printf("[%d] dispatch: %s %ux%u", dispatched, op_name(h.op), h.rows_a, h.cols_a);
        if (h.cols_b > 0) printf(" * _x%u", h.cols_b);

        bool ok = false;
        switch (h.op) {
            case OP_MATMUL:
            case OP_MATVEC:
                ok = do_matmul(buf, h, cublas);
                break;
            case OP_RELU:
                ok = do_relu(buf, h);
                break;
            case OP_SOFTMAX:
                ok = do_softmax(buf, h);
                break;
            case OP_CONV1D:
                ok = do_conv1d(buf, h);
                break;
            case OP_ELEMWISE_ADD:
                ok = do_elemwise(buf, h, true);
                break;
            case OP_ELEMWISE_MUL:
                ok = do_elemwise(buf, h, false);
                break;
            case OP_TRANSPOSE:
                ok = do_transpose(buf, h);
                break;
            case OP_LAYER_NORM:
                ok = do_layer_norm(buf, h);
                break;
            case OP_MAX_POOL:
                ok = do_max_pool(buf, h);
                break;
            case OP_GELU:
                ok = do_gelu(buf, h);
                break;
            case OP_SCALE:
                ok = do_scale(buf, h);
                break;
            case OP_CONV2D:
                ok = do_conv2d(buf, h);
                break;
            case OP_GROUP_NORM:
                ok = do_group_norm(buf, h);
                break;
            case OP_SILU:
                ok = do_silu(buf, h);
                break;
            case OP_UPSAMPLE2X:
                ok = do_upsample2x(buf, h);
                break;
            case OP_CLAMP:
                ok = do_clamp(buf, h);
                break;
            case OP_LAUNCH_PTX:
                ok = do_launch_ptx(buf, h);
                break;
            default:
                fprintf(stderr, " UNSUPPORTED\n");
                break;
        }

        if (ok) {
            printf(" -> %ux%u\n", read_u32(buf, 32), read_u32(buf, 36));
            write_u32(buf, 0, GPU_COMPLETE);
        } else {
            write_u32(buf, 0, GPU_ERROR);
        }

        write_file(shared_file, buf, BUF_SIZE);

        if (once) break;
    }

    printf("\nGPU Dispatch stopped. Dispatched: %d\n", dispatched);

    cublasDestroy(cublas);
    free(buf);
    return 0;
}
