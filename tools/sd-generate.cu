/* sd-generate.cu -- SD 1.5 inference PoC. Build: nvcc -O2 -lcublas -o sd-generate.exe sd-generate.cu */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <math.h>
#include <cuda_runtime.h>
#include <cuda_fp16.h>
#include <cublas_v2.h>
/* ── Constants ── */
#define MODEL_PATH "D:\\AI\\stable-diffusion-webui\\models\\Stable-diffusion\\realisticVisionV60B1_v20Novae.safetensors"
#define OUTPUT_PATH "D:\\Projects\\NewRepository-val\\build\\output\\duck.bmp"
#define PROMPT "a yellow rubber duck floating on a pond, photorealistic"

#define IMG_W 512
#define IMG_H 512
#define LAT_W 64   /* IMG_W / 8 */
#define LAT_H 64   /* IMG_H / 8 */
#define LAT_C 4    /* latent channels */
#define LAT_SZ (LAT_C * LAT_H * LAT_W)

#define CLIP_DIM 768
#define CLIP_LAYERS 12
#define CLIP_HEADS 12
#define CLIP_SEQ 77
#define CLIP_VOCAB 49408
#define CLIP_MLP (CLIP_DIM * 4) /* 3072 */

#define DDIM_STEPS 10
#define CFG_SCALE 7.5f

#define MAX_TENSORS 4096
#define MAX_KEY_LEN 256

/* ── CUDA ── */
#define CU(x) do { cudaError_t e = (x); if (e) { fprintf(stderr, "CUDA %s:%d: %s\n", __FILE__, __LINE__, cudaGetErrorString(e)); exit(1); } } while(0)
#define CUBLAS(x) do { cublasStatus_t s = (x); if (s) { fprintf(stderr, "cuBLAS %s:%d: status %d\n", __FILE__, __LINE__, s); exit(1); } } while(0)

static cublasHandle_t g_cublas;

/* ── SafeTensors ── */
typedef struct {
    char key[MAX_KEY_LEN];
    int dtype;          /* 0=F16, 1=F32, 2=I64 */
    int ndim;
    int64_t shape[8];
    int64_t off_start;
    int64_t off_end;
} TensorMeta;

static TensorMeta g_meta[MAX_TENSORS];
static int g_nmeta;
static uint8_t *g_data;         /* mmap'd tensor data region */
static int64_t g_data_offset;   /* file offset where tensor data starts */
static FILE *g_model_fp;

/* Minimal JSON parser -- good enough for SafeTensors headers */
static const char *skip_ws(const char *p) { while (*p==' '||*p=='\t'||*p=='\n'||*p=='\r') p++; return p; }

static const char *parse_string(const char *p, char *out, int maxlen) {
    p = skip_ws(p);
    if (*p != '"') return NULL;
    p++;
    int i = 0;
    while (*p && *p != '"' && i < maxlen - 1) {
        if (*p == '\\') { p++; if (*p) out[i++] = *p++; }
        else out[i++] = *p++;
    }
    out[i] = 0;
    if (*p == '"') p++;
    return p;
}

static const char *parse_int64(const char *p, int64_t *out) {
    p = skip_ws(p);
    char buf[32]; int i = 0;
    if (*p == '-') buf[i++] = *p++;
    while (*p >= '0' && *p <= '9' && i < 30) buf[i++] = *p++;
    buf[i] = 0;
    *out = strtoll(buf, NULL, 10);
    return p;
}

static void parse_header(const char *json, int64_t len) {
    const char *p = skip_ws(json);
    if (*p != '{') return;
    p++;
    while (*p && *p != '}') {
        p = skip_ws(p);
        if (*p == '}') break;
        /* key */
        char key[MAX_KEY_LEN];
        p = parse_string(p, key, MAX_KEY_LEN);
        if (!p) break;
        p = skip_ws(p);
        if (*p == ':') p++;
        p = skip_ws(p);
        /* Skip __metadata__ */
        if (strcmp(key, "__metadata__") == 0) {
            int depth = 0;
            while (*p) {
                if (*p == '{') depth++;
                else if (*p == '}') { depth--; if (depth == 0) { p++; break; } }
                p++;
            }
            if (*p == ',') p++;
            continue;
        }
        /* value: { "dtype": ..., "shape": [...], "data_offsets": [s, e] } */
        if (*p != '{') break;
        p++;
        TensorMeta *m = &g_meta[g_nmeta];
        strncpy(m->key, key, MAX_KEY_LEN - 1);
        m->ndim = 0;
        while (*p && *p != '}') {
            p = skip_ws(p);
            char field[64];
            p = parse_string(p, field, 64);
            if (!p) break;
            p = skip_ws(p); if (*p == ':') p++; p = skip_ws(p);
            if (strcmp(field, "dtype") == 0) {
                char dt[16];
                p = parse_string(p, dt, 16);
                if (strcmp(dt, "F16") == 0) m->dtype = 0;
                else if (strcmp(dt, "F32") == 0) m->dtype = 1;
                else m->dtype = 2; /* I64 or other */
            } else if (strcmp(field, "shape") == 0) {
                if (*p == '[') { p++;
                    while (*p && *p != ']') {
                        p = skip_ws(p);
                        if (*p == ']') break;
                        int64_t v;
                        p = parse_int64(p, &v);
                        m->shape[m->ndim++] = v;
                        p = skip_ws(p);
                        if (*p == ',') p++;
                    }
                    if (*p == ']') p++;
                }
            } else if (strcmp(field, "data_offsets") == 0) {
                if (*p == '[') { p++;
                    p = parse_int64(skip_ws(p), &m->off_start);
                    p = skip_ws(p); if (*p == ',') p++;
                    p = parse_int64(skip_ws(p), &m->off_end);
                    p = skip_ws(p); if (*p == ']') p++;
                }
            }
            p = skip_ws(p);
            if (*p == ',') p++;
        }
        if (*p == '}') p++;
        g_nmeta++;
        if (g_nmeta >= MAX_TENSORS) break;
        p = skip_ws(p);
        if (*p == ',') p++;
    }
}

static TensorMeta *find_tensor(const char *key) {
    for (int i = 0; i < g_nmeta; i++)
        if (strcmp(g_meta[i].key, key) == 0) return &g_meta[i];
    return NULL;
}

static int64_t tensor_numel(TensorMeta *m) {
    int64_t n = 1;
    for (int i = 0; i < m->ndim; i++) n *= m->shape[i];
    return n;
}

static float f16_to_f32(uint16_t hv) {
    uint32_t sign = (hv >> 15) & 1, exp = (hv >> 10) & 0x1F, mant = hv & 0x3FF, fv;
    if (exp == 0) {
        if (mant == 0) fv = sign << 31;
        else { exp = 1; while (!(mant & 0x400)) { mant <<= 1; exp--; }
            mant &= 0x3FF; fv = (sign << 31) | ((exp + 112) << 23) | (mant << 13); }
    } else if (exp == 31) fv = (sign << 31) | 0x7F800000 | (mant << 13);
    else fv = (sign << 31) | ((exp + 112) << 23) | (mant << 13);
    float r; memcpy(&r, &fv, 4); return r;
}

static float *load_f32(const char *key) {
    TensorMeta *m = find_tensor(key);
    if (!m) { fprintf(stderr, "Tensor not found: %s\n", key); exit(1); }
    int64_t n = tensor_numel(m), bytes = m->off_end - m->off_start;
    uint8_t *raw = (uint8_t *)malloc(bytes);
    fseek(g_model_fp, (long)(g_data_offset + m->off_start), SEEK_SET);
    fread(raw, 1, bytes, g_model_fp);
    float *host = (float *)malloc(n * sizeof(float));
    if (m->dtype == 0) { uint16_t *h = (uint16_t *)raw; for (int64_t i = 0; i < n; i++) host[i] = f16_to_f32(h[i]); }
    else if (m->dtype == 1) memcpy(host, raw, n * 4);
    else memset(host, 0, n * 4);
    free(raw);
    float *dev; CU(cudaMalloc(&dev, n * sizeof(float)));
    CU(cudaMemcpy(dev, host, n * sizeof(float), cudaMemcpyHostToDevice));
    free(host); return dev;
}

/* ── GPU kernels ── */
__global__ void k_add_bias(float *x, const float *bias, int C, int spatial, int total) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < total) { int c = (i / spatial) % C; x[i] += bias[c]; }
}

__global__ void k_silu(float *x, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) { float v = x[i]; x[i] = v / (1.0f + expf(-v)); }
}

__global__ void k_gelu(float *x, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) {
        float v = x[i];
        x[i] = 0.5f * v * (1.0f + tanhf(0.7978845608f * (v + 0.044715f * v * v * v)));
    }
}

__global__ void k_group_norm(float *out, const float *x, const float *w, const float *b,
                              int C, int spatial, int groups) {
    int batch_group = blockIdx.x;  /* batch * groups + group */
    int g = batch_group % groups;
    int cpg = C / groups;
    int base = batch_group * cpg * spatial;
    /* compute mean and var */
    float sum = 0, sq = 0;
    int count = cpg * spatial;
    for (int i = 0; i < count; i++) { float v = x[base + i]; sum += v; sq += v * v; }
    float mean = sum / count;
    float var = sq / count - mean * mean;
    float inv = 1.0f / sqrtf(var + 1e-5f);
    for (int i = 0; i < count; i++) {
        int c = g * cpg + i / spatial;
        out[base + i] = (x[base + i] - mean) * inv * w[c] + b[c];
    }
}

__global__ void k_layer_norm(float *out, const float *x, const float *w, const float *b,
                              int dim, int seq) {
    int s = blockIdx.x;
    if (s >= seq) return;
    float sum = 0, sq = 0;
    for (int i = 0; i < dim; i++) { float v = x[s * dim + i]; sum += v; sq += v * v; }
    float mean = sum / dim;
    float var = sq / dim - mean * mean;
    float inv = 1.0f / sqrtf(var + 1e-5f);
    for (int i = 0; i < dim; i++)
        out[s * dim + i] = (x[s * dim + i] - mean) * inv * w[i] + b[i];
}

__global__ void k_softmax(float *x, int rows, int cols) {
    int r = blockIdx.x * blockDim.x + threadIdx.x;
    if (r >= rows) return;
    float *row = x + r * cols;
    float mx = row[0];
    for (int i = 1; i < cols; i++) if (row[i] > mx) mx = row[i];
    float sum = 0;
    for (int i = 0; i < cols; i++) { row[i] = expf(row[i] - mx); sum += row[i]; }
    for (int i = 0; i < cols; i++) row[i] /= sum;
}

__global__ void k_add(float *a, const float *b, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) a[i] += b[i];
}

__global__ void k_scale(float *x, float s, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) x[i] *= s;
}

__global__ void k_axpy(float *y, const float *x, float a, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < n) y[i] += a * x[i];
}

/* Conv2d: naive but correct. out[oc][oy][ox] = sum over ic,ky,kx of w[oc][ic][ky][kx] * x[ic][iy][ix] + b[oc] */
__global__ void k_conv2d(float *out, const float *x, const float *w, const float *bias,
                          int IC, int OC, int H, int W, int KH, int KW, int pad, int stride) {
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    int OH = (H + 2 * pad - KH) / stride + 1;
    int OW = (W + 2 * pad - KW) / stride + 1;
    int total = OC * OH * OW;
    if (idx >= total) return;
    int oc = idx / (OH * OW);
    int oy = (idx / OW) % OH;
    int ox = idx % OW;
    float val = bias ? bias[oc] : 0.0f;
    for (int ic = 0; ic < IC; ic++)
        for (int ky = 0; ky < KH; ky++)
            for (int kx = 0; kx < KW; kx++) {
                int iy = oy * stride - pad + ky;
                int ix = ox * stride - pad + kx;
                if (iy >= 0 && iy < H && ix >= 0 && ix < W)
                    val += w[((oc * IC + ic) * KH + ky) * KW + kx] * x[(ic * H + iy) * W + ix];
            }
    out[(oc * OH + oy) * OW + ox] = val;
}

/* 2x nearest upsample */
__global__ void k_upsample2x(float *out, const float *x, int C, int H, int W) {
    int idx = blockIdx.x * blockDim.x + threadIdx.x;
    int total = C * H * 2 * W * 2;
    if (idx >= total) return;
    int c = idx / (H * 2 * W * 2);
    int oy = (idx / (W * 2)) % (H * 2);
    int ox = idx % (W * 2);
    out[idx] = x[(c * H + oy / 2) * W + ox / 2];
}

/* Concatenate along channel dim: [C1,H,W] + [C2,H,W] -> [C1+C2,H,W] */
__global__ void k_concat_channels(float *out, const float *a, const float *b,
                                   int C1, int C2, int spatial) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    int total = (C1 + C2) * spatial;
    if (i >= total) return;
    int c = i / spatial, s = i % spatial;
    out[i] = (c < C1) ? a[c * spatial + s] : b[(c - C1) * spatial + s];
}

/* ── Helpers ── */
#define BLK 256
#define GRID(n) (((n) + BLK - 1) / BLK)

static float *gpu_alloc(int64_t n) {
    float *p; CU(cudaMalloc(&p, n * sizeof(float))); return p;
}

static float *gpu_zeros(int64_t n) {
    float *p = gpu_alloc(n);
    CU(cudaMemset(p, 0, n * sizeof(float)));
    return p;
}

static void gpu_copy(float *dst, const float *src, int64_t n) {
    CU(cudaMemcpy(dst, src, n * sizeof(float), cudaMemcpyDeviceToDevice));
}

/* C = A * B^T */
static void matmul(float *C, const float *A, const float *B, int M, int N, int K) {
    float alpha = 1.0f, beta = 0.0f;
    /* cuBLAS is column-major. We want C = A * B^T in row-major.
       Equivalent: C^T = B * A^T in col-major.
       So call: sgemm(N, T, N, M, K, B, N, A, K, C, N) */
    CUBLAS(cublasSgemm(g_cublas, CUBLAS_OP_T, CUBLAS_OP_N,
                       N, M, K, &alpha, B, K, A, K, &beta, C, N));
}

/* C = A * B */
static void matmul_nn(float *C, const float *A, const float *B, int M, int K, int N) {
    float alpha = 1.0f, beta = 0.0f;
    CUBLAS(cublasSgemm(g_cublas, CUBLAS_OP_N, CUBLAS_OP_N,
                       N, M, K, &alpha, B, N, A, K, &beta, C, N));
}

/* out = x @ W^T + b */
static void linear(float *out, const float *x, const float *W, const float *bias,
                    int seq, int in_dim, int out_dim) {
    matmul(out, x, W, seq, out_dim, in_dim);
    if (bias) {
        int n = seq * out_dim;
        k_add_bias<<<GRID(n), BLK>>>(out, bias, out_dim, 1, n);
    }
}

/* GroupNorm (32 groups) */
static void group_norm(float *out, const float *x, const float *w, const float *b,
                        int C, int spatial, int groups) {
    k_group_norm<<<groups, 1>>>(out, x, w, b, C, spatial, groups);
}

/* LayerNorm */
static void layer_norm(float *out, const float *x, const float *w, const float *b, int dim, int seq) {
    k_layer_norm<<<seq, 1>>>(out, x, w, b, dim, seq);
}

/* Conv2d */
static void conv2d(float *out, const float *x, const float *w, const float *b,
                    int IC, int OC, int H, int W, int K, int pad, int stride) {
    int OH = (H + 2 * pad - K) / stride + 1;
    int OW = (W + 2 * pad - K) / stride + 1;
    int total = OC * OH * OW;
    k_conv2d<<<GRID(total), BLK>>>(out, x, w, b, IC, OC, H, W, K, K, pad, stride);
}

/* Concat two channel tensors */
static float *concat_ch(const float *a, int C1, const float *b, int C2, int H, int W) {
    int spatial = H * W;
    int total = (C1 + C2) * spatial;
    float *out = gpu_alloc(total);
    k_concat_channels<<<GRID(total), BLK>>>(out, a, b, C1, C2, spatial);
    return out;
}

/* Downsample via stride-2 conv */
static float *downsample(const float *x, const char *prefix, int C, int H, int W) {
    char kn[300];
    snprintf(kn, sizeof(kn), "%s.op.weight", prefix);
    float *w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.op.bias", prefix);
    float *b = load_f32(kn);
    int OH = H / 2, OW = W / 2;
    float *out = gpu_alloc(C * OH * OW);
    conv2d(out, x, w, b, C, C, H, W, 3, 1, 2);
    cudaFree(w); cudaFree(b);
    return out;
}

/* Upsample: nearest 2x then conv */
static float *upsample_conv(const float *x, const char *prefix, int C, int H, int W) {
    char kn[300];
    float *up = gpu_alloc(C * H * 2 * W * 2);
    k_upsample2x<<<GRID(C * H * 2 * W * 2), BLK>>>(up, x, C, H, W);
    snprintf(kn, sizeof(kn), "%s.conv.weight", prefix);
    float *w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.conv.bias", prefix);
    float *b = load_f32(kn);
    float *out = gpu_alloc(C * H * 2 * W * 2);
    conv2d(out, up, w, b, C, C, H * 2, W * 2, 3, 1, 1);
    cudaFree(w); cudaFree(b); cudaFree(up);
    return out;
}

/* ── CLIP Text Encoder ── */
static int g_tokens[CLIP_SEQ];
static int g_token_count;

static void tokenize_prompt(void) {
    /* Pre-computed CLIP BPE tokens for our prompt */
    int toks[] = {49406, 320, 5765, 6460, 7728, 10427, 525, 320, 15650, 267,
                  1125, 14719, 49407};
    g_token_count = sizeof(toks) / sizeof(toks[0]);
    for (int i = 0; i < CLIP_SEQ; i++)
        g_tokens[i] = (i < g_token_count) ? toks[i] : 49407;
}

/* Returns [77, 768] on GPU */
static float *run_clip(void) {
    printf("  Running CLIP text encoder...\n");
    tokenize_prompt();
    float *tok_emb = load_f32("cond_stage_model.transformer.text_model.embeddings.token_embedding.weight");
    float *pos_emb = load_f32("cond_stage_model.transformer.text_model.embeddings.position_embedding.weight");
    float *h_emb = (float *)malloc(CLIP_SEQ * CLIP_DIM * sizeof(float));
    float *h_tok = (float *)malloc(CLIP_VOCAB * CLIP_DIM * sizeof(float));
    float *h_pos = (float *)malloc(CLIP_SEQ * CLIP_DIM * sizeof(float));
    CU(cudaMemcpy(h_tok, tok_emb, CLIP_VOCAB * CLIP_DIM * sizeof(float), cudaMemcpyDeviceToHost));
    CU(cudaMemcpy(h_pos, pos_emb, CLIP_SEQ * CLIP_DIM * sizeof(float), cudaMemcpyDeviceToHost));
    for (int s = 0; s < CLIP_SEQ; s++)
        for (int d = 0; d < CLIP_DIM; d++)
            h_emb[s * CLIP_DIM + d] = h_tok[g_tokens[s] * CLIP_DIM + d] + h_pos[s * CLIP_DIM + d];
    cudaFree(tok_emb); cudaFree(pos_emb);
    free(h_tok); free(h_pos);
    float *x = gpu_alloc(CLIP_SEQ * CLIP_DIM);
    CU(cudaMemcpy(x, h_emb, CLIP_SEQ * CLIP_DIM * sizeof(float), cudaMemcpyHostToDevice));
    free(h_emb);
    float *tmp = gpu_alloc(CLIP_SEQ * CLIP_DIM);
    float *mlp_tmp = gpu_alloc(CLIP_SEQ * CLIP_MLP);
    for (int L = 0; L < CLIP_LAYERS; L++) {
        char prefix[200], kn[300];
        snprintf(prefix, sizeof(prefix), "cond_stage_model.transformer.text_model.encoder.layers.%d", L);
        int head_dim = CLIP_DIM / CLIP_HEADS;
        /* LN1 + self-attn (single-head approx) + residual */
        snprintf(kn, sizeof(kn), "%s.layer_norm1.weight", prefix); float *ln1w = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.layer_norm1.bias", prefix); float *ln1b = load_f32(kn);
        layer_norm(tmp, x, ln1w, ln1b, CLIP_DIM, CLIP_SEQ); cudaFree(ln1w); cudaFree(ln1b);
        snprintf(kn, sizeof(kn), "%s.self_attn.q_proj.weight", prefix); float *qw = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.self_attn.q_proj.bias", prefix); float *qb = load_f32(kn);
        float *Q = gpu_alloc(CLIP_SEQ * CLIP_DIM); linear(Q, tmp, qw, qb, CLIP_SEQ, CLIP_DIM, CLIP_DIM); cudaFree(qw); cudaFree(qb);
        snprintf(kn, sizeof(kn), "%s.self_attn.k_proj.weight", prefix); float *kw = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.self_attn.k_proj.bias", prefix); float *kb = load_f32(kn);
        float *K_mat = gpu_alloc(CLIP_SEQ * CLIP_DIM); linear(K_mat, tmp, kw, kb, CLIP_SEQ, CLIP_DIM, CLIP_DIM); cudaFree(kw); cudaFree(kb);
        snprintf(kn, sizeof(kn), "%s.self_attn.v_proj.weight", prefix); float *vw = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.self_attn.v_proj.bias", prefix); float *vb = load_f32(kn);
        float *V = gpu_alloc(CLIP_SEQ * CLIP_DIM); linear(V, tmp, vw, vb, CLIP_SEQ, CLIP_DIM, CLIP_DIM); cudaFree(vw); cudaFree(vb);
        float scale = 1.0f / sqrtf((float)head_dim);
        float *scores = gpu_alloc(CLIP_SEQ * CLIP_SEQ);
        matmul(scores, Q, K_mat, CLIP_SEQ, CLIP_SEQ, CLIP_DIM);
        k_scale<<<GRID(CLIP_SEQ * CLIP_SEQ), BLK>>>(scores, scale, CLIP_SEQ * CLIP_SEQ);
        float *h_scores = (float *)malloc(CLIP_SEQ * CLIP_SEQ * sizeof(float));
        CU(cudaMemcpy(h_scores, scores, CLIP_SEQ * CLIP_SEQ * sizeof(float), cudaMemcpyDeviceToHost));
        for (int i = 0; i < CLIP_SEQ; i++) for (int j = i+1; j < CLIP_SEQ; j++) h_scores[i*CLIP_SEQ+j] = -1e9f;
        CU(cudaMemcpy(scores, h_scores, CLIP_SEQ * CLIP_SEQ * sizeof(float), cudaMemcpyHostToDevice)); free(h_scores);
        k_softmax<<<GRID(CLIP_SEQ), BLK>>>(scores, CLIP_SEQ, CLIP_SEQ);
        float *attn_out = gpu_alloc(CLIP_SEQ * CLIP_DIM);
        matmul_nn(attn_out, scores, V, CLIP_SEQ, CLIP_SEQ, CLIP_DIM);
        cudaFree(Q); cudaFree(K_mat); cudaFree(V); cudaFree(scores);
        snprintf(kn, sizeof(kn), "%s.self_attn.out_proj.weight", prefix); float *ow = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.self_attn.out_proj.bias", prefix); float *ob = load_f32(kn);
        linear(tmp, attn_out, ow, ob, CLIP_SEQ, CLIP_DIM, CLIP_DIM); cudaFree(ow); cudaFree(ob); cudaFree(attn_out);
        k_add<<<GRID(CLIP_SEQ * CLIP_DIM), BLK>>>(x, tmp, CLIP_SEQ * CLIP_DIM);

        /* LN2 + MLP + residual */
        snprintf(kn, sizeof(kn), "%s.layer_norm2.weight", prefix); float *ln2w = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.layer_norm2.bias", prefix); float *ln2b = load_f32(kn);
        layer_norm(tmp, x, ln2w, ln2b, CLIP_DIM, CLIP_SEQ); cudaFree(ln2w); cudaFree(ln2b);
        snprintf(kn, sizeof(kn), "%s.mlp.fc1.weight", prefix); float *fc1w = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.mlp.fc1.bias", prefix); float *fc1b = load_f32(kn);
        linear(mlp_tmp, tmp, fc1w, fc1b, CLIP_SEQ, CLIP_DIM, CLIP_MLP); cudaFree(fc1w); cudaFree(fc1b);
        k_gelu<<<GRID(CLIP_SEQ * CLIP_MLP), BLK>>>(mlp_tmp, CLIP_SEQ * CLIP_MLP);
        snprintf(kn, sizeof(kn), "%s.mlp.fc2.weight", prefix); float *fc2w = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.mlp.fc2.bias", prefix); float *fc2b = load_f32(kn);
        linear(tmp, mlp_tmp, fc2w, fc2b, CLIP_SEQ, CLIP_MLP, CLIP_DIM); cudaFree(fc2w); cudaFree(fc2b);
        k_add<<<GRID(CLIP_SEQ * CLIP_DIM), BLK>>>(x, tmp, CLIP_SEQ * CLIP_DIM);
        printf("    CLIP layer %d done\n", L);
    }

    /* Final layer norm */
    float *flnw = load_f32("cond_stage_model.transformer.text_model.final_layer_norm.weight");
    float *flnb = load_f32("cond_stage_model.transformer.text_model.final_layer_norm.bias");
    layer_norm(tmp, x, flnw, flnb, CLIP_DIM, CLIP_SEQ);
    gpu_copy(x, tmp, CLIP_SEQ * CLIP_DIM);
    cudaFree(flnw); cudaFree(flnb);
    cudaFree(tmp); cudaFree(mlp_tmp);
    return x;
}
static float *run_clip_uncond(void) { return gpu_zeros(CLIP_SEQ * CLIP_DIM); }

/* ── UNet ── */
static float *sinusoidal_embedding(int timestep) {
    float *h_emb = (float *)malloc(320 * sizeof(float));
    for (int i = 0; i < 160; i++) {
        float freq = expf(-logf(10000.0f) * i / 160.0f);
        h_emb[i] = sinf(timestep * freq);
        h_emb[i + 160] = cosf(timestep * freq);
    }
    float *d_emb = gpu_alloc(320);
    CU(cudaMemcpy(d_emb, h_emb, 320 * sizeof(float), cudaMemcpyHostToDevice));
    free(h_emb);
    return d_emb;
}

static float *run_time_embed(int timestep) {
    float *sin_emb = sinusoidal_embedding(timestep);
    float *te0w = load_f32("model.diffusion_model.time_embed.0.weight");
    float *te0b = load_f32("model.diffusion_model.time_embed.0.bias");
    float *te2w = load_f32("model.diffusion_model.time_embed.2.weight");
    float *te2b = load_f32("model.diffusion_model.time_embed.2.bias");

    float *t1 = gpu_alloc(1280);
    linear(t1, sin_emb, te0w, te0b, 1, 320, 1280);
    k_silu<<<GRID(1280), BLK>>>(t1, 1280);
    float *t2 = gpu_alloc(1280);
    linear(t2, t1, te2w, te2b, 1, 1280, 1280);

    cudaFree(sin_emb); cudaFree(te0w); cudaFree(te0b);
    cudaFree(te2w); cudaFree(te2b); cudaFree(t1);
    return t2;  /* [1280] */
}

/* ResBlock */
static void resblock(float *out, const float *x, const float *temb,
                      const char *prefix, int C_in, int C_out, int H, int W) {
    char kn[300];
    int spatial = H * W;

    /* in_layers: GroupNorm(0) + SiLU + Conv3x3(2) */
    snprintf(kn, sizeof(kn), "%s.in_layers.0.weight", prefix);
    float *gn0w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.in_layers.0.bias", prefix);
    float *gn0b = load_f32(kn);
    float *h = gpu_alloc(C_in * spatial);
    group_norm(h, x, gn0w, gn0b, C_in, spatial, 32);
    k_silu<<<GRID(C_in * spatial), BLK>>>(h, C_in * spatial);
    cudaFree(gn0w); cudaFree(gn0b);

    snprintf(kn, sizeof(kn), "%s.in_layers.2.weight", prefix);
    float *c0w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.in_layers.2.bias", prefix);
    float *c0b = load_f32(kn);
    float *h2 = gpu_alloc(C_out * spatial);
    conv2d(h2, h, c0w, c0b, C_in, C_out, H, W, 3, 1, 1);
    cudaFree(c0w); cudaFree(c0b); cudaFree(h);

    /* emb_layers: SiLU + Linear(1280 -> C_out) -- add to each spatial position */
    snprintf(kn, sizeof(kn), "%s.emb_layers.1.weight", prefix);
    float *ew = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.emb_layers.1.bias", prefix);
    float *eb = load_f32(kn);
    float *temb_silu = gpu_alloc(1280);
    gpu_copy(temb_silu, temb, 1280);
    k_silu<<<GRID(1280), BLK>>>(temb_silu, 1280);
    float *temb_proj = gpu_alloc(C_out);
    linear(temb_proj, temb_silu, ew, eb, 1, 1280, C_out);
    cudaFree(ew); cudaFree(eb); cudaFree(temb_silu);
    /* Add temb to each spatial position */
    k_add_bias<<<GRID(C_out * spatial), BLK>>>(h2, temb_proj, C_out, spatial, C_out * spatial);
    cudaFree(temb_proj);

    /* out_layers: GroupNorm(0) + SiLU + Conv3x3(3) */
    snprintf(kn, sizeof(kn), "%s.out_layers.0.weight", prefix);
    float *gn1w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.out_layers.0.bias", prefix);
    float *gn1b = load_f32(kn);
    float *h3 = gpu_alloc(C_out * spatial);
    group_norm(h3, h2, gn1w, gn1b, C_out, spatial, 32);
    k_silu<<<GRID(C_out * spatial), BLK>>>(h3, C_out * spatial);
    cudaFree(gn1w); cudaFree(gn1b); cudaFree(h2);

    snprintf(kn, sizeof(kn), "%s.out_layers.3.weight", prefix);
    float *c1w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.out_layers.3.bias", prefix);
    float *c1b = load_f32(kn);
    conv2d(out, h3, c1w, c1b, C_out, C_out, H, W, 3, 1, 1);
    cudaFree(c1w); cudaFree(c1b); cudaFree(h3);

    /* Skip connection */
    if (C_in != C_out) {
        snprintf(kn, sizeof(kn), "%s.skip_connection.weight", prefix);
        float *sw = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.skip_connection.bias", prefix);
        float *sb = load_f32(kn);
        float *skip = gpu_alloc(C_out * spatial);
        conv2d(skip, x, sw, sb, C_in, C_out, H, W, 1, 0, 1);
        k_add<<<GRID(C_out * spatial), BLK>>>(out, skip, C_out * spatial);
        cudaFree(sw); cudaFree(sb); cudaFree(skip);
    } else {
        k_add<<<GRID(C_out * spatial), BLK>>>(out, x, C_out * spatial);
    }
}

/* Spatial transformer (cross-attn + FF) */
static void spatial_transformer(float *x, const float *context, const char *prefix,
                                 int C, int H, int W, int ctx_dim) {
    char kn[300];
    int spatial = H * W;

    /* norm -> proj_in -> reshape to [spatial, C] */
    snprintf(kn, sizeof(kn), "%s.norm.weight", prefix);
    float *nw = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.norm.bias", prefix);
    float *nb = load_f32(kn);
    float *normed = gpu_alloc(C * spatial);
    group_norm(normed, x, nw, nb, C, spatial, 32);
    cudaFree(nw); cudaFree(nb);

    snprintf(kn, sizeof(kn), "%s.proj_in.weight", prefix);
    float *piw = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.proj_in.bias", prefix);
    float *pib = load_f32(kn);
    /* proj_in is 1x1 conv, equivalent to per-position linear */
    float *h = gpu_alloc(C * spatial);
    conv2d(h, normed, piw, pib, C, C, H, W, 1, 0, 1);
    cudaFree(piw); cudaFree(pib); cudaFree(normed);

    /* Reshape h to [spatial, C] for attention operations */
    /* h is [C, H, W] -> we need [H*W, C] = transpose */
    float *h_host = (float *)malloc(C * spatial * sizeof(float));
    CU(cudaMemcpy(h_host, h, C * spatial * sizeof(float), cudaMemcpyDeviceToHost));
    float *ht = (float *)malloc(spatial * C * sizeof(float));
    for (int c = 0; c < C; c++)
        for (int s = 0; s < spatial; s++)
            ht[s * C + c] = h_host[c * spatial + s];
    float *h_seq = gpu_alloc(spatial * C);
    CU(cudaMemcpy(h_seq, ht, spatial * C * sizeof(float), cudaMemcpyHostToDevice));
    free(h_host); free(ht);

    /* transformer_blocks.0: self-attn (attn1) + cross-attn (attn2) + FF */
    char tb[300];
    snprintf(tb, sizeof(tb), "%s.transformer_blocks.0", prefix);

    /* attn1 (self-attention) */
    snprintf(kn, sizeof(kn), "%s.norm1.weight", tb);
    float *n1w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.norm1.bias", tb);
    float *n1b = load_f32(kn);
    float *ln1 = gpu_alloc(spatial * C);
    layer_norm(ln1, h_seq, n1w, n1b, C, spatial);
    cudaFree(n1w); cudaFree(n1b);

    /* Self-attention: Q, K, V all from ln1 */
    snprintf(kn, sizeof(kn), "%s.attn1.to_q.weight", tb);
    float *sa_qw = load_f32(kn);
    float *sa_Q = gpu_alloc(spatial * C);
    linear(sa_Q, ln1, sa_qw, NULL, spatial, C, C);
    cudaFree(sa_qw);

    snprintf(kn, sizeof(kn), "%s.attn1.to_k.weight", tb);
    float *sa_kw = load_f32(kn);
    float *sa_K = gpu_alloc(spatial * C);
    linear(sa_K, ln1, sa_kw, NULL, spatial, C, C);
    cudaFree(sa_kw);

    snprintf(kn, sizeof(kn), "%s.attn1.to_v.weight", tb);
    float *sa_vw = load_f32(kn);
    float *sa_V = gpu_alloc(spatial * C);
    linear(sa_V, ln1, sa_vw, NULL, spatial, C, C);
    cudaFree(sa_vw); cudaFree(ln1);

    int sa_heads = (C == 320) ? 5 : (C == 640) ? 10 : 20;
    int sa_hd = C / sa_heads;
    float sa_scale = 1.0f / sqrtf((float)sa_hd);
    float *sa_scores = gpu_alloc(spatial * spatial);
    matmul(sa_scores, sa_Q, sa_K, spatial, spatial, C);
    k_scale<<<GRID(spatial * spatial), BLK>>>(sa_scores, sa_scale, spatial * spatial);
    k_softmax<<<GRID(spatial), BLK>>>(sa_scores, spatial, spatial);
    float *sa_out = gpu_alloc(spatial * C);
    matmul_nn(sa_out, sa_scores, sa_V, spatial, spatial, C);
    cudaFree(sa_Q); cudaFree(sa_K); cudaFree(sa_V); cudaFree(sa_scores);

    snprintf(kn, sizeof(kn), "%s.attn1.to_out.0.weight", tb);
    float *sa_ow = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.attn1.to_out.0.bias", tb);
    float *sa_ob = load_f32(kn);
    float *sa_proj = gpu_alloc(spatial * C);
    linear(sa_proj, sa_out, sa_ow, sa_ob, spatial, C, C);
    cudaFree(sa_ow); cudaFree(sa_ob); cudaFree(sa_out);
    k_add<<<GRID(spatial * C), BLK>>>(h_seq, sa_proj, spatial * C);
    cudaFree(sa_proj);

    /* attn2 (cross-attention with text context) */
    snprintf(kn, sizeof(kn), "%s.norm2.weight", tb);
    float *n2w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.norm2.bias", tb);
    float *n2b = load_f32(kn);
    float *ln2 = gpu_alloc(spatial * C);
    layer_norm(ln2, h_seq, n2w, n2b, C, spatial);
    cudaFree(n2w); cudaFree(n2b);

    /* Cross-attention: Q from ln2, K,V from context */
    snprintf(kn, sizeof(kn), "%s.attn2.to_q.weight", tb);
    float *qw = load_f32(kn);
    float *Q = gpu_alloc(spatial * C);
    linear(Q, ln2, qw, NULL, spatial, C, C);
    cudaFree(qw);

    snprintf(kn, sizeof(kn), "%s.attn2.to_k.weight", tb);
    float *kw = load_f32(kn);
    float *K_mat = gpu_alloc(CLIP_SEQ * C);
    linear(K_mat, context, kw, NULL, CLIP_SEQ, ctx_dim, C);
    cudaFree(kw);

    snprintf(kn, sizeof(kn), "%s.attn2.to_v.weight", tb);
    float *vw = load_f32(kn);
    float *V = gpu_alloc(CLIP_SEQ * C);
    linear(V, context, vw, NULL, CLIP_SEQ, ctx_dim, C);
    cudaFree(vw);

    /* Simplified single-head cross-attention */
    int n_heads = (C == 320) ? 5 : (C == 640) ? 10 : 20;
    int head_dim = C / n_heads;
    float *attn_out = gpu_zeros(spatial * C);
    float scale_f = 1.0f / sqrtf((float)head_dim);

    /* Do attention in one shot: scores = Q @ K^T [spatial, 77] */
    float *scores = gpu_alloc(spatial * CLIP_SEQ);
    matmul(scores, Q, K_mat, spatial, CLIP_SEQ, C);
    k_scale<<<GRID(spatial * CLIP_SEQ), BLK>>>(scores, scale_f, spatial * CLIP_SEQ);
    k_softmax<<<GRID(spatial), BLK>>>(scores, spatial, CLIP_SEQ);
    /* attn_out = scores @ V [spatial, C] */
    matmul_nn(attn_out, scores, V, spatial, CLIP_SEQ, C);
    cudaFree(Q); cudaFree(K_mat); cudaFree(V); cudaFree(scores); cudaFree(ln2);

    /* to_out projection */
    snprintf(kn, sizeof(kn), "%s.attn2.to_out.0.weight", tb);
    float *ow = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.attn2.to_out.0.bias", tb);
    float *ob = load_f32(kn);
    float *proj = gpu_alloc(spatial * C);
    linear(proj, attn_out, ow, ob, spatial, C, C);
    cudaFree(ow); cudaFree(ob); cudaFree(attn_out);

    /* Residual */
    k_add<<<GRID(spatial * C), BLK>>>(h_seq, proj, spatial * C);
    cudaFree(proj);

    /* FF: norm3 -> fc1(GEGLU: splits into gate) -> fc2 */
    snprintf(kn, sizeof(kn), "%s.norm3.weight", tb);
    float *n3w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.norm3.bias", tb);
    float *n3b = load_f32(kn);
    float *ln3 = gpu_alloc(spatial * C);
    layer_norm(ln3, h_seq, n3w, n3b, C, spatial);
    cudaFree(n3w); cudaFree(n3b);

    /* GEGLU feed-forward */
    int ff_dim = C * 4;
    snprintf(kn, sizeof(kn), "%s.ff.net.0.proj.weight", tb);
    float *ff0w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.ff.net.0.proj.bias", tb);
    float *ff0b = load_f32(kn);
    float *ff0 = gpu_alloc(spatial * ff_dim * 2);
    linear(ff0, ln3, ff0w, ff0b, spatial, C, ff_dim * 2);
    cudaFree(ff0w); cudaFree(ff0b); cudaFree(ln3);

    /* Split and GELU-gate on CPU for simplicity */
    float *h_ff0 = (float *)malloc(spatial * ff_dim * 2 * sizeof(float));
    CU(cudaMemcpy(h_ff0, ff0, spatial * ff_dim * 2 * sizeof(float), cudaMemcpyDeviceToHost));
    float *h_gated = (float *)malloc(spatial * ff_dim * sizeof(float));
    for (int i = 0; i < spatial; i++)
        for (int j = 0; j < ff_dim; j++) {
            float a = h_ff0[i * ff_dim * 2 + j];
            float b = h_ff0[i * ff_dim * 2 + ff_dim + j];
            /* GELU gate */
            float gelu_b = 0.5f * b * (1.0f + tanhf(0.7978845608f * (b + 0.044715f * b * b * b)));
            h_gated[i * ff_dim + j] = a * gelu_b;
        }
    cudaFree(ff0);
    float *gated = gpu_alloc(spatial * ff_dim);
    CU(cudaMemcpy(gated, h_gated, spatial * ff_dim * sizeof(float), cudaMemcpyHostToDevice));
    free(h_ff0); free(h_gated);

    snprintf(kn, sizeof(kn), "%s.ff.net.2.weight", tb);
    float *ff2w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.ff.net.2.bias", tb);
    float *ff2b = load_f32(kn);
    float *ff_out = gpu_alloc(spatial * C);
    linear(ff_out, gated, ff2w, ff2b, spatial, ff_dim, C);
    cudaFree(ff2w); cudaFree(ff2b); cudaFree(gated);

    k_add<<<GRID(spatial * C), BLK>>>(h_seq, ff_out, spatial * C);
    cudaFree(ff_out);

    /* proj_out */
    snprintf(kn, sizeof(kn), "%s.proj_out.weight", prefix);
    float *pow_ = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.proj_out.bias", prefix);
    float *pob = load_f32(kn);

    /* Transpose back from [spatial, C] to [C, spatial] */
    float *h_seq_host = (float *)malloc(spatial * C * sizeof(float));
    CU(cudaMemcpy(h_seq_host, h_seq, spatial * C * sizeof(float), cudaMemcpyDeviceToHost));
    float *h_chw = (float *)malloc(C * spatial * sizeof(float));
    for (int c = 0; c < C; c++)
        for (int s = 0; s < spatial; s++)
            h_chw[c * spatial + s] = h_seq_host[s * C + c];
    float *chw = gpu_alloc(C * spatial);
    CU(cudaMemcpy(chw, h_chw, C * spatial * sizeof(float), cudaMemcpyHostToDevice));
    free(h_seq_host); free(h_chw);
    cudaFree(h_seq);

    float *pout = gpu_alloc(C * spatial);
    conv2d(pout, chw, pow_, pob, C, C, H, W, 1, 0, 1);
    cudaFree(pow_); cudaFree(pob); cudaFree(chw); cudaFree(h);

    /* Residual add to original x */
    k_add<<<GRID(C * spatial), BLK>>>(x, pout, C * spatial);
    cudaFree(pout);
}

/* Run a ResBlock (+ optional SpatialTransformer) at given block prefix */
static float *run_input_block(const float *x, const float *temb, const float *cond,
                               const char *blk_prefix, int Cin, int Cout, int H, int W,
                               int has_attn) {
    char rb[300], st[300];
    snprintf(rb, sizeof(rb), "%s.0", blk_prefix);
    float *h = gpu_alloc(Cout * H * W);
    resblock(h, x, temb, rb, Cin, Cout, H, W);
    if (has_attn) {
        snprintf(st, sizeof(st), "%s.1", blk_prefix);
        spatial_transformer(h, cond, st, Cout, H, W, CLIP_DIM);
    }
    return h;
}

/* Full SD 1.5 UNet: 12 input blocks, middle, 12 output blocks with skip connections.
   Channel schedule: 320, 320, 320 | ds | 640, 640 | ds | 1280, 1280 | ds | 1280, 1280
   Attention at channels 320, 640, 1280 (levels 0-2), none at level 3.
   Output blocks reverse: concat skip, resblock, optional attn, optional upsample. */
static float *run_unet(const float *latent, int timestep, const float *cond) {
    float *temb = run_time_embed(timestep);
    float *skips[12]; int skip_ch[12]; int skip_H[12]; int skip_W[12];
    int H = LAT_H, W = LAT_W;

    /* ── Input blocks ── */
    /* ib0: conv_in 4->320 */
    float *icw = load_f32("model.diffusion_model.input_blocks.0.0.weight");
    float *icb = load_f32("model.diffusion_model.input_blocks.0.0.bias");
    float *h = gpu_alloc(320 * H * W);
    conv2d(h, latent, icw, icb, 4, 320, H, W, 3, 1, 1);
    cudaFree(icw); cudaFree(icb);
    skips[0] = h; skip_ch[0] = 320; skip_H[0] = H; skip_W[0] = W;

    /* ib1: ResBlock+Attn 320->320 */
    float *ib1 = run_input_block(h, temb, cond, "model.diffusion_model.input_blocks.1", 320, 320, H, W, 1);
    skips[1] = ib1; skip_ch[1] = 320; skip_H[1] = H; skip_W[1] = W;

    /* ib2: ResBlock+Attn 320->320 */
    float *ib2 = run_input_block(ib1, temb, cond, "model.diffusion_model.input_blocks.2", 320, 320, H, W, 1);
    skips[2] = ib2; skip_ch[2] = 320; skip_H[2] = H; skip_W[2] = W;

    /* ib3: Downsample 320, 64->32 */
    float *ib3 = downsample(ib2, "model.diffusion_model.input_blocks.3.0", 320, H, W);
    H /= 2; W /= 2;
    skips[3] = ib3; skip_ch[3] = 320; skip_H[3] = H; skip_W[3] = W;
    printf("      UNet level 0 done (%dx%d, 320ch)\n", H*2, W*2);

    /* ib4: ResBlock+Attn 320->640 */
    float *ib4 = run_input_block(ib3, temb, cond, "model.diffusion_model.input_blocks.4", 320, 640, H, W, 1);
    skips[4] = ib4; skip_ch[4] = 640; skip_H[4] = H; skip_W[4] = W;

    /* ib5: ResBlock+Attn 640->640 */
    float *ib5 = run_input_block(ib4, temb, cond, "model.diffusion_model.input_blocks.5", 640, 640, H, W, 1);
    skips[5] = ib5; skip_ch[5] = 640; skip_H[5] = H; skip_W[5] = W;

    /* ib6: Downsample 640, 32->16 */
    float *ib6 = downsample(ib5, "model.diffusion_model.input_blocks.6.0", 640, H, W);
    H /= 2; W /= 2;
    skips[6] = ib6; skip_ch[6] = 640; skip_H[6] = H; skip_W[6] = W;
    printf("      UNet level 1 done (%dx%d, 640ch)\n", H*2, W*2);

    /* ib7: ResBlock+Attn 640->1280 */
    float *ib7 = run_input_block(ib6, temb, cond, "model.diffusion_model.input_blocks.7", 640, 1280, H, W, 1);
    skips[7] = ib7; skip_ch[7] = 1280; skip_H[7] = H; skip_W[7] = W;

    /* ib8: ResBlock+Attn 1280->1280 */
    float *ib8 = run_input_block(ib7, temb, cond, "model.diffusion_model.input_blocks.8", 1280, 1280, H, W, 1);
    skips[8] = ib8; skip_ch[8] = 1280; skip_H[8] = H; skip_W[8] = W;

    /* ib9: Downsample 1280, 16->8 */
    float *ib9 = downsample(ib8, "model.diffusion_model.input_blocks.9.0", 1280, H, W);
    H /= 2; W /= 2;
    skips[9] = ib9; skip_ch[9] = 1280; skip_H[9] = H; skip_W[9] = W;
    printf("      UNet level 2 done (%dx%d, 1280ch)\n", H*2, W*2);

    /* ib10: ResBlock 1280->1280, NO attention */
    float *ib10 = run_input_block(ib9, temb, cond, "model.diffusion_model.input_blocks.10", 1280, 1280, H, W, 0);
    skips[10] = ib10; skip_ch[10] = 1280; skip_H[10] = H; skip_W[10] = W;

    /* ib11: ResBlock 1280->1280, NO attention */
    float *ib11 = run_input_block(ib10, temb, cond, "model.diffusion_model.input_blocks.11", 1280, 1280, H, W, 0);
    skips[11] = ib11; skip_ch[11] = 1280; skip_H[11] = H; skip_W[11] = W;
    printf("      UNet level 3 done (%dx%d, 1280ch)\n", H, W);

    /* ── Middle block: ResBlock + Attn + ResBlock ── */
    float *mid1 = gpu_alloc(1280 * H * W);
    resblock(mid1, ib11, temb, "model.diffusion_model.middle_block.0", 1280, 1280, H, W);
    spatial_transformer(mid1, cond, "model.diffusion_model.middle_block.1", 1280, H, W, CLIP_DIM);
    float *mid2 = gpu_alloc(1280 * H * W);
    resblock(mid2, mid1, temb, "model.diffusion_model.middle_block.2", 1280, 1280, H, W);
    cudaFree(mid1);
    printf("      UNet middle done (%dx%d, 1280ch)\n", H, W);

    /* ── Output blocks (reverse order, concat skip connections) ──
       ob_idx: skip_from  Cin_cat  Cout  has_attn  has_upsample
       0:      ib11       2560     1280  0         0
       1:      ib10       2560     1280  0         0
       2:      ib9        2560     1280  0         0  + upsample 8->16
       3:      ib8        2560     1280  1         0
       4:      ib7        2560     1280  1         0
       5:      ib6        1920     1280  1         0  + upsample 16->32
       6:      ib5        1920     640   1         0
       7:      ib4        1280     640   1         0
       8:      ib3        960      640   1         0  + upsample 32->64
       9:      ib2        960      320   1         0
       10:     ib1        640      320   1         0
       11:     ib0        640      320   1         0
    */
    struct { int skip_idx; int Cout; int has_attn; int has_up; } ob_spec[12] = {
        {11, 1280, 0, 0}, {10, 1280, 0, 0}, {9, 1280, 0, 1},
        {8,  1280, 1, 0}, {7,  1280, 1, 0}, {6, 1280, 1, 1},
        {5,  640,  1, 0}, {4,  640,  1, 0}, {3, 640,  1, 1},
        {2,  320,  1, 0}, {1,  320,  1, 0}, {0, 320,  1, 0}
    };

    float *cur = mid2;
    int cur_ch = 1280;
    for (int oi = 0; oi < 12; oi++) {
        int si = ob_spec[oi].skip_idx;
        int Cout = ob_spec[oi].Cout;
        int sc = skip_ch[si];
        /* Concat: [cur_ch, H, W] + [sc, H, W] -> [cur_ch + sc, H, W] */
        float *cat = concat_ch(cur, cur_ch, skips[si], sc, H, W);
        int Cin = cur_ch + sc;
        if (cur != mid2) cudaFree(cur);

        /* ResBlock */
        char rb[300];
        snprintf(rb, sizeof(rb), "model.diffusion_model.output_blocks.%d.0", oi);
        float *ob = gpu_alloc(Cout * H * W);
        resblock(ob, cat, temb, rb, Cin, Cout, H, W);
        cudaFree(cat);

        /* Optional spatial transformer */
        if (ob_spec[oi].has_attn) {
            /* The attn subblock index depends on whether there's also an upsample.
               For blocks with upsample, attn is .1 and upsample is .2.
               For blocks without upsample, attn is .1. */
            char st[300];
            snprintf(st, sizeof(st), "model.diffusion_model.output_blocks.%d.1", oi);
            spatial_transformer(ob, cond, st, Cout, H, W, CLIP_DIM);
        }

        /* Optional upsample */
        if (ob_spec[oi].has_up) {
            char up[300];
            int sub = ob_spec[oi].has_attn ? 2 : 1;
            snprintf(up, sizeof(up), "model.diffusion_model.output_blocks.%d.%d", oi, sub);
            float *upsampled = upsample_conv(ob, up, Cout, H, W);
            cudaFree(ob);
            ob = upsampled;
            H *= 2; W *= 2;
        }

        cur = ob;
        cur_ch = Cout;
        printf("      UNet output_block %d done (%dx%d, %dch)\n", oi, H, W, cur_ch);
    }

    /* Free skip connections (some may have been used as cur earlier, careful) */
    /* skips are still referenced but we consumed them in concat; the concat made copies */
    for (int i = 0; i < 12; i++) cudaFree(skips[i]);

    /* ── Output head: GroupNorm + SiLU + Conv 320->4 ── */
    float *onw = load_f32("model.diffusion_model.out.0.weight");
    float *onb = load_f32("model.diffusion_model.out.0.bias");
    float *gn_out = gpu_alloc(cur_ch * H * W);
    group_norm(gn_out, cur, onw, onb, cur_ch, H * W, 32);
    k_silu<<<GRID(cur_ch * H * W), BLK>>>(gn_out, cur_ch * H * W);
    cudaFree(onw); cudaFree(onb); cudaFree(cur);

    float *ocw = load_f32("model.diffusion_model.out.2.weight");
    float *ocb = load_f32("model.diffusion_model.out.2.bias");
    float *out = gpu_alloc(LAT_SZ);
    conv2d(out, gn_out, ocw, ocb, cur_ch, 4, H, W, 3, 1, 1);
    cudaFree(ocw); cudaFree(ocb); cudaFree(gn_out);
    cudaFree(temb);
    return out;
}

/* ── DDIM Sampler ── */
static void get_ddim_schedule(float *alphas_bar, int n_steps, int *timesteps) {
    TensorMeta *m = find_tensor("alphas_cumprod");
    if (!m) { fprintf(stderr, "alphas_cumprod not found\n"); exit(1); }
    int64_t bytes = m->off_end - m->off_start;
    uint16_t *raw = (uint16_t *)malloc(bytes);
    fseek(g_model_fp, (long)(g_data_offset + m->off_start), SEEK_SET);
    fread(raw, 1, bytes, g_model_fp);
    float full_ab[1000];
    for (int i = 0; i < 1000; i++) full_ab[i] = f16_to_f32(raw[i]);
    free(raw);
    for (int i = 0; i < n_steps; i++) {
        timesteps[i] = 999 - i * (999 / (n_steps - 1));
        alphas_bar[i] = full_ab[timesteps[i]];
    }
    alphas_bar[n_steps] = 1.0f; /* alpha_bar for t=0 */
}

__global__ void k_ddim_step(float *x_out, const float *x_t, const float *eps,
                             float alpha_t, float alpha_prev, int n) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= n) return;
    float sqrt_at = sqrtf(alpha_t);
    float sqrt_1mat = sqrtf(1.0f - alpha_t);
    float sqrt_ap = sqrtf(alpha_prev);
    float sqrt_1map = sqrtf(1.0f - alpha_prev);
    /* x0_pred = (x_t - sqrt(1-alpha_t) * eps) / sqrt(alpha_t) */
    float x0 = (x_t[i] - sqrt_1mat * eps[i]) / sqrt_at;
    /* x_{t-1} = sqrt(alpha_{t-1}) * x0 + sqrt(1-alpha_{t-1}) * eps */
    x_out[i] = sqrt_ap * x0 + sqrt_1map * eps[i];
}

static float *run_ddim(const float *cond, const float *uncond) {
    printf("  Running DDIM sampling (%d steps)...\n", DDIM_STEPS);

    float alphas_bar[DDIM_STEPS + 1];
    int timesteps[DDIM_STEPS];
    get_ddim_schedule(alphas_bar, DDIM_STEPS, timesteps);

    /* Start from random noise -- use deterministic seed for reproducibility */
    float *h_noise = (float *)malloc(LAT_SZ * sizeof(float));
    unsigned int seed = 42;
    for (int i = 0; i < LAT_SZ; i++) {
        seed = seed * 1103515245 + 12345;
        /* Box-Muller approximation with LCG */
        float u1 = (float)((seed >> 16) & 0x7FFF) / 32767.0f + 1e-7f;
        seed = seed * 1103515245 + 12345;
        float u2 = (float)((seed >> 16) & 0x7FFF) / 32767.0f;
        h_noise[i] = sqrtf(-2.0f * logf(u1)) * cosf(6.2831853f * u2);
    }
    float *latent = gpu_alloc(LAT_SZ);
    CU(cudaMemcpy(latent, h_noise, LAT_SZ * sizeof(float), cudaMemcpyHostToDevice));
    free(h_noise);

    float *latent_next = gpu_alloc(LAT_SZ);

    for (int step = 0; step < DDIM_STEPS; step++) {
        int t = timesteps[step];
        printf("    Step %d/%d (t=%d)\n", step + 1, DDIM_STEPS, t);

        /* Conditional prediction */
        float *eps_cond = run_unet(latent, t, cond);

        /* Unconditional prediction */
        float *eps_uncond = run_unet(latent, t, uncond);

        /* CFG: guided = uncond + scale * (cond - uncond)
           = (1 - scale) * uncond + scale * cond */
        float *eps_guided = gpu_alloc(LAT_SZ);
        gpu_copy(eps_guided, eps_uncond, LAT_SZ);
        k_scale<<<GRID(LAT_SZ), BLK>>>(eps_guided, 1.0f - CFG_SCALE, LAT_SZ);
        k_axpy<<<GRID(LAT_SZ), BLK>>>(eps_guided, eps_cond, CFG_SCALE, LAT_SZ);
        cudaFree(eps_cond); cudaFree(eps_uncond);

        float alpha_t = alphas_bar[step];
        float alpha_prev = (step + 1 < DDIM_STEPS) ? alphas_bar[step + 1] : 1.0f;

        k_ddim_step<<<GRID(LAT_SZ), BLK>>>(latent_next, latent, eps_guided,
                                             alpha_t, alpha_prev, LAT_SZ);
        cudaFree(eps_guided);
        gpu_copy(latent, latent_next, LAT_SZ);
    }
    cudaFree(latent_next);
    return latent;
}

/* ── VAE Decoder ── */

static void vae_resblock(float *out, const float *x, const char *prefix, int C_in, int C_out, int H, int W) {
    char kn[300];
    int spatial = H * W;

    snprintf(kn, sizeof(kn), "%s.norm1.weight", prefix);
    float *n1w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.norm1.bias", prefix);
    float *n1b = load_f32(kn);
    float *h = gpu_alloc(C_in * spatial);
    group_norm(h, x, n1w, n1b, C_in, spatial, 32);
    k_silu<<<GRID(C_in * spatial), BLK>>>(h, C_in * spatial);
    cudaFree(n1w); cudaFree(n1b);

    snprintf(kn, sizeof(kn), "%s.conv1.weight", prefix);
    float *c1w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.conv1.bias", prefix);
    float *c1b = load_f32(kn);
    float *h2 = gpu_alloc(C_out * spatial);
    conv2d(h2, h, c1w, c1b, C_in, C_out, H, W, 3, 1, 1);
    cudaFree(c1w); cudaFree(c1b); cudaFree(h);

    snprintf(kn, sizeof(kn), "%s.norm2.weight", prefix);
    float *n2w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.norm2.bias", prefix);
    float *n2b = load_f32(kn);
    float *h3 = gpu_alloc(C_out * spatial);
    group_norm(h3, h2, n2w, n2b, C_out, spatial, 32);
    k_silu<<<GRID(C_out * spatial), BLK>>>(h3, C_out * spatial);
    cudaFree(n2w); cudaFree(n2b); cudaFree(h2);

    snprintf(kn, sizeof(kn), "%s.conv2.weight", prefix);
    float *c2w = load_f32(kn);
    snprintf(kn, sizeof(kn), "%s.conv2.bias", prefix);
    float *c2b = load_f32(kn);
    conv2d(out, h3, c2w, c2b, C_out, C_out, H, W, 3, 1, 1);
    cudaFree(c2w); cudaFree(c2b); cudaFree(h3);

    if (C_in != C_out) {
        snprintf(kn, sizeof(kn), "%s.nin_shortcut.weight", prefix);
        float *sw = load_f32(kn);
        snprintf(kn, sizeof(kn), "%s.nin_shortcut.bias", prefix);
        float *sb = load_f32(kn);
        float *skip = gpu_alloc(C_out * spatial);
        conv2d(skip, x, sw, sb, C_in, C_out, H, W, 1, 0, 1);
        k_add<<<GRID(C_out * spatial), BLK>>>(out, skip, C_out * spatial);
        cudaFree(sw); cudaFree(sb); cudaFree(skip);
    } else {
        k_add<<<GRID(C_out * spatial), BLK>>>(out, x, C_out * spatial);
    }
}

static float *run_vae_decoder(const float *latent) {
    printf("  Running VAE decoder...\n");
    char kn[300];
    int H = LAT_H, W = LAT_W;

    /* Scale latent by 1/0.18215 (SD 1.5 scaling factor) */
    float *z = gpu_alloc(LAT_SZ);
    gpu_copy(z, latent, LAT_SZ);
    k_scale<<<GRID(LAT_SZ), BLK>>>(z, 1.0f / 0.18215f, LAT_SZ);

    /* post_quant_conv: 4->4, 1x1 */
    float *pqw = load_f32("first_stage_model.post_quant_conv.weight");
    float *pqb = load_f32("first_stage_model.post_quant_conv.bias");
    float *h = gpu_alloc(4 * H * W);
    conv2d(h, z, pqw, pqb, 4, 4, H, W, 1, 0, 1);
    cudaFree(pqw); cudaFree(pqb); cudaFree(z);

    /* conv_in: 4 -> 512, 3x3 */
    float *ciw = load_f32("first_stage_model.decoder.conv_in.weight");
    float *cib = load_f32("first_stage_model.decoder.conv_in.bias");
    float *h512 = gpu_alloc(512 * H * W);
    conv2d(h512, h, ciw, cib, 4, 512, H, W, 3, 1, 1);
    cudaFree(ciw); cudaFree(cib); cudaFree(h);

    /* mid block: resblock1 + attn + resblock2 */
    float *mid1 = gpu_alloc(512 * H * W);
    vae_resblock(mid1, h512, "first_stage_model.decoder.mid.block_1", 512, 512, H, W);
    printf("    VAE mid.block_1 done\n");
    /* Skip mid attention for PoC (saves significant compute) */
    float *mid2 = gpu_alloc(512 * H * W);
    vae_resblock(mid2, mid1, "first_stage_model.decoder.mid.block_2", 512, 512, H, W);
    printf("    VAE mid.block_2 done\n");
    cudaFree(h512); cudaFree(mid1);

    /* Up blocks: 3(512), 2(512), 1(512->256), 0(256->128) */
    int channels[] = {512, 512, 256, 128};

    float *cur = mid2;
    for (int level = 3; level >= 0; level--) {
        int C_in = (level == 3) ? 512 : channels[4 - level - 1];
        int C_out = channels[3 - level];

        for (int blk = 0; blk < 3; blk++) {
            int cin = (blk == 0) ? C_in : C_out;
            snprintf(kn, sizeof(kn), "first_stage_model.decoder.up.%d.block.%d", level, blk);
            float *next = gpu_alloc(C_out * H * W);
            vae_resblock(next, cur, kn, cin, C_out, H, W);
            if (cur != mid2) cudaFree(cur);
            else if (blk > 0 || level < 3) cudaFree(cur);
            cur = next;
        }
        printf("    VAE up.%d blocks done (H=%d, W=%d, C=%d)\n", level, H, W, C_out);

        /* Upsample (levels 3, 2, 1 have upsampling) */
        if (level > 0) {
            snprintf(kn, sizeof(kn), "first_stage_model.decoder.up.%d.upsample.conv.weight", level);
            float *uw = load_f32(kn);
            snprintf(kn, sizeof(kn), "first_stage_model.decoder.up.%d.upsample.conv.bias", level);
            float *ub = load_f32(kn);
            /* Nearest 2x upsample then conv */
            float *up = gpu_alloc(C_out * H * 2 * W * 2);
            k_upsample2x<<<GRID(C_out * H * 2 * W * 2), BLK>>>(up, cur, C_out, H, W);
            H *= 2; W *= 2;
            float *conv_up = gpu_alloc(C_out * H * W);
            conv2d(conv_up, up, uw, ub, C_out, C_out, H, W, 3, 1, 1);
            cudaFree(uw); cudaFree(ub); cudaFree(up); cudaFree(cur);
            cur = conv_up;
            printf("    VAE up.%d upsample done (H=%d, W=%d)\n", level, H, W);
        }
    }
    /* norm_out + SiLU + conv_out */
    float *now = load_f32("first_stage_model.decoder.norm_out.weight");
    float *nob = load_f32("first_stage_model.decoder.norm_out.bias");
    int C_final = 128;
    float *normed = gpu_alloc(C_final * H * W);
    group_norm(normed, cur, now, nob, C_final, H * W, 32);
    k_silu<<<GRID(C_final * H * W), BLK>>>(normed, C_final * H * W);
    cudaFree(now); cudaFree(nob); cudaFree(cur);

    float *cow = load_f32("first_stage_model.decoder.conv_out.weight");
    float *cob = load_f32("first_stage_model.decoder.conv_out.bias");
    float *out = gpu_alloc(3 * H * W);
    conv2d(out, normed, cow, cob, C_final, 3, H, W, 3, 1, 1);
    cudaFree(cow); cudaFree(cob); cudaFree(normed);

    return out;  /* [3, 512, 512] */
}

/* ── BMP Output ── */
static void save_bmp(const float *d_img, int H, int W, const char *path) {
    printf("  Saving BMP to %s...\n", path);
    int img_sz = H * W * 3;
    float *h_img = (float *)malloc(img_sz * sizeof(float));
    CU(cudaMemcpy(h_img, d_img, img_sz * sizeof(float), cudaMemcpyDeviceToHost));

    /* Convert from [3, H, W] CHW float to BGR bottom-up uint8 */
    int row_bytes = W * 3;
    int row_pad = (4 - (row_bytes % 4)) % 4;
    int data_sz = (row_bytes + row_pad) * H;
    uint8_t *pixels = (uint8_t *)calloc(data_sz, 1);

    for (int y = 0; y < H; y++) {
        for (int x = 0; x < W; x++) {
            float r = h_img[0 * H * W + y * W + x];
            float g = h_img[1 * H * W + y * W + x];
            float b = h_img[2 * H * W + y * W + x];
            /* Clamp to [0, 1] */
            r = (r + 1.0f) * 0.5f; g = (g + 1.0f) * 0.5f; b = (b + 1.0f) * 0.5f;
            if (r < 0) r = 0; if (r > 1) r = 1;
            if (g < 0) g = 0; if (g > 1) g = 1;
            if (b < 0) b = 0; if (b > 1) b = 1;
            int by = H - 1 - y;  /* BMP is bottom-up */
            int off = by * (row_bytes + row_pad) + x * 3;
            pixels[off + 0] = (uint8_t)(b * 255.0f);
            pixels[off + 1] = (uint8_t)(g * 255.0f);
            pixels[off + 2] = (uint8_t)(r * 255.0f);
        }
    }
    free(h_img);

    /* BMP header */
    uint8_t header[54] = {0};
    int file_sz = 54 + data_sz;
    header[0] = 'B'; header[1] = 'M';
    memcpy(header + 2, &file_sz, 4);
    int data_off = 54;
    memcpy(header + 10, &data_off, 4);
    int info_sz = 40;
    memcpy(header + 14, &info_sz, 4);
    memcpy(header + 18, &W, 4);
    memcpy(header + 22, &H, 4);
    uint16_t planes = 1;
    memcpy(header + 26, &planes, 2);
    uint16_t bpp = 24;
    memcpy(header + 28, &bpp, 2);
    memcpy(header + 34, &data_sz, 4);

    FILE *fp = fopen(path, "wb");
    if (!fp) { fprintf(stderr, "Cannot open %s for writing\n", path); free(pixels); return; }
    fwrite(header, 1, 54, fp);
    fwrite(pixels, 1, data_sz, fp);
    fclose(fp);
    free(pixels);
    printf("  BMP saved: %dx%d (%d bytes)\n", W, H, file_sz);
}

/* ── Main ── */
int main(void) {
    printf("sd-generate: Stable Diffusion 1.5 inference (proof-of-concept)\n");
    printf("  Prompt: \"%s\"\n", PROMPT);
    printf("  Model: %s\n", MODEL_PATH);

    /* Init CUDA */
    CU(cudaSetDevice(0));
    CUBLAS(cublasCreate(&g_cublas));

    /* Open model and parse SafeTensors header */
    printf("  Loading SafeTensors header...\n");
    g_model_fp = fopen(MODEL_PATH, "rb");
    if (!g_model_fp) { fprintf(stderr, "Cannot open model file\n"); return 1; }

    uint8_t len_buf[8];
    fread(len_buf, 1, 8, g_model_fp);
    int64_t header_len = 0;
    memcpy(&header_len, len_buf, 8);
    printf("  Header length: %lld bytes\n", (long long)header_len);
    char *header_json = (char *)malloc(header_len + 1);
    fread(header_json, 1, header_len, g_model_fp);
    header_json[header_len] = 0;
    g_data_offset = 8 + header_len;

    parse_header(header_json, header_len);
    free(header_json);
    printf("  Parsed %d tensors\n", g_nmeta);

    /* Run CLIP text encoder */
    printf("  Phase 1: Text Encoding\n");
    float *cond = run_clip();
    float *uncond = run_clip_uncond();

    /* Run DDIM sampling */
    printf("  Phase 2: DDIM Denoising\n");
    float *latent = run_ddim(cond, uncond);
    cudaFree(cond); cudaFree(uncond);

    /* Run VAE decoder */
    printf("  Phase 3: VAE Decoding\n");
    float *image = run_vae_decoder(latent);
    cudaFree(latent);

    /* Save BMP */
    save_bmp(image, IMG_H, IMG_W, OUTPUT_PATH);
    cudaFree(image);

    fclose(g_model_fp);
    cublasDestroy(g_cublas);
    printf("Done.\n");
    return 0;
}
