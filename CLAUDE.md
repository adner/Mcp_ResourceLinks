# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an MCP (Model Context Protocol) server implementation called MyDataverseMcpServer, built with ASP.NET Core and .NET 9.0. The server provides Dataverse integration with advanced MCP features including [Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation), [Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling), and [Resources](https://modelcontextprotocol.io/specification/draft/server/resources). Beyond basic data retrieval, it features Chart.js report generation capabilities that transform Dataverse data into interactive visualizations served via HTTP endpoints.

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
- Sets up resource list/read handlers with smart result optimization
- Configures static file serving for Chart.js templates and assets
- Maps dynamic HTTP endpoint `/dynamic/{file}` for serving generated reports and resources
- Maps MCP endpoints and starts server on port 3001

**Dataverse Integration:**
- `IOrganizationService` - Singleton service for Dataverse operations using ClientSecret authentication
- Configuration-driven connection string with URL, ClientId, and Secret from appsettings
- Error handling with descriptive exceptions for missing configuration

**Resource Management System:**
- `ResourceCatalog` - Thread-safe storage for resource metadata using ConcurrentDictionary
- `ResourceAdder` - Manages file storage and URI generation with thread-safe counter
- `AddMarkdownFile` - Creates markdown resources with public HTTP URLs for direct access
- `AddHtmlFile` - Creates HTML resources for Chart.js reports with template-based generation
- Smart result handling: prompts user via Elicitation to save any result as resource

### MCP Server Tools

- **ExecuteFetch** - Executes FetchXML queries with query description parameter. Uses [MCP Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation) to offer resource storage with LLM-generated markdown formatting via [MCP Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling)
- **CreateReportFromQuery** - Generates Chart.js visualizations from FetchXML queries with [MCP Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling) and [progress notifications](https://modelcontextprotocol.io/specification/draft/client/progress)
- **CreateReportFromData** - Creates Chart.js reports from CSV data using LLM-powered code generation
- **CreateTextResource** - Creates basic [MCP Resources](https://modelcontextprotocol.io/specification/draft/server/resources) with specified name and content  
- **WhoAmI** - Returns current user identity from Dataverse
- **BulkCreateRandomContacts** - Creates 1-100 random contact records with [progress notifications](https://modelcontextprotocol.io/specification/draft/client/progress)

### Key Design Patterns

- **[MCP Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation) Workflow**: Universal result handling prompts user to save any result as resource
- **[MCP Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling) Integration**: LLM-powered transformations for both markdown tables and Chart.js code generation
- **Chart.js Report Generation**: Template-based HTML reports with placeholder replacement for dynamic content
- **Dual Resource Architecture**: Internal file:// URIs for MCP catalog with public HTTP URLs for browser access
- **Static + Dynamic Serving**: Static file middleware for assets (logo.png) with dynamic endpoint for generated content
- **Enhanced [Resource Management](https://modelcontextprotocol.io/specification/draft/server/resources)**: Supports both `.md` and `.html` file extensions with appropriate MIME types
- **Configuration Security**: Sensitive credentials in non-committed local file
- **[Progress Notifications](https://modelcontextprotocol.io/specification/draft/client/progress)**: Real-time updates for long-running operations with error-safe implementation
- **URI Generation**: Auto-incrementing counter creates unique `file://files/{id}.{ext}` URIs with file extensions
- **Thread Safety**: Uses ConcurrentDictionary and Interlocked operations for multi-threaded access

### Dependencies

- **ModelContextProtocol.AspNetCore** v0.3.0-preview.3 - MCP server framework
- **Microsoft.PowerPlatform.Dataverse.Client** v1.2.10 - Dataverse SDK
- **Newtonsoft.Json** - JSON serialization for Dataverse results

## Project Structure

- `MyDataverseMcpServer/` - Main project containing the MCP server implementation
- `MyDataverseMcpServer/chartTemplates/` - Chart.js HTML templates and static assets (logo.png)
- `appsettings.local.json` - Local configuration file (not committed, copy from appsettings.json)  
- `README.md` - Documents the MCP server's advanced features with demo video link
- Project file includes Debug-only copy rules for local configuration and Chart.js templates

## MCP Advanced Features Demonstrated

This server showcases advanced MCP capabilities:
- **[Elicitation](https://modelcontextprotocol.io/specification/draft/client/elicitation)**: Interactive user prompts for decision making on result handling
- **[Sampling](https://modelcontextprotocol.io/specification/draft/client/sampling)**: LLM calls to transform data formats (JSON to markdown tables, Chart.js code generation)
- **[Resources](https://modelcontextprotocol.io/specification/draft/server/resources)**: Dynamic resource creation with markdown and HTML formatting, accessible via HTTP URLs
- **[Progress](https://modelcontextprotocol.io/specification/draft/client/progress)**: Real-time progress updates for bulk operations and report generation
- **Chart.js Integration**: Template-based interactive visualization generation with browser-viewable reports
- See [demo video](https://www.youtube.com/watch?v=d1r9o559xkM) showing VS Code integration

## Chart.js Report Generation

### Template System
- `chartTemplates/template.html` - Base HTML template with Chart.js setup
- Placeholder replacement: `[ChartJsCode]` for dynamic chart configuration, `[reportHeading]` for titles
- LLM generates Chart.js code via MCP Sampling based on data and report requirements

### HTTP Serving
- Generated reports accessible at `http://localhost:3001/dynamic/{filename}.html`
- Static assets (logo.png) served from `/dynamic/` path for report branding
- Reports can be opened directly in browser or added as MCP Resources to AI context