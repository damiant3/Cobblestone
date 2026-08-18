const std = @import("std");

export fn compute(n: i32) i64 {
    var acc: i64 = 0;
    var i: i32 = n;
    while (i > 0) : (i -= 1) {
        const a: i64 = i + 1;
        const b: i64 = i * 2;
        const c: i64 = i - 3;
        const d = a * b;
        const e = b + c;
        const f = c * a;
        acc += d + e + f;
    }
    return acc;
}

pub fn main() void {
    std.debug.print("{d}\n", .{compute(1000)});
}