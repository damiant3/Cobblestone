#include <stdio.h>

long long collatz(long long n) {
    long long steps = 0;
    while (n != 1) {
        if (n % 2 == 0) n = n / 2;
        else n = 3 * n + 1;
        steps++;
    }
    return steps;
}

int main(void) {
    printf("%lld\n", collatz(837799));
    return 0;
}
