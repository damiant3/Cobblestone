// Nine benchmark functions, one per bench/c file, same signatures.
// Loops are written in the function body (for/while, or a self tail call the
// compiler turns into a loop), never as an inner `let rec go`, which F#
// compiles to a SEPARATE method that a listing of the outer name would miss.
// Run: Bench <name>; the JIT listing of that one function is taken with
// DOTNET_JitDisasm=<name> DOTNET_TieredCompilation=0 (FullOpts).
module Bench

open System.Runtime.CompilerServices

[<MethodImpl(MethodImplOptions.NoInlining)>]
let rec fib (n: int) : int64 = if n <= 1 then int64 n else fib (n - 1) + fib (n - 2)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let rec fact (n: int) : int64 = if n = 0 then 1L else int64 n * fact (n - 1)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let rec gcd (a: int) (b: int) : int = if b = 0 then a else gcd b (a % b)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let sum (n: int) : int64 =
    let mutable s = 0L
    for i in 1 .. n do
        s <- s + int64 i
    s

[<MethodImpl(MethodImplOptions.NoInlining)>]
let rec ack (m: int) (n: int) : int64 =
    if m = 0 then int64 (n + 1)
    elif n = 0 then ack (m - 1) 1
    else ack (m - 1) (int (ack m (n - 1)))

[<MethodImpl(MethodImplOptions.NoInlining)>]
let rec tak (x: int64) (y: int64) (z: int64) : int64 =
    if y >= x then z else tak (tak (x - 1L) y z) (tak (y - 1L) z x) (tak (z - 1L) x y)

[<MethodImpl(MethodImplOptions.NoInlining)>]
let collatz (n0: int64) : int64 =
    let mutable n = n0
    let mutable steps = 0L
    while n <> 1L do
        if n % 2L = 0L then n <- n / 2L else n <- 3L * n + 1L
        steps <- steps + 1L
    steps

[<MethodImpl(MethodImplOptions.NoInlining)>]
let compute (n: int) : int64 =
    let mutable acc = 0L
    let mutable i = n
    while i > 0 do
        let a = int64 (i + 1)
        let b = int64 (i * 2)
        let c = int64 (i - 3)
        let d = a * b
        let e = b + c
        let f = c * a
        acc <- acc + d + e + f
        i <- i - 1
    acc

[<MethodImpl(MethodImplOptions.NoInlining)>]
let rec regright (n: int) : int64 = if n <= 0 then 1L else regright (n - 1) * int64 n - int64 n

[<EntryPoint>]
let main (args: string[]) : int =
    let name = if args.Length > 0 then args.[0] else "fib"
    match name with
    | "fib" -> printfn "%d" (fib 35); 0
    | "fact" -> printfn "%d" (fact 20); 0
    | "gcd" -> printfn "%d" (gcd 46368 28657); 0
    | "sum" -> printfn "%d" (sum 1000000); 0
    | "ack" -> printfn "%d" (ack 3 9); 0
    | "tak" -> printfn "%d" (tak 24L 16L 8L); 0
    | "collatz" -> printfn "%d" (collatz 837799L); 0
    | "locals" -> printfn "%d" (compute 1000); 0
    | "regright" -> printfn "%d" (regright 12); 0
    | _ -> eprintfn "unknown bench %s" name; 2