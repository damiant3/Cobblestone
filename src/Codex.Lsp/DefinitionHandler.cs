using Codex.Ast;
using Codex.Core;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Codex.Lsp;

internal sealed class DefinitionHandler(DocumentStore store) : DefinitionHandlerBase
{
    readonly DocumentStore m_store = store;

    public override Task<LocationOrLocationLinks?> Handle(
        DefinitionParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        AnalysisResult? result = m_store.GetResult(uri);
        string? text = m_store.GetText(uri);
        if (result is null || text is null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        int line = (int)request.Position.Line;
        int col = (int)request.Position.Character;

        string? word = LspHelpers.GetWordAt(text, line, col);
        if (word is null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        SourceSpan? targetSpan = FindDefinitionSpan(result, word);
        if (targetSpan is null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        Location location = new()
        {
            Uri = DocumentUri.Parse(uri),
            Range = TextDocumentSyncHandler.SpanToRange(targetSpan.Value),
        };

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(location));
    }

    static SourceSpan? FindDefinitionSpan(AnalysisResult result, string name)
    {
        foreach (Definition def in result.Definitions)
        {
            if (def.Name.Value == name)
            {
                return def.Span;
            }
        }

        foreach (TypeDef typeDef in result.TypeDefinitions)
        {
            if (typeDef.Name.Value == name)
            {
                return typeDef.Span;
            }

            if (typeDef is VariantTypeDef variant)
            {
                foreach (VariantCtorDef ctor in variant.Constructors)
                {
                    if (ctor.Name.Value == name)
                    {
                        return ctor.Span;
                    }
                }
            }
        }

        return null;
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions
        {
            DocumentSelector = LspHelpers.s_selector,
        };
    }
}
