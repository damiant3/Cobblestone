using Codex.Ast;
using Codex.Core;
using Codex.Types;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Codex.Lsp;

internal sealed class HoverHandler(DocumentStore store) : HoverHandlerBase
{
    readonly DocumentStore m_store = store;

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        AnalysisResult? result = m_store.GetResult(uri);
        string? text = m_store.GetText(uri);
        if (result is null || text is null)
            return Task.FromResult<Hover?>(null);

        int line = (int)request.Position.Line;
        int col = (int)request.Position.Character;

        string? word = LspHelpers.GetWordAt(text, line, col);
        if (word is null)
            return Task.FromResult<Hover?>(null);

        string? content = GetHoverContent(result, word);
        if (content is null)
            return Task.FromResult<Hover?>(null);

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = content,
            }),
        });
    }

    static string? GetHoverContent(AnalysisResult result, string word)
    {
        CodexType? type = result.Types[word];
        if (type is not null)
            return $"**{word}** : `{type}`";

        foreach (TypeDef typeDef in result.TypeDefinitions)
        {
            if (typeDef.Name.Value == word)
                return FormatTypeDef(typeDef);

            if (typeDef is VariantTypeDef variant)
            {
                foreach (VariantCtorDef ctor in variant.Constructors)
                {
                    if (ctor.Name.Value == word)
                        return FormatConstructor(variant, ctor);
                }
            }
        }

        return null;
    }

    static string FormatTypeDef(TypeDef typeDef)
    {
        if (typeDef is RecordTypeDef rec)
        {
            string fields = string.Join(", ", rec.Fields.Select(f => $"{f.FieldName.Value} : {f.Type}"));
            return $"**{rec.Name.Value}** (record)\n\nFields: `{fields}`";
        }
        if (typeDef is VariantTypeDef variant)
        {
            string ctors = string.Join(" | ", variant.Constructors.Select(c => c.Name.Value));
            return $"**{variant.Name.Value}** (type)\n\nConstructors: `{ctors}`";
        }
        return $"**{typeDef.Name.Value}** (type)";
    }

    static string FormatConstructor(VariantTypeDef variant, VariantCtorDef ctor)
    {
        if (ctor.Fields.Count == 0)
            return $"**{ctor.Name.Value}** : `{variant.Name.Value}`";
        string fields = string.Join(", ", ctor.Fields.Select(f =>
            f.FieldName is not null ? $"{f.FieldName.Value} : {f.Type}" : $"{f.Type}"));
        return $"**{ctor.Name.Value}** ({fields}) : `{variant.Name.Value}`";
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = LspHelpers.s_selector,
        };
    }
}
