namespace Codex.Emit;

// Shared identifier sanitization for text emit backends. Every target-language
// emitter needs to map Codex's hyphenated identifiers (e.g. "compile-frontend")
// to a target-language-legal form, then escape any collision with the target's
// reserved keyword set.
//
// The common shape is:
//   1. name.Replace('-', '_')
//   2. if the result is a reserved keyword in the target, wrap with
//      per-language affixes (Rust: r#name, Ada: name_v, C-family: _name)
//
// Backends hold their own reserved-keyword set (a HashSet<string>) and call
// Sanitize with their escape affixes. C#'s SanitizeIdentifier has a second
// escape stage for Object-inherited method names and keeps its own flow —
// the keyword check still uses this helper's basic shape.
public static class NameSanitizer
{
    public static string Sanitize(
        string name,
        IReadOnlySet<string> reservedKeywords,
        string escapePrefix,
        string escapeSuffix = "")
    {
        string sanitized = name.Replace('-', '_');
        return reservedKeywords.Contains(sanitized)
            ? $"{escapePrefix}{sanitized}{escapeSuffix}"
            : sanitized;
    }
}
