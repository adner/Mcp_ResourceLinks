# Dataverse MCP Server - Advanced MCP Features Showcase

This repository demonstrates a sophisticated **Model Context Protocol (MCP) Server** that integrates with Microsoft Dataverse and showcases advanced MCP capabilities beyond basic tool execution.

## Key MCP Features Implemented

### 🔄 [MCP Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation)
Interactive decision-making for users when FetchXML queries return large result sets (>20 records). The server prompts users to choose between receiving raw JSON or saving results as formatted resources.

### 🧠 [MCP Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling)
Intelligent data transformation by calling the LLM to convert raw Dataverse JSON responses into user-friendly markdown tables with proper formatting and structure.

### 📁 [MCP Resources](https://modelcontextprotocol.io/specification/draft/server/resources)
Dynamic resource creation that stores transformed data as accessible resources within the MCP ecosystem, complete with descriptive metadata and markdown formatting.

### 📊 [MCP Progress](https://modelcontextprotocol.io/specification/draft/client/progress)
Real-time progress notifications during bulk operations (e.g., creating multiple contact records) with detailed status updates and completion tracking.

## How It Works

1. **Query Execution**: Execute FetchXML queries against Dataverse using the `ExecuteFetch` tool
2. **Smart Result Handling**: 
   - ≤20 records: Return JSON directly
   - >20 records: Trigger MCP Elicitation workflow
3. **User Choice**: Interactive prompt asking whether to save large results as resources
4. **Data Transformation**: If user chooses resource storage, MCP Sampling transforms JSON to markdown
5. **Resource Creation**: Formatted data saved as MCP Resource with descriptive metadata

## Available MCP Tools

- **ExecuteFetch**: FetchXML query execution with smart result handling
- **CreateTextResource**: Basic resource creation for any text content
- **WhoAmI**: Dataverse identity verification
- **BulkCreateRandomContacts**: Bulk data creation with progress tracking

## Demo

This [video](https://www.youtube.com/watch?v=d1r9o559xkM) demonstrates VS Code using this MCP Server to:
- Retrieve Dataverse data through FetchXML queries
- Transform large result sets into formatted resources
- Add resources to the AI context window for enhanced conversations

## Technical Implementation

Built with:
- **ASP.NET Core** and **.NET 9.0**
- **[C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)** for protocol implementation
- **Microsoft.PowerPlatform.Dataverse.Client** for Dataverse integration
- **ModelContextProtocol.AspNetCore** v0.3.0-preview.3

## Getting Started

1. Clone the repository
2. Copy `appsettings.json` to `appsettings.local.json` and configure Dataverse credentials
3. Run: `dotnet run --project MyDataverseMcpServer --environment Development`
4. Server runs on `http://localhost:3001`

The server demonstrates how MCP can create sophisticated, interactive experiences that go far beyond simple request-response patterns.
