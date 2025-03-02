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
string? embeddingDeploymentName = builder.Configuration["EmbeddingDeploymentName"];

if (string.IsNullOrWhiteSpace(openAIEndpoint) ||
    string.IsNullOrWhiteSpace(openAIKey) ||
    string.IsNullOrWhiteSpace(embeddingDeploymentName))
{
    throw new Exception("OpenAI configuration is missing. Check AzureOpenAIEndpoint, AzureOpenAIKey, and EmbeddingDeploymentName.");
}

// ✅ Register Semantic Kernel
builder.Services.AddSingleton<Kernel>((serviceProvider) =>
{
    IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.AddAzureOpenAIChatCompletion(
        deploymentName: embeddingDeploymentName,
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

    return kernelBuilder.Build();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ✅ Fix: Ensure `VectorSearchRequest.cs` Exists
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

// ✅ Other API Endpoints
app.MapGet("/", () => "Welcome to the Contoso Suites Web API!")
    .WithName("Index")
    .WithOpenApi();

app.MapGet("/Hotels", async ([FromServices] IDatabaseService databaseService) =>
{
    return await databaseService.GetHotels();
})
    .WithName("GetHotels")
    .WithOpenApi();

app.MapGet("/Hotels/{hotelId}/Bookings/", async (int hotelId, [FromServices] IDatabaseService databaseService) =>
{
    return await databaseService.GetBookingsForHotel(hotelId);
})
    .WithName("GetBookingsForHotel")
    .WithOpenApi();

app.MapGet("/Hotels/{hotelId}/Bookings/{min_date}", async (int hotelId, DateTime min_date, [FromServices] IDatabaseService databaseService) =>
{
    return await databaseService.GetBookingsByHotelAndMinimumDate(hotelId, min_date);
})
    .WithName("GetRecentBookingsForHotel")
    .WithOpenApi();

app.MapPost("/Chat", async Task<string>(HttpRequest request) =>
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

app.MapGet("/Vectorize", async (string text, [FromServices] IVectorizationService vectorizationService) =>
{
    return await vectorizationService.GetEmbeddings(text);
})
    .WithName("Vectorize")
    .WithOpenApi();

app.MapPost("/MaintenanceCopilotChat", async ([FromBody] string message, [FromServices] MaintenanceCopilot copilot) =>
{
    throw new NotImplementedException();
})
    .WithName("Copilot")
    .WithOpenApi();

app.Run();
