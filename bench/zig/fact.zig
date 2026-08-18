const std = @import("std");

export fn fact(n: i32) i64 {
    if (n == 0) return 1;
    return @as(i64, n) * fact(n - 1);
}

pub fn main() void {
    std.debug.print("{d}\n", .{fact(20)});
}