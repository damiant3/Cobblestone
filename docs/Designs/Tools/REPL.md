# REPL Usage Example

Run the REPL with `codex repl`. Here is an example session:

```
$ codex repl
Codex REPL 0.1.0-bootstrap
Type :help for commands, :quit to exit.

codex> 2 + 3
5

codex> square (x) = x * x
square : Integer → Integer

codex> square 7
49

codex> greeting (name) = "Hello, " ++ name ++ "!"
greeting : Text → Text

codex> greeting "World"
Hello, World!

codex> :type square 5
square 5 : Integer

codex> :defs
square (x) = x * x
greeting (name) = "Hello, " ++ name ++ "!"

codex> :reset
Session reset.

codex> :quit
```

## Meta-commands

| Command | Description |
|---------|-------------|
| `:help`, `:h` | Show available commands |
| `:quit`, `:q` | Exit the REPL |
| `:type`, `:t` | Show the type of an expression without evaluating |
| `:defs` | List all definitions in the current session |
| `:reset` | Clear all session definitions |

## Notes

- Definitions accumulate across the session
- Type definitions (starting with uppercase) are also supported
- Expression evaluation shells out to `dotnet build + run` (~2s per eval)
- Future: interpreter mode for instant evaluation
