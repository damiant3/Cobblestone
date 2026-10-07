#include <stdlib.h>
static int fail_allocation;
static void *test_realloc(void *p, size_t n) {
    return fail_allocation ? NULL : realloc(p, n);
}
#define realloc test_realloc
#define main codex_vm_main
#include "codex-vm.c"
#undef main
#undef realloc

static int failures;
static void check(int ok, const char *name) {
    printf("%s %s\n", ok ? "PASS" : "FAIL", name);
    if (!ok) failures++;
}

static void put32(unsigned char *p, unsigned long n) {
    p[0] = (unsigned char)(n >> 24); p[1] = (unsigned char)(n >> 16);
    p[2] = (unsigned char)(n >> 8); p[3] = (unsigned char)n;
}

static void segment(unsigned long seq, int flags, const char *data) {
    unsigned char frame[128] = {0};
    int n = (int)strlen(data);
    frame[12] = 8; frame[14] = 0x45; frame[17] = (unsigned char)(40 + n);
    frame[23] = 6;
    frame[26] = 10; frame[28] = 2; frame[29] = 15;
    frame[30] = 10; frame[32] = 2; frame[33] = 2;
    frame[34] = 0x0A; frame[35] = 0x21;
    frame[36] = 0xC3; frame[37] = 0x50;
    put32(frame + 38, seq); put32(frame + 42, 100001);
    frame[46] = 0x50; frame[47] = (unsigned char)flags;
    memcpy(frame + 54, data, n);
    nat_handle_tx(frame, 54 + n);
}

static unsigned long last_ack(void) {
    if (rx_queue_count <= 0) {
        check(0, "ACK frame exists");
        return 0;
    }
    unsigned char *p = rx_queue[(rx_queue_head + rx_queue_count - 1) % RX_QUEUE_SIZE].data + 42;
    return ((unsigned long)p[0] << 24) | ((unsigned long)p[1] << 16) | ((unsigned long)p[2] << 8) | p[3];
}

static NatConn *reset(unsigned long isn, int forwarded, int state) {
    NatConn *c = &nat_conns[0];
    if (c->txbuf) free(c->txbuf);
    memset(c, 0, sizeof(*c));
    c->active = 1; c->sock = INVALID_SOCKET; c->guest_port = 2593;
    c->dst_port = 50000; c->dst_ip[0] = 10; c->dst_ip[2] = 2; c->dst_ip[3] = 2;
    c->seq_offset = isn; c->guest_isn = isn; c->ack_offset = 100000;
    c->forwarded = forwarded; c->state = state;
    rx_queue_count = 0; rx_queue_head = 0;
    return c;
}

static int queued(NatConn *c, const char *s) {
    int n = (int)strlen(s);
    return c->txlen == n && (!n || memcmp(c->txbuf, s, n) == 0);
}

int main(void) {
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2,2), &wsa)) return 2;
    for (int forwarded = 0; forwarded <= 1; forwarded++) {
        NatConn *c = reset(99, forwarded, 2);
        segment(100, 0x10, "abc");
        check(queued(c, "abc") && last_ack() == 103, "first contiguous segment");
        segment(100, 0x10, "abc");
        check(queued(c, "abc") && last_ack() == 103, "duplicate re-ACK without forwarding");
        segment(101, 0x10, "bcd");
        check(queued(c, "abcd") && last_ack() == 104, "overlap forwards only unseen suffix");
        segment(106, 0x10, "gh");
        check(queued(c, "abcd") && last_ack() == 104, "gap withheld at cumulative ACK");
        segment(104, 0x10, "ef");
        segment(106, 0x10, "gh");
        check(queued(c, "abcdefgh") && last_ack() == 108, "gap fill and retransmit preserve stream");
        segment(100, 0x10, "ab");
        check(queued(c, "abcdefgh") && last_ack() == 108, "old segment cannot regress ACK");
    }
    NatConn *c = reset(0xFFFFFFFDUL, 1, 2);
    segment(0xFFFFFFFEUL, 0x10, "abc");
    segment(0xFFFFFFFEUL, 0x10, "abc");
    segment(0xFFFFFFFFUL, 0x10, "bcde");
    check(queued(c, "abcde") && last_ack() == 3, "wraparound duplicate and overlap");
    c = reset(99, 0, 1);
    segment(100, 0x10, "abc"); segment(100, 0x10, "abc");
    check(queued(c, "abc") && last_ack() == 103, "connecting outbound queue deduplicates");
    c = reset(99, 1, 2);
    fail_allocation = 1; segment(100, 0x10, "abc"); fail_allocation = 0;
    check(queued(c, "") && last_ack() == 100, "allocation failure does not acknowledge lost bytes");
    segment(100, 0x10, "abc");
    check(queued(c, "abc") && last_ack() == 103, "allocation retry retains all bytes");
    c = reset(99, 1, 2);
    segment(103, 0x11, "");
    check(c->state == 2 && last_ack() == 100, "FIN beyond gap does not close");
    segment(100, 0x11, "abc");
    check(queued(c, "abc") && c->state == 3 && last_ack() == 104, "FIN admits payload before half-close");
    segment(100, 0x11, "abc");
    check(queued(c, "abc") && last_ack() == 104, "duplicate FIN does not forward or advance");
    nat_conn_free(c);
    WSACleanup();
    return failures ? 1 : 0;
}
