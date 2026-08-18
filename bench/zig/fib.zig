const std = @import("std");

export fn fib(n: i32) i64 {
    if (n <= 1) return n;
    return fib(n - 1) + fib(n - 2);
}

pub fn main() void {
    std.debug.print("{d}\n", .{fib(35)});
}