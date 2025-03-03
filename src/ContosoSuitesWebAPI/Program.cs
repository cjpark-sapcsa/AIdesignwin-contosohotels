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
using System.Text.Json;  // ✅ Added missing namespace for JsonElement
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

    // ✅ Register `MaintenanceRequestPlugin`
    kernelBuilder.Plugins.AddFromType<MaintenanceRequestPlugin>("MaintenanceCopilot");

    return kernelBuilder.Build();
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
app.MapPost("/api/Chat", async Task<string>(HttpRequest request) =>
{
    if (!request.HasFormContentType || !request.Form.ContainsKey("message"))
    {
        return "Bad Request: Message is required.";
    }

    var message = request.Form["message"];
    var kernel = app.Services.GetRequiredService<Kernel>();
    var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
    var executionSettings = new OpenAIPromptExecutionSettings
    {
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
    };
    var response = await chatCompletionService.GetChatMessageContentAsync(message.ToString(), executionSettings, kernel);
    return response?.Content!;
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
