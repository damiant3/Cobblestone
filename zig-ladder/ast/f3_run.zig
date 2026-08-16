// F3 of the fib ladder: the emitted machine code, EXECUTED.
//
// The fibx rung proves the zig plug emits the same 45,432 bytes as bare
// metal. It does not prove those bytes mean anything -- two emitters can
// agree on a byte stream that no processor would run. This reads the dump
// back, resolves the calls that finalize would have resolved, drops the
// buffer into executable memory, and calls fib.
//
// Both dumps are run: fibx.truth (seed-compiled bare metal) and
// fibx.zigout (through the zig plug). They are byte-identical, so they
// must agree here too; running only one would leave open which side was
// being trusted.
const std = @import("std");

const Pair = struct { num: u64, name: []const u8 };

const Dump = struct {
    code_len: u64,
    code: []u8,
    funcs: []const Pair,
    calls: []const Pair,
};

// 0.16 routes file reads through an Io; a blocking one is two lines.
fn readFile(io: std.Io, gpa: std.mem.Allocator, path: []const u8) ![]u8 {
    return std.Io.Dir.cwd().readFileAlloc(io, path, gpa, .limited(64 << 20));
}

fn die(comptime fmt: []const u8, args: anytype) noreturn {
    std.debug.print("F3 FAIL: " ++ fmt ++ "\n", args);
    std.process.exit(1);
}

const Parser = struct {
    lines: std.mem.SplitIterator(u8, .scalar),
    path: []const u8,

    fn next(self: *Parser) []const u8 {
        const raw = self.lines.next() orelse die("{s}: dump ended early", .{self.path});
        // The truth side arrives over a serial line with CRLF; the oracle
        // strips it with tr, and so must this.
        return std.mem.trimEnd(u8, raw, "\r");
    }

    fn expect(self: *Parser, want: []const u8) void {
        const got = self.next();
        if (!std.mem.eql(u8, got, want))
            die("{s}: expected '{s}', got '{s}'", .{ self.path, want, got });
    }

    // Header lines are `<key> <value>`; the value is all this needs.
    fn headerValue(self: *Parser, key: []const u8) []const u8 {
        const line = self.next();
        if (!std.mem.startsWith(u8, line, key) or line.len <= key.len + 1)
            die("{s}: expected a '{s}' line, got '{s}'", .{ self.path, key, line });
        return line[key.len + 1 ..];
    }

    // `--- funcs ---` and `--- calls ---` are both `<integer> <name>`,
    // terminated by a lone dot.
    fn pairs(self: *Parser, gpa: std.mem.Allocator) ![]const Pair {
        var acc: std.ArrayList(Pair) = .empty;
        while (true) {
            const line = self.next();
            if (std.mem.eql(u8, line, ".")) return acc.toOwnedSlice(gpa);
            const sp = std.mem.indexOfScalar(u8, line, ' ') orelse
                die("{s}: table row has no space: '{s}'", .{ self.path, line });
            try acc.append(gpa, .{
                .num = std.fmt.parseInt(u64, line[0..sp], 10) catch
                    die("{s}: table row has no integer: '{s}'", .{ self.path, line }),
                .name = line[sp + 1 ..],
            });
        }
    }

    fn bytes(self: *Parser, gpa: std.mem.Allocator, want: u64) ![]u8 {
        var acc: std.ArrayList(u8) = .empty;
        while (true) {
            const line = self.next();
            if (std.mem.eql(u8, line, ".")) break;
            var toks = std.mem.tokenizeScalar(u8, line, ' ');
            while (toks.next()) |t| try acc.append(gpa, std.fmt.parseInt(u8, t, 10) catch
                die("{s}: '{s}' is not a byte", .{ self.path, t }));
        }
        if (acc.items.len != want)
            die("{s}: code-len says {d}, dump carries {d}", .{ self.path, want, acc.items.len });
        return acc.toOwnedSlice(gpa);
    }
};

fn parse(gpa: std.mem.Allocator, path: []const u8, text: []const u8) !Dump {
    var p = Parser{ .lines = std.mem.splitScalar(u8, text, '\n'), .path = path };
    const errs = p.headerValue("check-errors");
    if (!std.mem.eql(u8, errs, "0")) die("{s}: subject had {s} check errors", .{ path, errs });
    _ = p.headerValue("ir-defs");
    _ = p.headerValue("fo-names");
    const code_len = std.fmt.parseInt(u64, p.headerValue("code-len"), 10) catch
        die("{s}: unreadable code-len", .{path});
    p.expect("--- funcs ---");
    const funcs = try p.pairs(gpa);
    p.expect("--- calls ---");
    const calls = try p.pairs(gpa);
    p.expect("--- code ---");
    return .{ .code_len = code_len, .code = try p.bytes(gpa, code_len), .funcs = funcs, .calls = calls };
}

fn lookup(funcs: []const Pair, name: []const u8) ?u64 {
    for (funcs) |f| if (std.mem.eql(u8, f.name, name)) return f.num;
    return null;
}

// The offset table records where each function starts, not how long it is,
// so a function runs to whichever function starts next.
fn functionEnd(d: Dump, start: u64) u64 {
    var end = d.code_len;
    for (d.funcs) |f| if (f.num > start and f.num < end) {
        end = f.num;
    };
    return end;
}

// apply-call-patches-direct, transcribed: rel32 = target - (call site + 5),
// written into the four bytes after the E8. Every displacement is relative
// to the buffer, which is why the buffer can be loaded anywhere.
//
// Returns the sites that resolved to nothing. Bare metal diagnoses those at
// finalize (CDX unresolved-func-offset); here they are only fatal if one
// lands inside a function about to be called, which runFunc checks.
fn patchCalls(gpa: std.mem.Allocator, d: Dump) ![]const u64 {
    var unresolved: std.ArrayList(u64) = .empty;
    const trap = lookup(d.funcs, "__unresolved_trap");
    for (d.calls) |c| {
        const target = lookup(d.funcs, c.name) orelse trap orelse {
            try unresolved.append(gpa, c.num);
            continue;
        };
        const rel: i32 = @intCast(@as(i64, @intCast(target)) - @as(i64, @intCast(c.num + 5)));
        std.mem.writeInt(i32, d.code[c.num + 1 ..][0..4], rel, .little);
    }
    return unresolved.toOwnedSlice(gpa);
}

// The generated prologue pushes rbp/rbx/r12/r13/r14 but never r15, so this
// does not take zig's word for what survives: every volatile and every
// callee-saved register is declared clobbered. r10 carries the stack limit
// the stack-guard compares against; zero means the guard never fires.
fn callCodex(entry: usize, arg: i64) i64 {
    return asm volatile ("call *%[f]"
        : [ret] "={rax}" (-> i64),
        : [f] "r" (entry),
          [a] "{rdi}" (arg),
          [lim] "{r10}" (@as(i64, 0)),
        : .{ .rcx = true, .rdx = true, .rsi = true, .rdi = true, .r8 = true, .r9 = true, .r10 = true, .r11 = true, .rbx = true, .r12 = true, .r13 = true, .r14 = true, .r15 = true, .memory = true, .cc = true });
}

const Case = struct { n: i64, want: i64 };

// fib is the recursive one -- the reason F3 is interesting, since its two
// self-calls are the only thing the patcher has to get right. double is a
// leaf that fits in five bytes with no frame at all, so it is an
// independent check that the offsets carve where they claim to.
const subjects = [_]struct { name: []const u8, cases: []const Case }{
    .{ .name = "fib", .cases = &.{
        .{ .n = 0, .want = 0 },
        .{ .n = 1, .want = 1 },
        .{ .n = 10, .want = 55 },
        .{ .n = 20, .want = 6765 },
        .{ .n = 30, .want = 832040 },
    } },
    .{ .name = "double", .cases = &.{
        .{ .n = 0, .want = 0 },
        .{ .n = 21, .want = 42 },
        .{ .n = -3, .want = -6 },
    } },
};

fn runFunc(d: Dump, base: usize, unresolved: []const u64, name: []const u8, cases: []const Case) bool {
    const start = lookup(d.funcs, name) orelse {
        std.debug.print("no '{s}' in the offset table; names containing it:\n", .{name});
        for (d.funcs) |f| if (std.mem.indexOf(u8, f.name, name) != null)
            std.debug.print("  {d} {s}\n", .{ f.num, f.name });
        die("cannot locate {s}", .{name});
    };
    const end = functionEnd(d, start);
    for (unresolved) |pos| if (pos >= start and pos < end)
        die("a call at {d} is inside {s} and resolves to nothing", .{ pos, name });

    std.debug.print("  {s} at {d}..{d} ({d} bytes)\n", .{ name, start, end, end - start });
    var ok = true;
    for (cases) |c| {
        const got = callCodex(base + start, c.n);
        if (got != c.want) ok = false;
        std.debug.print("    {s}({d}) = {d}  (want {d}) {s}\n", .{
            name, c.n, got, c.want, if (got == c.want) "ok" else "WRONG",
        });
    }
    return ok;
}

fn runOne(io: std.Io, gpa: std.mem.Allocator, path: []const u8) !bool {
    const text = try readFile(io, gpa, path);
    const d = try parse(gpa, path, text);
    const unresolved = try patchCalls(gpa, d);

    const page = std.heap.pageSize();
    const len = (d.code.len + page - 1) / page * page;
    const mem = try std.posix.mmap(
        null,
        len,
        .{ .READ = true, .WRITE = true, .EXEC = true },
        .{ .TYPE = .PRIVATE, .ANONYMOUS = true },
        -1,
        0,
    );
    @memcpy(mem[0..d.code.len], d.code);

    std.debug.print("{s}: {d} bytes, {d} functions, {d} call sites, {d} unresolved\n", .{
        path, d.code_len, d.funcs.len, d.calls.len, unresolved.len,
    });

    var ok = true;
    for (subjects) |s| ok = runFunc(d, @intFromPtr(mem.ptr), unresolved, s.name, s.cases) and ok;
    return ok;
}

pub fn main() !void {
    const gpa = std.heap.page_allocator;
    var threaded: std.Io.Threaded = .init(gpa, .{});
    defer threaded.deinit();
    const io = threaded.io();
    var ok = true;
    for ([_][]const u8{ "fibx.truth", "fibx.zigout" }) |path| {
        ok = try runOne(io, gpa, path) and ok;
    }
    if (!ok) die("the emitted code computed the wrong answers", .{});
    std.debug.print("F3 PASS: emitted machine code runs; both dumps agree with fib\n", .{});
}
