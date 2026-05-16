using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Codex.Lsp;

internal static class Program
{
    static async Task Main(string[] args)
    {
        ILanguageServer server = await LanguageServer.From(options =>
        {
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .WithServices(services =>
                {
                    services.AddSingleton<DocumentStore>();
                })
                .WithHandler<TextDocumentSyncHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<DocumentSymbolHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<SemanticTokensHandler>()
                .OnInitialize((server, request, ct) =>
                {
                    return Task.CompletedTask;
                });
        }).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);
    }
}
