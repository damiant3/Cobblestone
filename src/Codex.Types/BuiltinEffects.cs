using Codex.Ast;
using Codex.Core;
using Codex.Syntax;

namespace Codex.Types;

sealed class BuiltinEffects
{
    static readonly string[] s_effectSources =
    [
        """
        effect Console where
          print-line : Text -> [Console] Nothing
          read-line  : [Console] Text
        """,

        """
        effect FileSystem where
          open-file  : Text -> [FileSystem] linear FileHandle
          read-all   : linear FileHandle -> [FileSystem] Pair Text (linear FileHandle)
          close-file : linear FileHandle -> [FileSystem] Nothing
          read-file  : Text -> [FileSystem] Text
          write-file : Text -> Text -> [FileSystem] Nothing
          write-binary : List Integer -> [FileSystem] Nothing
        """,

        """
        effect Time where
          now : [Time] Integer
        """,

        """
        effect Random where
          random-integer : Integer -> Integer -> [Random] Integer
        """,

        """
        effect State where
          get-state : [State] s
          set-state : s -> [State] Nothing
        """,

        """
        effect Network where
          fetch       : Text -> [Network] Text
          post        : Text -> Text -> [Network] Text
          resolve-dns : Text -> [Network] Text
        """,

        """
        effect Display where
          draw-text  : Text -> Integer -> Integer -> [Display] Nothing
          draw-rect  : Integer -> Integer -> Integer -> Integer -> [Display] Nothing
          clear      : [Display] Nothing
          set-pixel  : Integer -> Integer -> Integer -> [Display] Nothing
        """,

        """
        effect Camera where
          capture     : [Camera] Text
          capture-raw : Integer -> Integer -> [Camera] Text
        """,

        """
        effect Microphone where
          listen   : Integer -> [Microphone] Text
          is-quiet : [Microphone] Boolean
        """,

        """
        effect Location where
          locate    : [Location] Pair Integer Integer
          altitude  : [Location] Integer
        """,

        """
        effect Sensors where
          accelerometer : [Sensors] Pair Integer (Pair Integer Integer)
          gyroscope     : [Sensors] Pair Integer (Pair Integer Integer)
          barometer     : [Sensors] Integer
          light-level   : [Sensors] Integer
        """,

        """
        effect Identity where
          authenticate : [Identity] Text
          current-user : [Identity] Text
        """
    ];

    static IReadOnlyList<EffectDef>? s_cached;

    public static IReadOnlyList<EffectDef> Load()
    {
        if (s_cached is not null)
            return s_cached;

        List<EffectDef> allEffects = [];
        DiagnosticBag diagnostics = new();
        Desugarer desugarer = new(diagnostics);

        foreach (string source in s_effectSources)
        {
            SourceText src = new("<builtin-effects>", source);
            Lexer lexer = new(src, diagnostics);
            IReadOnlyList<Token> tokens = lexer.TokenizeAll();
            Parser parser = new(tokens, diagnostics);
            DocumentNode document = parser.ParseDocument();
            Chapter chapter = desugarer.Desugar(document, "builtins");
            allEffects.AddRange(chapter.EffectDefs);
        }

        s_cached = allEffects;
        return s_cached;
    }
}
