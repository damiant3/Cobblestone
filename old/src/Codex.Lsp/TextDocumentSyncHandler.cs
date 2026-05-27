using System.Collections.Immutable;
using Codex.Core;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Codex.Lsp;

internal sealed class TextDocumentSyncHandler(ILanguageServerFacade server, DocumentStore store)
    : TextDocumentSyncHandlerBase
{
    readonly ILanguageServerFacade m_server = server;
    readonly DocumentStore m_store = store;

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "codex");
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        string text = request.TextDocument.Text;
        m_store.Update(uri, text);
        PublishDiagnostics(uri, text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        string text = request.ContentChanges.Last().Text;
        m_store.Update(uri, text);
        PublishDiagnostics(uri, text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        m_store.Remove(uri);
        m_server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(),
        });
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
    {
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = LspHelpers.s_selector,
        };
    }

    void PublishDiagnostics(string uri, string text)
    {
        AnalysisResult result = Analyzer.Analyze(uri, text);
        m_store.UpdateResult(uri, result);

        List<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic> lspDiags = [];
        foreach (Codex.Core.Diagnostic diag in result.Diagnostics)
        {
            lspDiags.Add(new OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic
            {
                Range = SpanToRange(diag.Span),
                Severity = MapSeverity(diag.Severity),
                Code = diag.FormattedCode,
                Source = "codex",
                Message = diag.Message,
            });
        }

        m_server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.Parse(uri),
            Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(lspDiags),
        });
    }

    internal static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range SpanToRange(SourceSpan span)
    {
        return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            Math.Max(0, span.Start.Line - 1), Math.Max(0, span.Start.Column - 1),
            Math.Max(0, span.End.Line - 1), Math.Max(0, span.End.Column - 1));
    }

    static OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity? MapSeverity(
        Codex.Core.DiagnosticSeverity severity)
    {
        return severity switch
        {
            Codex.Core.DiagnosticSeverity.Error =>
                OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error,
            Codex.Core.DiagnosticSeverity.Warning =>
                OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Warning,
            Codex.Core.DiagnosticSeverity.Info =>
                OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
            Codex.Core.DiagnosticSeverity.Hint =>
                OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Hint,
            _ => OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Information,
        };
    }
}
