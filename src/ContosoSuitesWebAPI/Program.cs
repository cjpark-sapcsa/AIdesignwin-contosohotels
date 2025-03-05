using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using ContosoSuitesWebAPI.Agents;
using ContosoSuitesWebAPI.Entities;
using ContosoSuitesWebAPI.Plugins;
using ContosoSuitesWebAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;  
using Azure.Core;

var builder = WebApplication.CreateBuilder(args);

// ✅ Enable Logging for Debugging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var logger = loggerFactory.CreateLogger<Program>();

logger.LogInformation("Application is starting...");

// ✅ Register Services for Dependency Injection
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IVectorizationService, VectorizationService>();

// ✅ Register CosmosClient FIRST before any dependent services
builder.Services.AddSingleton<CosmosClient>((serviceProvider) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    string cosmosEndpoint = configuration["CosmosDB:AccountEndpoint"] ?? throw new InvalidOperationException("Missing CosmosDB:AccountEndpoint in configuration.");
    string userAssignedClientId = configuration["AZURE_CLIENT_ID"] ?? throw new InvalidOperationException("Missing AZURE_CLIENT_ID in configuration.");

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
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

// ✅ Register DatabaseService AFTER CosmosClient
builder.Services.AddSingleton<IDatabaseService, DatabaseService>((serviceProvider) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("ContosoSuites") ?? throw new InvalidOperationException("Missing ContosoSuites connection string.");
    return new DatabaseService(connectionString);
});

// ✅ Register `MaintenanceRequestPlugin` AFTER CosmosClient so it resolves properly
builder.Services.AddSingleton<MaintenanceRequestPlugin>((serviceProvider) =>
{
    var cosmosClient = serviceProvider.GetRequiredService<CosmosClient>();
    return new MaintenanceRequestPlugin(cosmosClient);
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

    // ✅ Ensure `MaintenanceRequestPlugin` is registered correctly
    var maintenancePlugin = serviceProvider.GetRequiredService<MaintenanceRequestPlugin>();
    kernelBuilder.Plugins.AddFromObject(maintenancePlugin);

    return kernelBuilder.Build();
});

// ✅ Register `MaintenanceCopilot`
builder.Services.AddSingleton<MaintenanceCopilot>((serviceProvider) =>
{
    var kernel = serviceProvider.GetRequiredService<Kernel>();
    return new MaintenanceCopilot(kernel);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ✅ Fix: Ensure `/api/VectorSearch` Exists
app.MapPost("/api/VectorSearch", async (
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

// ✅ Fix: Ensure `/api/Vectorize` Exists
app.MapGet("/api/Vectorize", async (string text, [FromServices] IVectorizationService vectorizationService) =>
{
    logger.LogInformation("Processing vectorization for text: {text}", text);
    return await vectorizationService.GetEmbeddings(text);
})
.WithName("Vectorize")
.WithOpenApi();

// ✅ Fix: Ensure `/api/Hotels` Exists
app.MapGet("/api/Hotels", async ([FromServices] IDatabaseService databaseService) =>
{
    logger.LogInformation("Fetching hotel list...");
    return await databaseService.GetHotels();
})
.WithName("GetHotels")
.WithOpenApi();

// ✅ Fix: Ensure `/api/Hotels/{hotelId}/Bookings/` Exists
app.MapGet("/api/Hotels/{hotelId}/Bookings/", async (int hotelId, [FromServices] IDatabaseService databaseService) =>
{
    return await databaseService.GetBookingsForHotel(hotelId);
})
.WithName("GetBookingsForHotel")
.WithOpenApi();

// ✅ Fix: Ensure `/api/Hotels/{hotelId}/Bookings/{min_date}` Exists
app.MapGet("/api/Hotels/{hotelId}/Bookings/{min_date}", async (int hotelId, DateTime min_date, [FromServices] IDatabaseService databaseService) =>
{
    return await databaseService.GetBookingsByHotelAndMinimumDate(hotelId, min_date);
})
.WithName("GetRecentBookingsForHotel")
.WithOpenApi();

// ✅ Fix: Ensure `/api/Chat` Exists
app.MapPost("/api/Chat", async ([FromBody] JsonElement body, [FromServices] Kernel kernel) =>
{
    if (!body.TryGetProperty("message", out var messageElement) || messageElement.ValueKind != JsonValueKind.String)
    {
        return Results.BadRequest(new { error = "Message is required." });
    }

    string message = messageElement.GetString()!;
    var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

    var executionSettings = new OpenAIPromptExecutionSettings
    {
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
    };

    var response = await chatCompletionService.GetChatMessageContentAsync(message, executionSettings, kernel);
    
    return Results.Ok(new { message = response?.Content ?? "No response received." });
})
.WithName("Chat")
.WithOpenApi();

// ✅ Fix: Ensure `/api/MaintenanceCopilotChat` Exists
app.MapPost("/api/MaintenanceCopilotChat", async ([FromBody] JsonElement body, [FromServices] MaintenanceCopilot copilot) =>
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
