namespace Codex.Syntax;

public enum TokenKind
{
    // Structural
    EndOfFile,
    Newline,
    Indent,
    Dedent,

    // Literals
    IntegerLiteral,
    NumberLiteral,
    TextLiteral,
    CharLiteral,
    TrueKeyword,
    FalseKeyword,

    // Interpolated strings
    InterpolatedStart,      // opening " of an interpolated string
    InterpolatedEnd,        // closing " of an interpolated string
    TextFragment,           // literal text segment between { } holes
    InterpolatedExprStart,  // #{ inside an interpolated string
    InterpolatedExprEnd,    // } that closes an interpolated expression

    // Identifiers
    Identifier,         // lowercase-hyphenated
    TypeIdentifier,     // Capitalized

    // Prose
    ProseText,          // natural language text in prose mode
    ChapterHeader,      // "Chapter: ..."
    SectionHeader,      // "Section: ..."

    // Keywords
    LetKeyword,
    InKeyword,
    IfKeyword,
    IsKeyword,
    OtherwiseKeyword,
    ThenKeyword,
    ElseKeyword,
    WhenKeyword,
    WhereKeyword,
    SuchThatKeyword,
    ActKeyword,
    EndKeyword,
    RecordKeyword,
    CitesKeyword,
    ClaimKeyword,
    ProofKeyword,
    QedKeyword,
    ForAllKeyword,
    ThereExistsKeyword,
    LinearKeyword,
    EffectKeyword,
    WithKeyword,

    // Operators
    Equals,             // =
    Colon,              // :
    Arrow,              // → or ->
    LeftArrow,          // ← or <-
    Pipe,               // |
    Ampersand,          // &
    Plus,               // +
    Minus,              // -
    Star,               // *
    Slash,              // /
    Caret,              // ^
    PlusPlus,           // ++
    ColonColon,         // ::
    DoubleEquals,       // ==
    NotEquals,          // ≠ or /=
    LessThan,           // <
    GreaterThan,        // >
    LessOrEqual,        // ≤ or <=
    GreaterOrEqual,     // ≥ or >=
    TripleEquals,       // ≡ or ===
    Turnstile,          // ⊢ or |-
    LinearProduct,      // ⊗ or (**)
    ForAllSymbol,       // ∀
    ExistsSymbol,       // ∃

    // Delimiters
    LeftParen,          // (
    RightParen,         // )
    LeftBracket,        // [
    RightBracket,       // ]
    LeftBrace,          // {
    RightBrace,         // }
    Comma,              // ,
    Dot,                // .
    DashGreater,        // -> (ASCII arrow)
    Underscore,         // _
    Backslash,          // \

    // Special
    Error,              // unrecognized input
}
