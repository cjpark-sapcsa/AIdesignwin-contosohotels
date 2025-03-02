namespace ContosoSuitesWebAPI.Entities
{
    public class VectorSearchRequest
    {
        public required float[] QueryVector { get; set; }  // Required query vector
        public int MaxResults { get; set; } = 10;  // Default max results
        public double MinimumSimilarityScore { get; set; } = 0.8;  // Default similarity score
    }
}
