using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codex.Mcp;

static class Program
{
    static async Task Main()
    {
        McpServer server = new();
        await server.RunAsync();
    }
}

sealed class McpServer
{
    readonly ToolDispatcher m_dispatcher = new();

    public async Task RunAsync()
    {
        using StreamReader reader = new(Console.OpenStandardInput());
        using StreamWriter writer = new(Console.OpenStandardOutput()) { AutoFlush = true };

        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonNode? request;
            try
            {
                request = JsonNode.Parse(line);
            }
            catch
            {
                await WriteError(writer, null, -32700, "Parse error");
                continue;
            }

            if (request is null)
            {
                await WriteError(writer, null, -32700, "Parse error");
                continue;
            }

            JsonNode? id = request["id"];
            string? method = request["method"]?.GetValue<string>();

            if (method is null)
            {
                await WriteError(writer, id, -32600, "Invalid request: missing method");
                continue;
            }

            try
            {
                JsonNode? result = method switch
                {
                    "initialize" => HandleInitialize(request),
                    "notifications/initialized" => null, // notification, no response
                    "tools/list" => HandleToolsList(),
                    "tools/call" => m_dispatcher.Dispatch(request),
                    "resources/list" => HandleResourcesList(),
                    "resources/read" => m_dispatcher.ReadResource(request),
                    _ => throw new McpException(-32601, $"Unknown method: {method}"),
                };

                if (id is not null && result is not null)
                {
                    await WriteResult(writer, id, result);
                }
                else if (id is not null)
                {
                    await WriteResult(writer, id, new JsonObject());
                }
            }
            catch (McpException ex)
            {
                await WriteError(writer, id, ex.Code, ex.Message);
            }
            catch (Exception ex)
            {
                await WriteError(writer, id, -32603, ex.Message);
            }
        }
    }

    static JsonNode HandleInitialize(JsonNode request)
    {
        return new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject(),
                ["resources"] = new JsonObject(),
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "codex-mcp",
                ["version"] = "0.1.0",
            },
        };
    }

    static JsonNode HandleToolsList()
    {
        return new JsonObject
        {
            ["tools"] = new JsonArray(
                MakeTool("codex-check", "Type-check a Codex source file. Returns diagnostics with line/column spans.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["file"] = new JsonObject { ["type"] = "string", ["description"] = "Path to the .codex file" },
                        },
                        ["required"] = new JsonArray("file"),
                    }),
                MakeTool("codex-build", "Compile a Codex source file or directory to one or more targets.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Path to .codex file or directory" },
                            ["targets"] = new JsonObject { ["type"] = "string", ["description"] = "Comma-separated targets (cs,js,rust,python,il,...). Default: cs" },
                        },
                        ["required"] = new JsonArray("path"),
                    }),
                MakeTool("codex-hover", "Get the type of a name in a Codex source file.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["file"] = new JsonObject { ["type"] = "string", ["description"] = "Path to the .codex file" },
                            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Name to look up (function or type name)" },
                        },
                        ["required"] = new JsonArray("file", "name"),
                    }),
                MakeTool("codex-parse", "Parse a Codex source file and return the token stream or syntax tree.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["file"] = new JsonObject { ["type"] = "string", ["description"] = "Path to the .codex file" },
                            ["mode"] = new JsonObject { ["type"] = "string", ["description"] = "Output mode: tokens, cst, or ast. Default: ast" },
                        },
                        ["required"] = new JsonArray("file"),
                    })
            ),
        };
    }

    static JsonNode HandleResourcesList()
    {
        return new JsonObject
        {
            ["resources"] = new JsonArray(
                new JsonObject
                {
                    ["uri"] = "codex://builtins",
                    ["name"] = "Codex Builtins",
                    ["description"] = "All built-in functions with their type signatures",
                    ["mimeType"] = "text/plain",
                }
            ),
        };
    }

    static JsonObject MakeTool(string name, string description, JsonObject inputSchema)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema,
        };
    }

    static async Task WriteResult(StreamWriter writer, JsonNode? id, JsonNode result)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        };
        await writer.WriteLineAsync(response.ToJsonString(JsonOpts.s_compact));
    }

    static async Task WriteError(StreamWriter writer, JsonNode? id, int code, string message)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        await writer.WriteLineAsync(response.ToJsonString(JsonOpts.s_compact));
    }
}

static class JsonOpts
{
    public static readonly JsonSerializerOptions s_compact = new()
    {
        WriteIndented = false,
    };
}

sealed class McpException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
