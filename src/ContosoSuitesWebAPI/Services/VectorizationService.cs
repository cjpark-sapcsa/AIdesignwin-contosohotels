using ContosoSuitesWebAPI.Entities;
using Microsoft.Azure.Cosmos;
using System.Globalization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace ContosoSuitesWebAPI.Services
{
    public class VectorizationService(Kernel kernel, CosmosClient cosmosClient, IConfiguration configuration) : IVectorizationService
    {
        private readonly Kernel _kernel = kernel;
        private readonly CosmosClient _cosmosClient = cosmosClient;
        private readonly IConfiguration _configuration = configuration;
        private readonly string _embeddingDeploymentName = configuration.GetValue<string>("AzureOpenAI:EmbeddingDeploymentName") ?? "text-embedding-ada-002";

        public async Task<float[]> GetEmbeddings(string text)
        {
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            // Generate a vector for the provided text.
            var embeddings = await _kernel.GetRequiredService<ITextEmbeddingGenerationService>().GenerateEmbeddingAsync(text);
#pragma warning restore SKEXP0001
            var vector = embeddings.ToArray();
            return vector;
        }

        public async Task<List<VectorSearchResult>> ExecuteVectorSearch(float[] queryVector, int max_results = 10, double minimum_similarity_score = 0.8)
        {
            var db = _cosmosClient.GetDatabase(_configuration.GetValue<string>("CosmosDB:DatabaseName") ?? "ContosoSuites");
            var container = db.GetContainer(_configuration.GetValue<string>("CosmosDB:MaintenanceRequestsContainerName") ?? "MaintenanceRequests");

            var vectorString = string.Join(", ", queryVector.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray());

            var query = $@"
                SELECT c.hotel_id AS HotelId, c.hotel AS Hotel, c.details AS Details, 
                VectorDistance(c.request_vector, [{vectorString}]) AS SimilarityScore
                FROM c
                WHERE VectorDistance(c.request_vector, [{vectorString}]) > {minimum_similarity_score.ToString(CultureInfo.InvariantCulture)}
                ORDER BY VectorDistance(c.request_vector, [{vectorString}])";

            var results = new List<VectorSearchResult>();

            using var feedIterator = container.GetItemQueryIterator<VectorSearchResult>(new QueryDefinition(query));
            while (feedIterator.HasMoreResults)
            {
                foreach (var item in await feedIterator.ReadNextAsync())
                {
                    results.Add(item);
                }
            }
            return max_results > 0 ? results.Take(max_results).ToList() : results;
        }
    }
}
