"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.activate = activate;
exports.deactivate = deactivate;
const path = require("path");
const vscode = require("vscode");
const node_1 = require("vscode-languageclient/node");
let client;
function activate(context) {
    const config = vscode.workspace.getConfiguration("codex");
    let serverPath = config.get("serverPath", "");
    if (!serverPath) {
        // Default: assume `dotnet run` from the Codex.Lsp project
        serverPath = "dotnet";
    }
    const serverArgs = serverPath === "dotnet"
        ? ["run", "--project", findLspProject(context), "--no-build"]
        : [];
    const serverOptions = {
        command: serverPath,
        args: serverArgs,
        transport: node_1.TransportKind.stdio,
    };
    const clientOptions = {
        documentSelector: [{ scheme: "file", language: "codex" }],
    };
    client = new node_1.LanguageClient("codexLanguageServer", "Codex Language Server", serverOptions, clientOptions);
    client.start();
}
function deactivate() {
    return client?.stop();
}
function findLspProject(context) {
    // Walk up from extension dir to find the solution root
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (workspaceFolders && workspaceFolders.length > 0) {
        return path.join(workspaceFolders[0].uri.fsPath, "src", "Codex.Lsp", "Codex.Lsp.csproj");
    }
    // Fallback: relative from extension location
    return path.join(context.extensionPath, "..", "..", "src", "Codex.Lsp", "Codex.Lsp.csproj");
}
//# sourceMappingURL=extension.js.map