const std = @import("std");

export fn gcd(a0: i32, b0: i32) i32 {
    var a = a0;
    var b = b0;
    while (b != 0) {
        const t = b;
        b = @rem(a, b);
        a = t;
    }
    return a;
}

pub fn main() void {
    std.debug.print("{d}\n", .{gcd(46368, 28657)});
}