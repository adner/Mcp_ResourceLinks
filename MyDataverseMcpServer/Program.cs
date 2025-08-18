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
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;

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

// Serve static assets (e.g. logo.png) from chartTemplates folder under the /dynamic path.
// This runs before the dynamic resource endpoint below. If a physical file (like logo.png) exists
// it will be served directly; otherwise the request falls through to the dynamic handler.
// NOTE: Use ContentRootPath so we can serve files from the project directory during development
// (AppContext.BaseDirectory points to bin output where the folder may not be copied yet.)
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, "chartTemplates")),
    RequestPath = "/dynamic"
});

// Dynamic HTML resource endpoint: /dynamic/{id}.html
app.MapGet("/dynamic/{file}", (string file) =>
{
    // Basic filename sanitization (no path traversal / dir separators)
    if (file.IndexOfAny(new[] { '/', '\\' }) >= 0)
        return Results.NotFound();

    // 1. Attempt to serve any physical file from chartTemplates (e.g., logo.png) if it exists.
    var staticDir = Path.Combine(app.Environment.ContentRootPath, "chartTemplates");
    var physicalPath = Path.Combine(staticDir, file);
    if (File.Exists(physicalPath))
    {
        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(physicalPath, out var contentType))
            contentType = "application/octet-stream";
        return Results.File(physicalPath, contentType);
    }

    // 2. Support serving dynamically published HTML / Markdown resources.
    if (!(file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        return Results.NotFound();

    var match = ResourceCatalog.Items.Values.FirstOrDefault(r =>
        r.Uri.EndsWith($"/{file}", StringComparison.OrdinalIgnoreCase) &&
        (r.MimeType == "text/html" || r.MimeType == "text/markdown" || r.MimeType == "text/plain")
    );
    if (match == null)
        return Results.NotFound();

    if (!ResourceAdder.TryGet(match.Uri, out var mime, out var text, out var binary))
        return Results.NotFound();

    var content = text ?? (binary != null ? System.Text.Encoding.UTF8.GetString(binary) : string.Empty);
    return Results.Content(content, mime);
});

app.Run("http://localhost:3001");

[McpServerToolType]
public static partial class Tools
{
    // Prompts user to save large result as a resource, returns either the resource URI or the JSON result
    private static async Task<string> promptToSaveResultAsResource(EntityCollection result, IMcpServer server, CancellationToken ct, string queryDescription)
    {
        var createResource = await server.ElicitAsync(
            new()
            {
                Message = $"Do you want to get the result as a Resource?",
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
            return $"The result has been saved to an MCP resource: {uri}. Show the link to the Resource URI to the user, and instruct the user that he can add the resource to the context by clicking 'Add Context...' and selecting 'MCP Resources' in the GitHub Copilot chat window.";
        }
        else
        {
            return jsonResult;
        }
    }

    [McpServerTool, Description("Executes an FetchXML request using the supplied expression that needs to be a valid FetchXml expression. Also supply a description of the query in natural language. Returns the result as a JSON string, if there are less than 21 results, otherwise give the user the option (through MCP elicitation) of returning a Resource Uri to the result instead. If the request fails, the response will be prepended with [ERROR] and the error should be presented to the user.")]
    public static async Task<string> ExecuteFetch([Description("The FetchXml query.")] string fetchXmlRequest, [Description("A description of the expected result of the query in natural language in maximum 20 characters, which will be used to describe a resource containing the result. Example: 'The top 5 contacts, firstname and lastname.'")] string queryDescription, IOrganizationService orgService, IMcpServer server, CancellationToken ct)
    {
        try
        {
            FetchExpression fetchExpression = new FetchExpression(fetchXmlRequest);
            EntityCollection result = orgService.RetrieveMultiple(fetchExpression);

            return await promptToSaveResultAsResource(result, server, ct, queryDescription);
        }
        catch (Exception err)
        {
            var errorString = "[ERROR] " + err.ToString();
            Console.Error.WriteLine(err.ToString());

            return errorString;
        }
    }

    [McpServerTool, Description("Executes an FetchXML request using the supplied expression that needs to be a valid FetchXml expression, then create a report using Chart.js that visualizes the result and returns a link to the report. If the request fails, the response will be prepended with [ERROR] and the error should be presented to the user.")]
    public static async Task<string> CreateReportFromQuery([Description("The FetchXml query. Should be kept simple, no aggregate functions!")] string fetchXmlRequest, [Description("A description in natural language of the report that is to be created.'")] string reportDescription, [Description("A heading for the report. Max 50 characters.")] string reportHeading, IOrganizationService orgService, IMcpServer server, RequestContext<CallToolRequestParams> context, CancellationToken ct)
    {
        try
        {
            ProgressToken? progressToken = context.Params?.ProgressToken is ProgressToken pt ? pt : null;   

            await NotifyProgress(server, progressToken, 0, "Executing query...");

            FetchExpression fetchExpression = new FetchExpression(fetchXmlRequest);
            EntityCollection result = orgService.RetrieveMultiple(fetchExpression);
            
            await NotifyProgress(server, progressToken, 1, "Generating report...");

            var jsonResult = Newtonsoft.Json.JsonConvert.SerializeObject(result);

            var samplingResponse = await server.SampleAsync([
                 new ChatMessage(ChatRole.User, $"A report should be generated in Chart.js that fulfills this requirement: {reportDescription}. I want you to create Chart.js code that replaces the '[ChartJsCode]' placeholder in this template: ```const ctx = document.getElementById('myChart'); [ChartJsCode] new Chart(ctx, config);  ``` Only return the exact code, nothing else. Dont't return markdown, always return pure Javascript code. The data that the report should be based on is the following: {jsonResult}. If you need to insert data under the chart, there is a div with id 'additionalContent' that you can access using Javascript."),
            ],
             options: new ChatOptions
             {
                 MaxOutputTokens = 65536,
                 Temperature = 0f,
             },
             cancellationToken: ct);

            // Read the template file
            string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chartTemplates", "template.html");
            string templateHtml = await File.ReadAllTextAsync(templatePath, ct);

            // Replace the placeholder
            string reportHtml = templateHtml.Replace("[ChartJsCode]", samplingResponse.Text);

            reportHtml = reportHtml.Replace("[reportHeading]", reportHeading);

            var uri = await ResourceAdder.AddHtmlFile(server, DateTime.Now.ToString(), reportHtml, ct, reportDescription);
            return $"The report has been saved to an MCP resource and is viewable at: {uri}. You can open this URL in a browser, or add it as context via 'Add Context...' -> 'MCP Resources' in the Copilot chat window.";
        }
        catch (Exception err)
        {
            var errorString = "[ERROR] " + err.ToString();
            Console.Error.WriteLine(err.ToString());

            return errorString;
        }
    }

    [McpServerTool, Description("Creates a report using Chart.js that visualizes the provided data and returns a link to the report. If the request fails, the response will be prepended with [ERROR] and the error should be presented to the user.")]
    public static async Task<string> CreateReportFromData([Description("The data in CSV format.")]string csvData,[Description("A description in natural language of the report that is to be created.'")]string reportDescription, [Description("A heading for the report. Max 50 characters.")] string reportHeading, IOrganizationService orgService, IMcpServer server, RequestContext<CallToolRequestParams> context, CancellationToken ct)
    {
        try
        {
            ProgressToken? progressToken = context.Params?.ProgressToken is ProgressToken pt ? pt : null;  

            await NotifyProgress(server, progressToken, 1, "Generating report...");

            var samplingResponse = await server.SampleAsync([
                 new ChatMessage(ChatRole.User, $"A report should be generated in Chart.js that fulfills this requirement: {reportDescription}. I want you to create Chart.js code that replaces the '[ChartJsCode]' placeholder in this template: ```const ctx = document.getElementById('myChart'); [ChartJsCode] new Chart(ctx, config);  ``` Only return the exact code, nothing else. Dont't return markdown, always return pure Javascript code. The data that the report should be based on is the following: {csvData}. If you need to insert data under the chart, there is a div with id 'additionalContent' that you can access using Javascript."),
            ],
             options: new ChatOptions
             {
                 MaxOutputTokens = 65536,
                 Temperature = 0f,
             },
             cancellationToken: ct);
            // Read the template file
            string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chartTemplates", "template.html");
            string templateHtml = await File.ReadAllTextAsync(templatePath, ct);

            // Replace the placeholder
            string reportHtml = templateHtml.Replace("[ChartJsCode]", samplingResponse.Text);

            reportHtml = reportHtml.Replace("[reportHeading]", reportHeading);

             var uri = await ResourceAdder.AddHtmlFile(server, DateTime.Now.ToString(), reportHtml, ct, reportDescription);
            return $"The report has been saved to an MCP resource and is viewable at: {uri}. You can open this URL in a browser, or add it as context via 'Add Context...' -> 'MCP Resources' in the Copilot chat window.";
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

            // Safely access the progress token (could be absent)
            ProgressToken? progressToken = context.Params?.ProgressToken is ProgressToken pt ? pt : null;
            await NotifyProgress(server, progressToken, 0, $"Starting creation of {count} contacts.", count);

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

                    await NotifyProgress(server, progressToken, i + 1, $"Created {i + 1}/{count} contacts.", count);
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

// Internal helpers
public static partial class Tools
{
    private static async Task NotifyProgress(IMcpServer server, ProgressToken? token, int progress, string message, int? total = null)
    {
        if (token == null) return; // No progress token supplied by caller
        try
        {
            await server.NotifyProgressAsync(token.Value, new()
            {
                Progress = progress,
                Message = message,
                Total = total ?? 0
            });
        }
        catch (Exception ex)
        {
            // Swallow/log: progress updates should not break primary flow
            Console.Error.WriteLine($"Progress notification failed: {ex.Message}");
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
    private const string BaseHttpUrl = "http://localhost:3001"; // TODO: derive from configuration if needed

    private static string NextUri(string fileExtension)
    {
        int id = Interlocked.Increment(ref _counter); // thread-safe

        return $"file://files/{id}.{fileExtension}";
    }

    public static async Task<string> AddMarkdownFile(IMcpServer server, string resourceName, string content, CancellationToken ct, string description)
    {
        // Create an internal file:// uri for storage & MCP catalog key
        string internalUri = NextUri("md"); // e.g. file://files/4.md
        var idFileName = internalUri.Split('/').Last(); // 4.md

        // Public HTTP URL exposed via dynamic endpoint
        string publicUrl = $"{BaseHttpUrl}/dynamic/{idFileName}";

        var resource = new Resource
        {
            Uri = internalUri, 
            Name = resourceName,
            Title = resourceName,
            MimeType = "text/markdown",
            Description = description
        };

        // Store under internalUri so retrieval endpoint can locate it; also store under public URL for direct mapping
        _files[internalUri] = new FileEntry(resource.MimeType!, content, null);
        _files[resource.Uri] = new FileEntry(resource.MimeType!, content, null);

        await AddAsync(server, resource, ct);

        return publicUrl;
    }
    
     public static async Task<string> AddHtmlFile(IMcpServer server, string resourceName, string content, CancellationToken ct, string description)
    {
        // Create an internal file:// uri for storage & MCP catalog key
        string internalUri = NextUri("html"); // e.g. file://files/3.html
        var idFileName = internalUri.Split('/').Last(); // 3.html

        // Public HTTP URL exposed via dynamic endpoint
        string publicUrl = $"{BaseHttpUrl}/dynamic/{idFileName}";

        var resource = new Resource
        {
            Uri = publicUrl, // Expose HTTP URL to clients
            Name = resourceName,
            Title = resourceName,
            MimeType = "text/html",
            Description = description
        };

        // Store under internalUri so retrieval endpoint can locate it; also store under public URL for direct mapping
        _files[internalUri] = new FileEntry(resource.MimeType!, content, null);
        _files[resource.Uri] = new FileEntry(resource.MimeType!, content, null);

        await AddAsync(server, resource, ct);

        return resource.Uri;
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


