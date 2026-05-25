using Codex.Ast;
using Codex.Types;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Codex.Lsp;

internal sealed class CompletionHandler(DocumentStore store) : CompletionHandlerBase
{
    readonly DocumentStore m_store = store;

    static readonly string[] s_keywords =
    [
        "if", "then", "else", "let", "in", "when", "act", "linear",
        "record", "True", "False",
    ];

    static readonly string[] s_builtins =
    [
        "show", "negate", "print-line", "read-line",
        "open-file", "read-all", "close-file",
        "text-length", "char-at", "substring", "text-replace",
        "integer-to-text", "char-code", "code-to-char",
        "is-letter", "is-digit", "is-whitespace",
        "list-at", "list-length", "map", "filter", "fold",
    ];

    static readonly string[] s_typeNames =
    [
        "Integer", "Number", "Text", "Boolean", "Nothing",
        "List", "FileHandle",
    ];

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        AnalysisResult? result = m_store.GetResult(uri);

        List<CompletionItem> items = [];

        if (result is not null)
        {
            foreach (Definition def in result.Definitions)
            {
                CodexType? type = result.Types[def.Name.Value];
                items.Add(new CompletionItem
                {
                    Label = def.Name.Value,
                    Kind = type is FunctionType ? CompletionItemKind.Function : CompletionItemKind.Variable,
                    Detail = type?.ToString(),
                });
            }

            foreach (TypeDef typeDef in result.TypeDefinitions)
            {
                items.Add(new CompletionItem
                {
                    Label = typeDef.Name.Value,
                    Kind = CompletionItemKind.Struct,
                    Detail = "(type)",
                });

                if (typeDef is VariantTypeDef variant)
                {
                    foreach (VariantCtorDef ctor in variant.Constructors)
                    {
                        items.Add(new CompletionItem
                        {
                            Label = ctor.Name.Value,
                            Kind = CompletionItemKind.EnumMember,
                            Detail = $"({variant.Name.Value} constructor)",
                        });
                    }
                }
            }
        }

        foreach (string builtin in s_builtins)
        {
            items.Add(new CompletionItem
            {
                Label = builtin,
                Kind = CompletionItemKind.Function,
                Detail = "(builtin)",
            });
        }

        foreach (string typeName in s_typeNames)
        {
            items.Add(new CompletionItem
            {
                Label = typeName,
                Kind = CompletionItemKind.Class,
            });
        }

        foreach (string kw in s_keywords)
        {
            items.Add(new CompletionItem
            {
                Label = kw,
                Kind = CompletionItemKind.Keyword,
            });
        }

        return Task.FromResult(new CompletionList(items));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = LspHelpers.s_selector,
            TriggerCharacters = new Container<string>("."),
            ResolveProvider = false,
        };
    }
}
