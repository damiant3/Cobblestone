// F3 of the fib ladder: the emitted machine code, EXECUTED.
//
// The fibx rung proves the zig plug emits the same CDX as bare metal. It
// does not prove those bytes mean anything -- two emitters can agree on a
// byte stream that no processor would run. This reads the dump back, drops
// the content section into executable memory, and calls into it.
//
// Both dumps are run: fibx.truth (seed-compiled bare metal) and
// fibx.zigout (through the zig plug). They are byte-identical, so they
// must agree here too; running only one would leave open which side was
// being trusted.
//
// Nothing is patched here. finalize resolved every call before serializing,
// which is the whole difference between this and the pre-finalize rung.
const std = @import("std");

// A symbol-map row: `0x<addr> <size> <name>`, exactly what the compiler
// writes beside a .cdx. Addresses are absolute; the content buffer starts
// at bare-metal-load-addr.
const load_addr = 1048576;

const Sym = struct { off: u64, size: u64, name: []const u8 };

const Dump = struct {
    header: []u8,
    content: []u8,
    tail: []u8,
    syms: []const Sym,
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

    fn count(self: *Parser, key: []const u8) u64 {
        return std.fmt.parseInt(u64, self.headerValue(key), 10) catch
            die("{s}: unreadable {s}", .{ self.path, key });
    }

    fn symbols(self: *Parser, gpa: std.mem.Allocator) ![]const Sym {
        var acc: std.ArrayList(Sym) = .empty;
        while (true) {
            const line = self.next();
            if (std.mem.eql(u8, line, ".")) return acc.toOwnedSlice(gpa);
            var t = std.mem.tokenizeScalar(u8, line, ' ');
            const addr_s = t.next() orelse die("{s}: empty symbol row", .{self.path});
            const size_s = t.next() orelse die("{s}: symbol row has no size: '{s}'", .{ self.path, line });
            const name = t.next() orelse die("{s}: symbol row has no name: '{s}'", .{ self.path, line });
            if (!std.mem.startsWith(u8, addr_s, "0x"))
                die("{s}: symbol address is not hex: '{s}'", .{ self.path, line });
            const addr = std.fmt.parseInt(u64, addr_s[2..], 16) catch
                die("{s}: unreadable symbol address: '{s}'", .{ self.path, line });
            if (addr < load_addr)
                die("{s}: symbol below the load address: '{s}'", .{ self.path, line });
            try acc.append(gpa, .{
                .off = addr - load_addr,
                .size = std.fmt.parseInt(u64, size_s, 10) catch
                    die("{s}: unreadable symbol size: '{s}'", .{ self.path, line }),
                .name = name,
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
            die("{s}: section says {d} bytes, dump carries {d}", .{ self.path, want, acc.items.len });
        return acc.toOwnedSlice(gpa);
    }
};

fn parse(gpa: std.mem.Allocator, path: []const u8, text: []const u8) !Dump {
    var p = Parser{ .lines = std.mem.splitScalar(u8, text, '\n'), .path = path };
    if (p.count("check-errors") != 0) die("{s}: subject had check errors", .{path});
    _ = p.count("ir-defs");
    if (p.count("emit-errors") != 0) die("{s}: emission put errors in the bag", .{path});
    const header_len = p.count("header-len");
    const content_len = p.count("content-len");
    const tail_len = p.count("tail-len");
    p.expect("--- symbols ---");
    const syms = try p.symbols(gpa);
    p.expect("--- header ---");
    const header = try p.bytes(gpa, header_len);
    p.expect("--- content ---");
    const content = try p.bytes(gpa, content_len);
    p.expect("--- tail ---");
    const tail = try p.bytes(gpa, tail_len);
    if (!std.mem.startsWith(u8, header, "CDX1"))
        die("{s}: header does not start with the CDX1 magic", .{path});
    return .{ .header = header, .content = content, .tail = tail, .syms = syms };
}

fn lookup(syms: []const Sym, name: []const u8) ?Sym {
    for (syms) |s| if (std.mem.eql(u8, s.name, name)) return s;
    return null;
}

// The generated prologue pushes rbp/rbx/r12/r13/r14 but never r15, so this
// does not take zig's word for what survives: every volatile and every
// callee-saved register is declared clobbered. r10 carries the stack limit
// the stack-guard compares against; zero means the guard never fires -- and
// after finalize the guard is a real branch to __out_of_memory, so this
// matters more than it did before.
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
// self-calls are the only thing finalize had to resolve inside it. double
// is a leaf that fits in five bytes with no frame at all, so it is an
// independent check that the symbol map points where it claims to.
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

fn runFunc(d: Dump, base: usize, name: []const u8, cases: []const Case) bool {
    const sym = lookup(d.syms, name) orelse {
        std.debug.print("no '{s}' in the symbol map; names containing it:\n", .{name});
        for (d.syms) |s| if (std.mem.indexOf(u8, s.name, name) != null)
            std.debug.print("  {d} {s}\n", .{ s.off, s.name });
        die("cannot locate {s}", .{name});
    };
    std.debug.print("  {s} at {d} ({d} bytes)\n", .{ name, sym.off, sym.size });
    var ok = true;
    for (cases) |c| {
        const got = callCodex(base + sym.off, c.n);
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

    const page = std.heap.pageSize();
    const len = (d.content.len + page - 1) / page * page;
    const mem = try std.posix.mmap(
        null,
        len,
        .{ .READ = true, .WRITE = true, .EXEC = true },
        .{ .TYPE = .PRIVATE, .ANONYMOUS = true },
        -1,
        0,
    );
    @memcpy(mem[0..d.content.len], d.content);

    std.debug.print("{s}: {d} header + {d} content + {d} tail bytes, {d} symbols\n", .{
        path, d.header.len, d.content.len, d.tail.len, d.syms.len,
    });

    var ok = true;
    for (subjects) |s| ok = runFunc(d, @intFromPtr(mem.ptr), s.name, s.cases) and ok;
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
