# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an MCP (Model Context Protocol) server implementation called MyDataverseMcpServer, built with ASP.NET Core and .NET 9.0. The server provides Dataverse integration with advanced MCP features including [Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation), [Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling), and [Resources](https://modelcontextprotocol.io/specification/draft/server/resources). For large FetchXML result sets (>20 records), it uses MCP Elicitation to ask users if they want results saved as MCP Resources, then uses MCP Sampling to transform raw JSON into markdown tables before storing.

## Common Commands

### Build and Run
- `dotnet build` - Build the solution
- `dotnet run --project MyDataverseMcpServer --environment Development` - Run the MCP server with local configuration
- `dotnet restore` - Restore NuGet packages

### Configuration Setup
- Copy `appsettings.json` structure to `appsettings.local.json` and fill in actual Dataverse credentials
- `appsettings.local.json` is excluded from git commits for security
- The project loads local configuration automatically in Debug mode

### Development
- Server runs on port 3001 by default
- Launch settings configured to not open browser automatically (suitable for MCP server usage)
- Uses Development environment by default, which loads `appsettings.local.json`

## Architecture

### Core Components

**Program.cs** - Main application entry point that:
- Loads local configuration file (`appsettings.local.json`) 
- Configures Dataverse ServiceClient as singleton with connection string authentication
- Configures MCP server with HTTP transport and tools from assembly
- Sets up resource list/read handlers with large result optimization
- Maps MCP endpoints and starts server on port 3001

**Dataverse Integration:**
- `IOrganizationService` - Singleton service for Dataverse operations using ClientSecret authentication
- Configuration-driven connection string with URL, ClientId, and Secret from appsettings
- Error handling with descriptive exceptions for missing configuration

**Resource Management System:**
- `ResourceCatalog` - Thread-safe storage for resource metadata using ConcurrentDictionary
- `ResourceAdder` - Manages file storage and URI generation with thread-safe counter
- Smart result handling: large FetchXML results (>20 records) prompt user to save as resource

### MCP Server Tools

- **ExecuteFetch** - Executes FetchXML queries with query description parameter. For >20 results, uses [MCP Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation) to offer resource storage with LLM-generated markdown formatting via [MCP Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling)
- **CreateTextResource** - Creates basic [MCP Resources](https://modelcontextprotocol.io/specification/draft/server/resources) with specified name and content  
- **WhoAmI** - Returns current user identity from Dataverse
- **BulkCreateRandomContacts** - Creates 1-100 random contact records with [progress notifications](https://modelcontextprotocol.io/specification/draft/client/progress)

### Key Design Patterns

- **[MCP Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation) Workflow**: For large result sets, prompts user via MCP Elicitation with Yes/No choice to save as resource
- **[MCP Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling) Integration**: Uses MCP Sampling to call LLM for transforming JSON to markdown tables with proper formatting
- **Enhanced [Resource Management](https://modelcontextprotocol.io/specification/draft/server/resources)**: `AddMarkdownFile` method creates `.md` file extensions with descriptive metadata
- **Configuration Security**: Sensitive credentials in non-committed local file
- **[Progress Notifications](https://modelcontextprotocol.io/specification/draft/client/progress)**: Real-time updates for long-running operations  
- **URI Generation**: Auto-incrementing counter creates unique `file://files/{id}.{ext}` URIs with file extensions
- **Thread Safety**: Uses ConcurrentDictionary and Interlocked operations for multi-threaded access

### Dependencies

- **ModelContextProtocol.AspNetCore** v0.3.0-preview.3 - MCP server framework
- **Microsoft.PowerPlatform.Dataverse.Client** v1.2.10 - Dataverse SDK
- **Newtonsoft.Json** - JSON serialization for Dataverse results

## Project Structure

- `MyDataverseMcpServer/` - Main project containing the MCP server implementation
- `appsettings.local.json` - Local configuration file (not committed, copy from appsettings.json)  
- `README.md` - Documents the MCP server's advanced features with demo video link
- Project file includes Debug-only copy rules for local configuration

## MCP Advanced Features Demonstrated

This server showcases advanced MCP capabilities:
- **[Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation)**: Interactive user prompts for decision making on large result handling
- **[Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling)**: LLM calls to transform data formats (JSON to markdown tables)
- **[Resources](https://modelcontextprotocol.io/specification/draft/server/resources)**: Dynamic resource creation with user-friendly markdown formatting
- **[Progress](https://modelcontextprotocol.io/specification/draft/client/progress)**: Real-time progress updates for bulk operations
- See [demo video](https://www.youtube.com/watch?v=d1r9o559xkM) showing VS Code integration