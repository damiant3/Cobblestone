const std = @import("std");

export fn regright(n: i32) i64 {
    if (n <= 0) return 1;
    return regright(n - 1) * n - n;
}

pub fn main() void {
    std.debug.print("{d}\n", .{regright(12)});
}