using System;
using System.Collections.Generic;
using System.Linq;

public sealed record Token(TokenKind kind, string text, long offset, long line, long column);

public static class Codex_Token
{
    public static long token_length(Token t)
    {
        return ((long)t.text.Length);
    }

}
