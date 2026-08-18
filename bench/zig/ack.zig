const std = @import("std");

export fn ack(m: i32, n: i32) i64 {
    if (m == 0) return n + 1;
    if (n == 0) return ack(m - 1, 1);
    return ack(m - 1, @intCast(ack(m, n - 1)));
}

pub fn main() void {
    std.debug.print("{d}\n", .{ack(3, 9)});
}