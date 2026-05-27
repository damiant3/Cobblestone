import * as path from "path";
import * as vscode from "vscode";
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind,
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;

export function activate(context: vscode.ExtensionContext): void {
  const config = vscode.workspace.getConfiguration("codex");
  let serverPath = config.get<string>("serverPath", "");

  if (!serverPath) {
    // Default: assume `dotnet run` from the Codex.Lsp project
    serverPath = "dotnet";
  }

  const serverArgs = serverPath === "dotnet"
    ? ["run", "--project", findLspProject(context), "--no-build"]
    : [];

  const serverOptions: ServerOptions = {
    command: serverPath,
    args: serverArgs,
    transport: TransportKind.stdio,
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "codex" }],
  };

  client = new LanguageClient(
    "codexLanguageServer",
    "Codex Language Server",
    serverOptions,
    clientOptions
  );

  client.start();
}

export function deactivate(): Thenable<void> | undefined {
  return client?.stop();
}

function findLspProject(context: vscode.ExtensionContext): string {
  // Walk up from extension dir to find the solution root
  const workspaceFolders = vscode.workspace.workspaceFolders;
  if (workspaceFolders && workspaceFolders.length > 0) {
    return path.join(
      workspaceFolders[0].uri.fsPath,
      "src",
      "Codex.Lsp",
      "Codex.Lsp.csproj"
    );
  }
  // Fallback: relative from extension location
  return path.join(
    context.extensionPath,
    "..",
    "..",
    "src",
    "Codex.Lsp",
    "Codex.Lsp.csproj"
  );
}
