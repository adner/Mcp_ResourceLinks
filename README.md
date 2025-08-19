# Dataverse MCP Server - Advanced MCP Features Showcase

This repository demonstrates a sophisticated **Model Context Protocol (MCP) Server** that integrates with Microsoft Dataverse and showcases advanced MCP capabilities beyond basic tool execution. Features include intelligent data visualization through Chart.js report generation.

## Key MCP Features Implemented

### 🔄 [MCP Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation)
When FetchXML queries return large result sets (>20 records), the server prompts users to choose between receiving raw JSON or saving results as formatted resources.

### 🧠 [MCP Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling)
Data transformation by internal calls to the LLM - **sampling** - to convert raw Dataverse JSON responses into user-friendly formats including markdown tables and interactive Chart.js visualizations.

### 📁 [MCP Resources](https://modelcontextprotocol.io/specification/draft/server/resources)
Dynamic resource creation that stores transformed data as accessible resources available from the MCP Server. Supports both markdown documents and interactive HTML reports viewable in browsers.

### 📊 [MCP Progress](https://modelcontextprotocol.io/specification/draft/client/progress)
Real-time progress notifications during bulk operations (e.g., creating multiple contact records, generating reports) with status updates and completion tracking.

## How It Works

### Data Retrieval & Resource Creation
1. **Query Execution**: Execute FetchXML queries against Dataverse using the `ExecuteFetch` tool
2. **Smart Result Handling**: MCP Elicitation prompts user to save results as resources
3. **Data Transformation**: MCP Sampling transforms JSON to markdown tables
4. **Resource Creation**: Formatted data saved as MCP Resource with descriptive metadata

### Report Generation
1. **Data Input**: Provide FetchXML query or CSV data via `CreateReportFromQuery` or `CreateReportFromData`
2. **Chart Generation**: MCP Sampling generates Chart.js code based on data and report requirements
3. **Template Processing**: LLM-generated code inserted into HTML template with custom headings
4. **HTTP Serving**: Interactive reports accessible at `http://localhost:3001/dynamic/{filename}.html`

## Available MCP Tools

### Data Operations
- **ExecuteFetch**: FetchXML query execution with smart result handling
- **WhoAmI**: Dataverse identity verification  
- **BulkCreateRandomContacts**: Bulk data creation with progress tracking

### Report Generation
- **CreateReportFromQuery**: Generate Chart.js visualizations from FetchXML queries
- **CreateReportFromData**: Create Chart.js reports from CSV data with custom headings

### Resource Management
- **CreateTextResource**: Basic resource creation for any text content

## Demo

This [video](https://www.youtube.com/watch?v=d1r9o559xkM) demonstrates VS Code using this MCP Server to:
- Retrieve Dataverse data through FetchXML queries
- Transform large result sets into `Markdown` formatted resources
- Generate interactive Chart.js visualizations from Dataverse data
- Optional adding of resources to the AI context window for enhanced conversations

## Technical Implementation

Built with:
- **ASP.NET Core** and **.NET 9.0**
- **[C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)** for protocol implementation
- **[Microsoft.PowerPlatform.Dataverse.Client](https://www.nuget.org/packages/Microsoft.PowerPlatform.Dataverse.Client/)** for Dataverse integration
- **[ModelContextProtocol.AspNetCore](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/src/ModelContextProtocol.AspNetCore)** v0.3.0-preview.3
- **Chart.js** for interactive data visualizations via template-based generation

## Getting Started

1. Clone the repository
2. Copy `appsettings.json` to `appsettings.local.json` and configure Dataverse credentials
3. Run: `dotnet run --project MyDataverseMcpServer --environment Development`
4. Server runs on `http://localhost:3001`

The server demonstrates how MCP can create sophisticated, interactive experiences that go far beyond simple request-response patterns, including dynamic visualization generation and browser-viewable reports.
