#include <stdio.h>

long long sum(int n) {
    long long s = 0;
    for (int i = 1; i <= n; i++)
        s += i;
    return s;
}

int main(void) {
    printf("%lld\n", sum(1000000));
    return 0;
}
