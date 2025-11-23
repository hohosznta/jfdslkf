using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.PgVector;
using Npgsql;
using System.Text.Json;

namespace WpfApp1
{
    public class RAGService : IDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly EmbeddingService _embeddingService;
        private readonly NpgsqlDataSource _dataSource;
        private readonly VectorStore _vectorStore;

        public RAGService(
            string connectionString = "Host=localhost;Database=postgres;Username=postgres;Password=password",
            string tableName = "data_contextual_rag_vectors")
        {
            _connectionString = connectionString;
            _tableName = tableName;
            _embeddingService = new EmbeddingService();

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
            dataSourceBuilder.UseVector();
            _dataSource = dataSourceBuilder.Build();

            _vectorStore = new PostgresVectorStore(_dataSource, ownsDataSource: true);
        }

        /// <summary>
        /// Initialize the embedding model.
        /// </summary>
        public void InitializeModel()
        {
            _embeddingService.InitModel();
        }

        /// <summary>
        /// Document chunk model matching LlamaIndex schema.
        /// Schema: id, text, metadata, node_id, embedding, text_search_tsv
        /// </summary>
        public class DocumentChunk
        {
            [VectorStoreKey(StorageName = "id")]
            public int Id { get; set; } 

            [VectorStoreData(StorageName = "text")]
            public string Text { get; set; } 

            [VectorStoreData(StorageName = "metadata_")]
            public string Metadata { get; set; } 

            [VectorStoreVector(Dimensions: 1024, DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Hnsw, StorageName = "embedding")]
            public ReadOnlyMemory<float>? Embedding { get; set; }
        }

        /// <summary>
        /// Search result with parsed metadata.
        /// </summary>
        public class DocumentSearchResult
        {
            public string Id { get; set; }
            public string Text { get; set; }
            public string Metadata { get; set; }
            public double? Score { get; set; }

            /// <summary>
            /// Parse metadata JSON to dictionary.
            /// </summary>
            public Dictionary<string, object> GetMetadataDict()
            {
                if (string.IsNullOrEmpty(Metadata))
                    return new Dictionary<string, object>();

                try
                {
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(Metadata);
                }
                catch
                {
                    return new Dictionary<string, object>();
                }
            }
        }

        /// <summary>
        /// Perform hybrid search combining vector similarity and keyword matching using RRF.
        /// </summary>
        public async Task<List<DocumentSearchResult>> HybridSearchAsync(
            string searchText,
            string[] keywords,
            int top = 5)
        {
            // Generate embedding for search text using KURE-v1
            var embeddings = await _embeddingService.GetEmbeddingsAsync(searchText);
            if (embeddings.Length == 0)
            {
                return new List<DocumentSearchResult>();
            }

            var hybridSearchCollection = (IKeywordHybridSearchable<DocumentChunk>)_vectorStore.GetCollection<int, DocumentChunk>(_tableName);
            var searchResults = hybridSearchCollection.HybridSearchAsync(embeddings[0], keywords, top: top);

            var results = new List<DocumentSearchResult>();
            await foreach (var result in searchResults)
            {
                results.Add(new DocumentSearchResult
                {
                    Id = result.Record.Id.ToString(),
                    Text = result.Record.Text,
                    Metadata = result.Record.Metadata,
                    Score = result.Score
                });
            }

            return results;
        }

        /// <summary>
        /// Perform vector search only (no keyword matching).
        /// </summary>
        public async Task<List<DocumentSearchResult>> VectorSearchAsync(
            string searchText,
            int top = 5)
        {
            // Generate embedding for search text using KURE-v1
            var embeddings = await _embeddingService.GetEmbeddingsAsync(searchText);
            if (embeddings.Length == 0)
            {
                return new List<DocumentSearchResult>();
            }

            var searchCollection = _vectorStore.GetCollection<int, DocumentChunk>(_tableName);
            var searchResults = searchCollection.SearchAsync(embeddings[0], top: top);

            var results = new List<DocumentSearchResult>();
            await foreach (var result in searchResults)
            {
                results.Add(new DocumentSearchResult
                {
                    Id = result.Record.Id.ToString(),
                    Text = result.Record.Text,
                    Metadata = result.Record.Metadata,
                    Score = result.Score
                });
            }

            return results;
        }

        /// <summary>
        /// Search for handover documents with optional keywords.
        /// </summary>
        public async Task<List<DocumentSearchResult>> SearchDocumentsAsync(
            string query,
            string[] keywords = null,
            int top = 5)
        {
            if (keywords != null && keywords.Length > 0)
            {
                return await HybridSearchAsync(query, keywords, top);
            }
            else
            {
                return await VectorSearchAsync(query, top);
            }
        }

        public void Dispose()
        {
            _embeddingService?.Dispose();
            _vectorStore?.Dispose();
        }
    }
}
