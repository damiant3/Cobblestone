using Codex.Ast;
using Codex.Core;
using Codex.Types;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Codex.Lsp;

internal sealed class DocumentSymbolHandler(DocumentStore store) : DocumentSymbolHandlerBase
{
    readonly DocumentStore m_store = store;

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        AnalysisResult? result = m_store.GetResult(uri);
        if (result is null)
        {
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
        }

        List<SymbolInformationOrDocumentSymbol> symbols = [];
        foreach (Definition def in result.Definitions)
        {
            CodexType? type = result.Types[def.Name.Value];
            string detail = type?.ToString() ?? "";
            SymbolKind kind = type is FunctionType ? SymbolKind.Function : SymbolKind.Variable;

            OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range =
                TextDocumentSyncHandler.SpanToRange(def.Span);

            symbols.Add(new SymbolInformationOrDocumentSymbol(new DocumentSymbol
            {
                Name = def.Name.Value,
                Detail = detail,
                Kind = kind,
                Range = range,
                SelectionRange = range,
            }));
        }

        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(symbols));
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DocumentSymbolRegistrationOptions
        {
            DocumentSelector = LspHelpers.s_selector,
        };
    }
}
