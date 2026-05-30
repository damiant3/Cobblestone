namespace Codex.Types;

// Language-level builtin function names — the "__"-prefixed primitives a
// Codex program can invoke. Each name is registered in BuiltinChapters with
// a typed signature (the contract) and dispatched by every code-generating
// backend (the implementation). A single rename used to require hunting the
// literal through six files; now the constant is the anchor.
//
// These are NOT the per-backend runtime helpers (__start, __itoa, __ipow,
// __memmove, etc.) — those are implementation-internal symbols and do not
// appear in BuiltinChapters.
public static class Builtins
{
    public const string RecordSet = "__record-set";

    public const string HeapSave = "__heap-save";
    public const string HeapRestore = "__heap-restore";
    public const string HeapAdvance = "__heap-advance";

    public const string ListWithCapacity = "__list-with-capacity";

    public const string BufWriteByte = "__buf-write-byte";
    public const string BufWriteBytes = "__buf-write-bytes";
    public const string BufReadBytes = "__buf-read-bytes";

    public const string LinkedListEmpty = "__linked-list-empty";
    public const string LinkedListPush = "__linked-list-push";
    public const string LinkedListToList = "__linked-list-to-list";
}
