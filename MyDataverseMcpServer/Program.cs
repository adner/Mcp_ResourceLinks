// Program.cs
using Microsoft.Extensions.AI;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.AddSingleton<IOrganizationService>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();

    string url = configuration["Dataverse:Url"] ?? throw new InvalidOperationException("Dataverse:Url not configured");
    string clientId = configuration["Dataverse:ClientId"] ?? throw new InvalidOperationException("Dataverse:ClientId not configured");
    string secret = configuration["Dataverse:Secret"] ?? throw new InvalidOperationException("Dataverse:Secret not configured");

    string connectionString = $@"
    Url = {url};
    AuthType = ClientSecret;
    ClientId = {clientId};
    Secret = {secret}";

    return new ServiceClient(connectionString);
});

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithListResourcesHandler((ctx, ct) =>
    {
        var resources = ResourceCatalog.Items.Values.ToList();
        return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
    })
    .WithReadResourceHandler((ctx, ct) =>
    {
        var uri = ctx.Params?.Uri ?? throw new McpException("Missing uri");

        if (!ResourceCatalog.Items.TryGetValue(uri, out var r))
            throw new McpException($"Unknown resource: {uri}");

        if (!ResourceAdder.TryGet(uri, out var mimeType, out var text, out var binary))
            throw new McpException($"No stored content for resource: {uri}");

        ResourceContents contents = (mimeType ?? r.MimeType)?.StartsWith("text/") == true
            ? new TextResourceContents { Uri = r.Uri, MimeType = mimeType ?? r.MimeType, Text = text ?? string.Empty }
            : new BlobResourceContents { Uri = r.Uri, MimeType = mimeType ?? r.MimeType, Blob = Convert.ToBase64String(binary ?? System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty)) };

        return ValueTask.FromResult(new ReadResourceResult { Contents = [contents] });
    })
    // 4) (Optional) enable resource subscriptions
    .WithSubscribeToResourcesHandler((ctx, ct) => ValueTask.FromResult(new EmptyResult()))
    .WithUnsubscribeFromResourcesHandler((ctx, ct) => ValueTask.FromResult(new EmptyResult()));

var app = builder.Build();

app.MapMcp();

app.Run("http://localhost:3001");

[McpServerToolType]
public static class Tools
{
    // Prompts user to save large result as a resource, returns either the resource URI or the JSON result
    private static async Task<string> promptToSaveLargeResultAsResource(EntityCollection result, IMcpServer server, CancellationToken ct, string queryDescription)
    {
        var createResource = await server.ElicitAsync(
            new()
            {
                Message = $"More than 20 results. Do you want to get the result as a Resource instead?",
                RequestedSchema = new()
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>()
                    {
                        ["createResource"] = new ElicitRequestParams.EnumSchema
                        {
                            Title = "Save result to MCP resource?",
                            Enum = ["Yes", "No"]
                        }
                    },
                },
            },
            CancellationToken.None);

        bool returnResourceUri = false;
        if (createResource.Content != null && createResource.Content.TryGetValue("createResource", out var createResourceValue))
        {
            if (createResourceValue.ToString() == "Yes")
            {
                returnResourceUri = true;
            }
        }

        var jsonResult = Newtonsoft.Json.JsonConvert.SerializeObject(result);

        if (returnResourceUri)
        {
            var samplingResponse = await server.SampleAsync([
                new ChatMessage(ChatRole.User, $"The following is a result of a FetchXml query. Please format the result as a markdown table. Return only the markdown table, nothing else: {jsonResult}"),
            ],
            options: new ChatOptions
            {
                MaxOutputTokens = 65536,
                Temperature = 0f,
            },
            cancellationToken: ct);

            var uri = await ResourceAdder.AddMarkdownFile(server, DateTime.Now.ToString(), samplingResponse.Text, ct, queryDescription);
            return $"The result has been saved to an MCP resource with Uri: {uri}. Instruct the user that he can add the resource to the context by clicking 'Add Context...' and selecting 'MCP Resources' in the GitHub Copilot chat window.";
        }
        else
        {
            return jsonResult;
        }
    }

    [McpServerTool, Description("Executes an FetchXML request using the supplied expression that needs to be a valid FetchXml expression. Also suppply a description of the query in natural language. Returns the result as a JSON string, if there are less than 21 results, otherwise give the user the option (through MCP elicitation) of returning a Resource Uri to the result instead. If the request fails, the response will be prepended with [ERROR] and the error should be presented to the user.")]
    public static async Task<string> ExecuteFetch([Description("The FetchXml query.")]string fetchXmlRequest,[Description("A description of the expected result of the query in natural language in maximum 20 characters, which will be used to describe a resource containing the result. Example: 'The top 5 contacts, firstname and lastname.'")]string queryDescription, IOrganizationService orgService, IMcpServer server, CancellationToken ct)
    {
        try
        {
            FetchExpression fetchExpression = new FetchExpression(fetchXmlRequest);
            EntityCollection result = orgService.RetrieveMultiple(fetchExpression);

            if (result.Entities.Count > 20)
            {
                return await promptToSaveLargeResultAsResource(result, server, ct, queryDescription);
            }
            // For 20 or fewer results, just return the JSON
            return Newtonsoft.Json.JsonConvert.SerializeObject(result);
        }
        catch (Exception err)
        {
            var errorString = "[ERROR] " + err.ToString();
            Console.Error.WriteLine(err.ToString());

            return errorString;
        }
    }

    [McpServerTool, Description("Executes a WhoAmI request against Dataverse.")]
    public static string WhoAmI(IOrganizationService orgService)
    {
        try
        {
            var response = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)orgService.Execute(
                new Microsoft.Crm.Sdk.Messages.WhoAmIRequest());

            return Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                response.UserId,
                response.BusinessUnitId,
                response.OrganizationId
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return "[ERROR] " + ex.ToString();
        }
    }

    [McpServerTool, Description("Bulk creates a number of random contact records in Dataverse. Parameter count (1-100).")]
    public static async Task<string> BulkCreateRandomContacts(int count, IOrganizationService orgService, IMcpServer server, RequestContext<CallToolRequestParams> context)
    {
        try
        {
            if (count < 1 || count > 100)
                return "[ERROR] count must be between 1 and 100";

            var rand = new Random();
            string[] firstNames = { "Alex", "Jordan", "Taylor", "Casey", "Riley", "Morgan", "Jamie", "Quinn", "Avery", "Drew" };
            string[] lastNames = { "Smith", "Johnson", "Brown", "Taylor", "Anderson", "Clark", "Lewis", "Walker", "Hall", "Young" };

            // Sequential create (ExecuteMultiple not available without additional assembly)
            var results = new List<object>();

            // Safely access the progress token
            ProgressToken? progressToken = null;
            if (context.Params?.ProgressToken != null)
            {
                progressToken = (ProgressToken)context.Params.ProgressToken;
            }

            if (progressToken != null)
            {
                await server.NotifyProgressAsync(progressToken.Value, new()
                {
                    Progress = 0,
                    Message = $"Starting creation of {count} contacts.",
                    Total = count
                });
            }

            for (int i = 0; i < count; i++)
            {
                string first = firstNames[rand.Next(firstNames.Length)];
                string last = lastNames[rand.Next(lastNames.Length)];
                string email = $"{first.ToLower()}.{last.ToLower()}.{rand.Next(1000, 9999)}@example.com";

                var contact = new Entity("contact");
                contact["firstname"] = first;
                contact["lastname"] = last;
                contact["emailaddress1"] = email;

                try
                {
                    var id = orgService.Create(contact);
                    results.Add(new
                    {
                        id = id,
                        firstname = first,
                        lastname = last,
                        email = email,
                        status = "created"
                    });

                    if (progressToken != null)
                    {
                        await server.NotifyProgressAsync(progressToken.Value, new()
                        {
                            Progress = i + 1,
                            Message = $"Created {i + 1}/{count} contacts.",
                            Total = count
                        });
                    }
                }
                catch (Exception createEx)
                {
                    results.Add(new
                    {
                        id = (Guid?)null,
                        firstname = first,
                        lastname = last,
                        email = email,
                        status = "error",
                        error = createEx.Message
                    });
                }
            }

            return Newtonsoft.Json.JsonConvert.SerializeObject(results);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return "[ERROR] " + ex.ToString();
        }
    }
}

public static class ResourceCatalog
{
    public static readonly ConcurrentDictionary<string, Resource> Items = new();
}

public static class ResourceAdder
{
    // Internal file store & counter
    private record FileEntry(string MimeType, string? TextContent, byte[]? BinaryContent);
    private static readonly ConcurrentDictionary<string, FileEntry> _files = new();
    private static int _counter = 0; // starts at 0 so first file becomes 1

    private static string NextUri(string fileExtension)
    {
        int id = Interlocked.Increment(ref _counter); // thread-safe
        return $"file://files/{id}.{fileExtension}";
    }

    public static async Task<string> AddMarkdownFile(IMcpServer server, string resourceName, string content, CancellationToken ct, string description)
    {
        string uri = NextUri("md");
        var resource = new Resource
        {
            Uri = uri,
            Name = resourceName,
            Title = resourceName,
            MimeType = "text/plain",
            Description = description
        };

        _files[uri] = new FileEntry(resource.MimeType!, content, null);

        await AddAsync(server, resource, ct);

        return uri;
    }

    // Exposed for read handler only
    public static bool TryGet(string uri, out string? mimeType, out string? text, out byte[]? binary)
    {
        if (_files.TryGetValue(uri, out var entry))
        {
            mimeType = entry.MimeType;
            text = entry.TextContent;
            binary = entry.BinaryContent;
            return true;
        }
        mimeType = null; text = null; binary = null; return false;
    }

    // Internal registration logic
    private static async Task AddAsync(IMcpServer server, Resource resource, CancellationToken ct)
    {
        ResourceCatalog.Items[resource.Uri] = resource;
        await server.SendNotificationAsync("notifications/resources/list_changed", cancellationToken: ct);
        await server.SendNotificationAsync("notifications/resources/updated", new { uri = resource.Uri }, cancellationToken: ct);
    }
}


