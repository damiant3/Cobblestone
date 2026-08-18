const std = @import("std");

export fn sum(n: i32) i64 {
    var s: i64 = 0;
    var i: i32 = 1;
    while (i <= n) : (i += 1)
        s += i;
    return s;
}

pub fn main() void {
    std.debug.print("{d}\n", .{sum(1000000)});
}