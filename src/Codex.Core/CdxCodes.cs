namespace Codex.Core;

// Central registry for CDX diagnostic codes.
//
// Codes are plain integers in the compiler's internal data. The "CDX####"
// form is purely a rendering convention, applied at display time by
// Diagnostic.ToString via FormatCode. Internal code never carries the
// prefix — call sites reference named constants (e.g., CdxCodes.UndefinedName).
//
// Self-host parallel: Codex.Codex/Core/CdxCodes.codex. Intentionally not
// shared — two compilers, two languages, parity not share-ity.
//
// Ranges (by phase where first emitted):
//   0xxx  — infrastructure / lexer
//   1xxx  — parser / prose-mode validation
//   2xxx  — type checker / linearity / unifier
//   3xxx  — name resolver / chapter scoper / citations
//   40xx  — proof checker
//   405x  — capability checker
//   9xxx  — reserved for compiler-internal (fuel exhaustion etc.)

public enum CdxPhase
{
    Infrastructure,
    Lexer,
    Parser,
    ProseValidation,
    NameResolver,
    TypeChecker,
    Linearity,
    ProofChecker,
    CapabilityChecker,
    CodeGen,
}

public sealed record CdxCodeInfo(
    int Code,
    string Name,
    DiagnosticSeverity Severity,
    CdxPhase Phase,
    string Summary);

public static class CdxCodes
{
    // ---- Infrastructure / Lexer (0xxx) ----
    public const int TooManyErrors = 1;
    public const int UnexpectedCharacter = 2;
    public const int IndentationMismatch = 3;
    public const int UnterminatedInterpolation = 4;
    public const int InvalidTabEscape = 5;
    public const int InvalidCarriageReturnEscape = 6;
    public const int UnterminatedTextLiteral = 7;

    // Page-marker validation (CLI-level, runs before lexer). 10–19 reserved.
    public const int MissingPageMarker = 10;

    // ---- Parser (1xxx) ----
    public const int ExpectedTokenKind = 1000;
    public const int ExpectedDefinition = 1001;
    public const int ExpectedTypeParameterName = 1002;
    public const int ExpectedType = 1010;
    public const int ExpectedExpression = 1020;
    public const int ExpectedThenKeyword = 1021;
    public const int ExpectedElseKeyword = 1022;
    public const int ExpectedInKeyword = 1023;
    public const int ExpectedFieldName = 1024;
    public const int UnterminatedListLiteral = 1025;
    public const int ExpectedMatchBranch = 1030;
    public const int ExpectedPattern = 1031;
    public const int ExpectedArrowAfterPattern = 1032;
    public const int EmptyDoBlock = 1040;
    public const int ExpectedTypeDefBody = 1050;
    public const int ExpectedRecordFieldName = 1051;
    public const int ExpectedConstructorName = 1052;
    public const int ExpectedPageOrEffectKeyword = 1070;
    public const int EffectRequiresOperation = 1071;
    public const int HandleClauseMissingResume = 1080;
    public const int HandleMissingClauses = 1081;

    // Page-marker validation (107x)
    public const int PageCountMismatch = 1072;
    public const int DuplicatePage = 1073;
    public const int MissingPage = 1074;
    public const int DuplicateChapterInQuire = 1075;

    // Prose-mode warnings (11xx)
    public const int ProseMissingChapter = 1100;
    public const int ProseFunctionNameMismatch = 1101;
    public const int ProseParameterNameMismatch = 1102;
    public const int ProseClaimWithoutNotation = 1105;
    public const int FileMissingChapter = 1106;
    public const int FileMultipleChapters = 1107;

    // ---- Type checker / Unifier (2xxx) ----
    public const int IrError = 2000;
    public const int TypeMismatch = 2001;
    public const int UnknownName = 2002;
    public const int ArithmeticRequiresNumeric = 2003;
    public const int RecordFieldNotFound = 2005;
    public const int InfiniteType = 2010;
    public const int NonExhaustiveMatch = 2020;
    public const int EffectLabelMustBeName = 2030;
    public const int EffectNotDeclared = 2031;
    public const int TypeArgArityMismatch = 2032;
    public const int LetBindsEffectfulValue = 2033;

    // Linearity (204x)
    public const int LinearUnused = 2040;
    public const int LinearUsedTwice = 2041;
    public const int LinearInconsistentBranches = 2042;
    public const int LinearCapturedByClosure = 2043;

    // Context notes
    public const int InDefinition = 2099;

    // ---- Name resolver / citations (3xxx) ----
    public const int DuplicateDefinition = 3001;
    public const int UndefinedName = 3002;
    public const int UnresolvedCitation = 3010;

    // ---- Proof checker (40xx) ----
    public const int MissingClaim = 4001;
    public const int ProofFailedToVerify = 4002;
    public const int ReflSidesNotEqual = 4010;
    public const int CongProofMismatch = 4011;
    public const int UnknownLemma = 4012;
    public const int LemmaGoalMismatch = 4013;
    public const int TransChainMismatch = 4014;
    public const int LemmaArgArityMismatch = 4015;
    public const int InductionNoCases = 4020;
    public const int InductionCaseFailed = 4021;

    // ---- Capability checker (405x) ----
    public const int CapabilityNotGranted = 4050;

    // Render an integer code as the canonical "CDX####" display string.
    public static string FormatCode(int code) => $"CDX{code:D4}";

    static readonly Dictionary<int, CdxCodeInfo> s_registry = new()
    {
        [TooManyErrors] = new(TooManyErrors, nameof(TooManyErrors),
            DiagnosticSeverity.Error, CdxPhase.Infrastructure,
            "Diagnostic bag overflowed its error cap; further errors suppressed."),
        [UnterminatedTextLiteral] = new(UnterminatedTextLiteral, nameof(UnterminatedTextLiteral),
            DiagnosticSeverity.Error, CdxPhase.Lexer,
            "Text literal was opened but never closed before end of line or file."),
        [MissingPageMarker] = new(MissingPageMarker, nameof(MissingPageMarker),
            DiagnosticSeverity.Warning, CdxPhase.Infrastructure,
            "A source file is missing its 'Page N' (or 'Page N of M') marker."),
        [PageCountMismatch] = new(PageCountMismatch, nameof(PageCountMismatch),
            DiagnosticSeverity.Error, CdxPhase.Infrastructure,
            "Files of the same chapter disagree on total page count."),
        [DuplicatePage] = new(DuplicatePage, nameof(DuplicatePage),
            DiagnosticSeverity.Error, CdxPhase.Infrastructure,
            "The same page number appears in more than one file of a chapter."),
        [MissingPage] = new(MissingPage, nameof(MissingPage),
            DiagnosticSeverity.Error, CdxPhase.Infrastructure,
            "A chapter is missing one of its expected pages."),
        [DuplicateChapterInQuire] = new(DuplicateChapterInQuire, nameof(DuplicateChapterInQuire),
            DiagnosticSeverity.Error, CdxPhase.Infrastructure,
            "Two or more files in the same quire declare the same chapter without 'Page N of M' markers."),
        [UnexpectedCharacter] = new(UnexpectedCharacter, nameof(UnexpectedCharacter),
            DiagnosticSeverity.Error, CdxPhase.Lexer,
            "Character outside any known lexical class."),
        [IndentationMismatch] = new(IndentationMismatch, nameof(IndentationMismatch),
            DiagnosticSeverity.Warning, CdxPhase.Lexer,
            "Indentation does not match any outer level."),
        [UnterminatedInterpolation] = new(UnterminatedInterpolation, nameof(UnterminatedInterpolation),
            DiagnosticSeverity.Error, CdxPhase.Lexer,
            "Interpolation expression inside a text literal was not closed."),
        [InvalidTabEscape] = new(InvalidTabEscape, nameof(InvalidTabEscape),
            DiagnosticSeverity.Error, CdxPhase.Lexer,
            "\\t escape is rejected in CCE; use a literal space."),
        [InvalidCarriageReturnEscape] = new(InvalidCarriageReturnEscape, nameof(InvalidCarriageReturnEscape),
            DiagnosticSeverity.Error, CdxPhase.Lexer,
            "\\r escape is rejected in CCE; use \\n for newlines."),

        [ExpectedTokenKind] = new(ExpectedTokenKind, nameof(ExpectedTokenKind),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "A specific token kind was required but a different one was found."),
        [ExpectedDefinition] = new(ExpectedDefinition, nameof(ExpectedDefinition),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected a top-level definition (function, type, effect, etc.)."),
        [ExpectedTypeParameterName] = new(ExpectedTypeParameterName, nameof(ExpectedTypeParameterName),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected a type parameter name after a type constructor."),
        [ExpectedType] = new(ExpectedType, nameof(ExpectedType),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected a type expression."),
        [ExpectedExpression] = new(ExpectedExpression, nameof(ExpectedExpression),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected a value or proof expression."),
        [ExpectedThenKeyword] = new(ExpectedThenKeyword, nameof(ExpectedThenKeyword),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "'if' condition was not followed by 'then'."),
        [ExpectedElseKeyword] = new(ExpectedElseKeyword, nameof(ExpectedElseKeyword),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "'if/then' was not followed by 'else'."),
        [ExpectedInKeyword] = new(ExpectedInKeyword, nameof(ExpectedInKeyword),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "'let' bindings were not followed by 'in'."),
        [ExpectedFieldName] = new(ExpectedFieldName, nameof(ExpectedFieldName),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected a field name after '.' in a record access."),
        [UnterminatedListLiteral] = new(UnterminatedListLiteral, nameof(UnterminatedListLiteral),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "List literal was not closed with ']'."),
        [ExpectedMatchBranch] = new(ExpectedMatchBranch, nameof(ExpectedMatchBranch),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected a match branch after 'match' scrutinee."),
        [ExpectedPattern] = new(ExpectedPattern, nameof(ExpectedPattern),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected a pattern."),
        [ExpectedArrowAfterPattern] = new(ExpectedArrowAfterPattern, nameof(ExpectedArrowAfterPattern),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Pattern was not followed by '->'."),
        [EmptyDoBlock] = new(EmptyDoBlock, nameof(EmptyDoBlock),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "'do' expression requires at least one statement."),
        [ExpectedTypeDefBody] = new(ExpectedTypeDefBody, nameof(ExpectedTypeDefBody),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected 'record', a variant body, or constructors after '='."),
        [ExpectedRecordFieldName] = new(ExpectedRecordFieldName, nameof(ExpectedRecordFieldName),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected a field name in a record body."),
        [ExpectedConstructorName] = new(ExpectedConstructorName, nameof(ExpectedConstructorName),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected a constructor name after '|' in a variant declaration."),
        [ExpectedPageOrEffectKeyword] = new(ExpectedPageOrEffectKeyword, nameof(ExpectedPageOrEffectKeyword),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Expected 'of' page count or 'where' effect body."),
        [EffectRequiresOperation] = new(EffectRequiresOperation, nameof(EffectRequiresOperation),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "An effect declaration must declare at least one operation."),
        [HandleClauseMissingResume] = new(HandleClauseMissingResume, nameof(HandleClauseMissingResume),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "Handle clause must have at least a resume parameter."),
        [HandleMissingClauses] = new(HandleMissingClauses, nameof(HandleMissingClauses),
            DiagnosticSeverity.Error, CdxPhase.Parser,
            "'handle' requires at least one clause."),

        [ProseMissingChapter] = new(ProseMissingChapter, nameof(ProseMissingChapter),
            DiagnosticSeverity.Warning, CdxPhase.ProseValidation,
            "Prose-mode document is missing its 'Chapter:' header."),
        [ProseFunctionNameMismatch] = new(ProseFunctionNameMismatch, nameof(ProseFunctionNameMismatch),
            DiagnosticSeverity.Warning, CdxPhase.ProseValidation,
            "Prose-declared function name does not match the notation definition."),
        [ProseParameterNameMismatch] = new(ProseParameterNameMismatch, nameof(ProseParameterNameMismatch),
            DiagnosticSeverity.Warning, CdxPhase.ProseValidation,
            "Prose parameter name does not match the notation parameter name."),
        [ProseClaimWithoutNotation] = new(ProseClaimWithoutNotation, nameof(ProseClaimWithoutNotation),
            DiagnosticSeverity.Warning, CdxPhase.ProseValidation,
            "Prose declares a claim but no formal claim follows in notation."),
        [FileMissingChapter] = new(FileMissingChapter, nameof(FileMissingChapter),
            DiagnosticSeverity.Error, CdxPhase.ProseValidation,
            "Every .codex file must declare a 'Chapter:' header at column 0."),
        [FileMultipleChapters] = new(FileMultipleChapters, nameof(FileMultipleChapters),
            DiagnosticSeverity.Error, CdxPhase.ProseValidation,
            "A .codex file may declare at most one 'Chapter:' header; split into separate files."),

        [IrError] = new(IrError, nameof(IrError),
            DiagnosticSeverity.Error, CdxPhase.CodeGen,
            "Codegen encountered an IrError node, indicating an earlier phase silently failed."),
        [TypeMismatch] = new(TypeMismatch, nameof(TypeMismatch),
            DiagnosticSeverity.Error, CdxPhase.TypeChecker,
            "Two types could not be unified."),
        [UnknownName] = new(UnknownName, nameof(UnknownName),
            DiagnosticSeverity.Error, CdxPhase.TypeChecker,
            "Name was not found in the type environment during checking."),
        [ArithmeticRequiresNumeric] = new(ArithmeticRequiresNumeric, nameof(ArithmeticRequiresNumeric),
            DiagnosticSeverity.Error, CdxPhase.TypeChecker,
            "Arithmetic operator was applied to a non-numeric type."),
        [RecordFieldNotFound] = new(RecordFieldNotFound, nameof(RecordFieldNotFound),
            DiagnosticSeverity.Error, CdxPhase.TypeChecker,
            "Record type does not contain the accessed field."),
        [InfiniteType] = new(InfiniteType, nameof(InfiniteType),
            DiagnosticSeverity.Error, CdxPhase.TypeChecker,
            "Occurs check failed — unification would produce an infinite type."),
        [NonExhaustiveMatch] = new(NonExhaustiveMatch, nameof(NonExhaustiveMatch),
            DiagnosticSeverity.Warning, CdxPhase.TypeChecker,
            "Match does not cover every constructor of the scrutinee's variant type."),
        [EffectLabelMustBeName] = new(EffectLabelMustBeName, nameof(EffectLabelMustBeName),
            DiagnosticSeverity.Error, CdxPhase.TypeChecker,
            "Effect label in an effect row must be a plain name."),
        [EffectNotDeclared] = new(EffectNotDeclared, nameof(EffectNotDeclared),
            DiagnosticSeverity.Error, CdxPhase.TypeChecker,
            "An effect operation was used without the effect being declared on the function's signature."),
        [TypeArgArityMismatch] = new(TypeArgArityMismatch, nameof(TypeArgArityMismatch),
            DiagnosticSeverity.Error, CdxPhase.TypeChecker,
            "A type constructor received the wrong number of arguments."),
        [LetBindsEffectfulValue] = new(LetBindsEffectfulValue, nameof(LetBindsEffectfulValue),
            DiagnosticSeverity.Error, CdxPhase.TypeChecker,
            "let-binding the result of an effectful call silently corrupts on bare metal; use do-bind (X <- expr) inside a do block."),

        [LinearUnused] = new(LinearUnused, nameof(LinearUnused),
            DiagnosticSeverity.Error, CdxPhase.Linearity,
            "Linear variable is declared but never consumed."),
        [LinearUsedTwice] = new(LinearUsedTwice, nameof(LinearUsedTwice),
            DiagnosticSeverity.Error, CdxPhase.Linearity,
            "Linear variable is consumed more than once."),
        [LinearInconsistentBranches] = new(LinearInconsistentBranches, nameof(LinearInconsistentBranches),
            DiagnosticSeverity.Error, CdxPhase.Linearity,
            "Linear variable is used inconsistently across branches of a match or if."),
        [LinearCapturedByClosure] = new(LinearCapturedByClosure, nameof(LinearCapturedByClosure),
            DiagnosticSeverity.Error, CdxPhase.Linearity,
            "Linear variable was captured by a closure rather than consumed directly."),

        [InDefinition] = new(InDefinition, nameof(InDefinition),
            DiagnosticSeverity.Info, CdxPhase.TypeChecker,
            "Contextual note attaching a subsequent diagnostic to a specific definition."),

        [DuplicateDefinition] = new(DuplicateDefinition, nameof(DuplicateDefinition),
            DiagnosticSeverity.Error, CdxPhase.NameResolver,
            "The same name is introduced twice at chapter scope (definitions, types, constructors, effect ops)."),
        [UndefinedName] = new(UndefinedName, nameof(UndefinedName),
            DiagnosticSeverity.Error, CdxPhase.NameResolver,
            "A name was referenced but is not in any visible scope."),
        [UnresolvedCitation] = new(UnresolvedCitation, nameof(UnresolvedCitation),
            DiagnosticSeverity.Error, CdxPhase.NameResolver,
            "A 'cites' declaration referenced a chapter that could not be loaded."),

        [MissingClaim] = new(MissingClaim, nameof(MissingClaim),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "Proof references a claim name that does not exist."),
        [ProofFailedToVerify] = new(ProofFailedToVerify, nameof(ProofFailedToVerify),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "A proof term did not check against its claim."),
        [ReflSidesNotEqual] = new(ReflSidesNotEqual, nameof(ReflSidesNotEqual),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "'refl' was used where the two sides of the goal are not syntactically equal."),
        [CongProofMismatch] = new(CongProofMismatch, nameof(CongProofMismatch),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "A 'cong' congruence proof did not yield the expected equality."),
        [UnknownLemma] = new(UnknownLemma, nameof(UnknownLemma),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "A lemma was applied but no lemma with that name is in scope."),
        [LemmaGoalMismatch] = new(LemmaGoalMismatch, nameof(LemmaGoalMismatch),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "Applied lemma's conclusion does not match the current proof goal."),
        [TransChainMismatch] = new(TransChainMismatch, nameof(TransChainMismatch),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "A transitivity chain's end-points do not match the goal's end-points."),
        [LemmaArgArityMismatch] = new(LemmaArgArityMismatch, nameof(LemmaArgArityMismatch),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "Lemma received the wrong number of arguments."),
        [InductionNoCases] = new(InductionNoCases, nameof(InductionNoCases),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "Induction proof has no cases."),
        [InductionCaseFailed] = new(InductionCaseFailed, nameof(InductionCaseFailed),
            DiagnosticSeverity.Error, CdxPhase.ProofChecker,
            "One case of an induction proof failed to check."),

        [CapabilityNotGranted] = new(CapabilityNotGranted, nameof(CapabilityNotGranted),
            DiagnosticSeverity.Error, CdxPhase.CapabilityChecker,
            "A capability required by main was not granted in the runtime policy."),
    };

    public static IReadOnlyDictionary<int, CdxCodeInfo> All => s_registry;

    public static CdxCodeInfo? Lookup(int code) =>
        s_registry.TryGetValue(code, out CdxCodeInfo? info) ? info : null;
}
