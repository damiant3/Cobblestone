const std = @import("std");

export fn collatz(n0: i64) i64 {
    var n = n0;
    var steps: i64 = 0;
    while (n != 1) {
        if (@rem(n, 2) == 0) n = @divTrunc(n, 2) else n = 3 * n + 1;
        steps += 1;
    }
    return steps;
}

pub fn main() void {
    std.debug.print("{d}\n", .{collatz(837799)});
}