using ContosoSuitesWebAPI.Entities;

namespace ContosoSuitesWebAPI.Services
{
    public interface IVectorizationService
    {
        Task<float[]> GetEmbeddings(string text);

        // ✅ **Ensure this method exists in the interface**
        Task<List<VectorSearchResult>> ExecuteVectorSearch(float[] queryVector, int max_results = 10, double minimum_similarity_score = 0.8);
    }
}
