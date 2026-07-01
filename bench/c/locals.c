#include <stdio.h>

long long compute(int n) {
    long long acc = 0;
    for (int i = n; i > 0; i--) {
        long long a = i + 1, b = i * 2, c = i - 3;
        long long d = a * b, e = b + c, f = c * a;
        acc += d + e + f;
    }
    return acc;
}

int main(void) {
    printf("%lld\n", compute(1000));
    return 0;
}
