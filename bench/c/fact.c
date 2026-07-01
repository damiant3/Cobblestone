#include <stdio.h>

long long fact(int n) {
    if (n == 0) return 1;
    return (long long)n * fact(n - 1);
}

int main(void) {
    printf("%lld\n", fact(20));
    return 0;
}
