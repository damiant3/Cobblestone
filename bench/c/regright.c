#include <stdio.h>

long long regright(int n) {
    if (n <= 0) return 1;
    return regright(n - 1) * n - n;
}

int main(void) {
    printf("%lld\n", regright(12));
    return 0;
}
