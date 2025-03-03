using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using ContosoSuitesWebAPI.Agents;
using ContosoSuitesWebAPI.Entities;
using ContosoSuitesWebAPI.Plugins;
using ContosoSuitesWebAPI.Services;
using Microsoft.Data.SqlClient;
using Azure;
using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ✅ Enable Logging for Debugging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
logger.LogInformation("Application is starting...");

// ✅ Register Services for Dependency Injection
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IVectorizationService, VectorizationService>();

// ✅ Configure DatabaseService
builder.Services.AddSingleton<IDatabaseService, DatabaseService>((_) =>
{
    var connectionString = builder.Configuration.GetConnectionString("ContosoSuites");
    return new DatabaseService(connectionString!);
});

// ✅ Register CosmosClient with Managed Identity Authentication
builder.Services.AddSingleton<CosmosClient>((_) =>
{
    string cosmosEndpoint = builder.Configuration["CosmosDB:AccountEndpoint"]!;
    string userAssignedClientId = builder.Configuration["AZURE_CLIENT_ID"]!;

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        TenantId = "16b3c013-d300-468d-ac64-7eda0820b6d3",
        ManagedIdentityClientId = userAssignedClientId,
        ExcludeVisualStudioCredential = true,
        ExcludeVisualStudioCodeCredential = true,
        ExcludeSharedTokenCacheCredential = true
    });

    var tokenRequestContext = new TokenRequestContext(new[] { "https://cosmos.azure.com/.default" });
    var token = credential.GetTokenAsync(tokenRequestContext).Result;

    logger.LogInformation("Successfully retrieved AAD Token for Cosmos DB: Expires at {ExpiresOn}", token.ExpiresOn);

    return new CosmosClient(cosmosEndpoint, credential);
});

// ✅ Configure OpenAI Integration
string? openAIEndpoint = builder.Configuration["AzureOpenAIEndpoint"];
string? openAIKey = builder.Configuration["AzureOpenAIKey"];
string? chatModelName = builder.Configuration["deployment_name"];
string? embeddingDeploymentName = builder.Configuration["EmbeddingDeploymentName"];

if (string.IsNullOrWhiteSpace(openAIEndpoint) ||
    string.IsNullOrWhiteSpace(openAIKey) ||
    string.IsNullOrWhiteSpace(chatModelName) ||
    string.IsNullOrWhiteSpace(embeddingDeploymentName))
{
    throw new Exception("OpenAI configuration is missing. Check AzureOpenAIEndpoint, AzureOpenAIKey, deployment_name, and EmbeddingDeploymentName.");
}

// ✅ Register Semantic Kernel with Correct Models
builder.Services.AddSingleton<Kernel>((serviceProvider) =>
{
    IKernelBuilder kernelBuilder = Kernel.CreateBuilder();

    kernelBuilder.AddAzureOpenAIChatCompletion(
        deploymentName: chatModelName,
        endpoint: openAIEndpoint,
        apiKey: openAIKey
    );

#pragma warning disable SKEXP0010
    kernelBuilder.AddAzureOpenAITextEmbeddingGeneration(
        deploymentName: embeddingDeploymentName,
        endpoint: openAIEndpoint,
        apiKey: openAIKey
    );
#pragma warning restore SKEXP0010

    var databaseService = serviceProvider.GetRequiredService<IDatabaseService>();
    kernelBuilder.Plugins.AddFromObject(databaseService);

    // ✅ Add MaintenanceRequestPlugin
    kernelBuilder.Plugins.AddFromType<MaintenanceRequestPlugin>("MaintenanceCopilot");

    // ✅ Ensure CosmosClient is available within Kernel service definition
    kernelBuilder.Services.AddSingleton<CosmosClient>((_) =>
    {
        string userAssignedClientId = builder.Configuration["AZURE_CLIENT_ID"]!;
        var credential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = userAssignedClientId
            });
        CosmosClient client = new(
            accountEndpoint: builder.Configuration["CosmosDB:AccountEndpoint"]!,
            tokenCredential: credential
        );
        return client;
    });

    return kernelBuilder.Build();
});

// ✅ Register MaintenanceCopilot
builder.Services.AddSingleton<MaintenanceCopilot>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ✅ Define API Endpoints
app.MapPost("/VectorSearch", async (
    [FromBody] VectorSearchRequest request,  
    [FromServices] IVectorizationService vectorizationService) =>
{
    logger.LogInformation("Received VectorSearch request with {Dimensions} dimensions.", request.QueryVector.Length);

    if (request.QueryVector == null || request.QueryVector.Length == 0)
    {
        logger.LogError("Empty query vector received.");
        return Results.BadRequest("Query vector is required.");
    }

    var results = await vectorizationService.ExecuteVectorSearch(
        request.QueryVector, 
        request.MaxResults, 
        request.MinimumSimilarityScore
    );

    logger.LogInformation("Returning {ResultsCount} results.", results.Count);
    return Results.Ok(results);
})
.WithName("VectorSearch")
.WithOpenApi();

app.MapPost("/MaintenanceCopilotChat", async ([FromBody] JsonElement body, [FromServices] MaintenanceCopilot copilot) =>
{
    if (!body.TryGetProperty("message", out var messageElement) || messageElement.ValueKind != JsonValueKind.String)
    {
        return Results.BadRequest("Invalid request format. Expected JSON: { \"message\": \"your text here\" }");
    }

    string message = messageElement.GetString()!;
    var response = await copilot.Chat(message);
    return Results.Ok(response);
})
.WithName("Copilot")
.WithOpenApi();

app.Run();