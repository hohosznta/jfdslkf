using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.PgVector;
using Npgsql;
using System.Text.Json;
using NetKiwi;
using NetKiwi.Backend;
using System.IO;

namespace WpfApp1
{
    public class RAGService : IDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly EmbeddingService _embeddingService;
        private readonly NpgsqlDataSource _dataSource;
        private readonly VectorStore _vectorStore;
        private readonly SharpKiwi _kiwi;

        public RAGService(
            string connectionString = "Host=172.30.1.95;Database=postgres;Username=postgres;Password=Bluegood166*!",
            string tableName = "data_contextual_rag_vectors")
        {
            _connectionString = connectionString;
            _tableName = tableName;
            _embeddingService = new EmbeddingService();

            // NetKiwi 모델 경로를 실행 파일 위치 기준으로 설정
            var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "netkiwi", "models");
            _kiwi = new SharpKiwi(modelPath);

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
        /// Schema: id, text, metadata, node_id, embedding, text_search_tsv, category
        /// </summary>
        public class DocumentChunk
        {
            [VectorStoreKey(StorageName = "id")]
            public int Id { get; set; }

            [VectorStoreData(StorageName = "text")]
            public string Text { get; set; }

            [VectorStoreData(StorageName = "metadata_")]
            public string Metadata { get; set; }

            [VectorStoreData(StorageName = "categories", IsIndexed = true)]
            public List<string> Category { get; set; }

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
        /// Uses Reciprocal Rank Fusion to combine vector search and keyword search results.
        /// </summary>
        public async Task<List<DocumentSearchResult>> HybridSearchAsync(
            string searchText,
            string[] keywords,
            int top)
        {
            // 1. 벡터 검색 수행 (더 많은 결과 가져오기)
            var vectorResults = await VectorSearchWithoutFilterAsync(searchText, top * 3);

            // 2. 키워드 검색 수행 (PostgreSQL full-text search)
            var keywordResults = await KeywordSearchAsync(keywords, top * 3);

            // 3. RRF (Reciprocal Rank Fusion)를 사용하여 결과 병합
            var mergedResults = MergeResultsWithRRF(vectorResults, keywordResults, k: 60);

            // 4. 상위 결과만 반환
            return mergedResults.Take(top).ToList();
        }

        /// <summary>
        /// Perform keyword-based search using PostgreSQL full-text search.
        /// </summary>
        private async Task<List<DocumentSearchResult>> KeywordSearchAsync(string[] keywords, int top)
        {
            if (keywords == null || keywords.Length == 0)
            {
                return new List<DocumentSearchResult>();
            }

            var results = new List<DocumentSearchResult>();

            using var connection = await _dataSource.OpenConnectionAsync();

            // PostgreSQL full-text search query
            // ts_rank를 사용하여 관련성 점수 계산
            var keywordQuery = string.Join(" | ", keywords); // OR 검색

            var sql = $@"
                SELECT id, text, metadata_,
                       ts_rank(to_tsvector('simple', text), to_tsquery('simple', @query)) as rank
                FROM {_tableName}
                WHERE to_tsvector('simple', text) @@ to_tsquery('simple', @query)
                ORDER BY rank DESC
                LIMIT @limit";

            using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("query", keywordQuery);
            cmd.Parameters.AddWithValue("limit", top);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new DocumentSearchResult
                {
                    Id = reader.GetInt32(0).ToString(),
                    Text = reader.GetString(1),
                    Metadata = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Score = Convert.ToDouble(reader.GetFloat(3))
                });
            }

            return results;
        }

        /// <summary>
        /// Vector search without category filter.
        /// </summary>
        private async Task<List<DocumentSearchResult>> VectorSearchWithoutFilterAsync(string searchText, int top)
        {
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
        /// Merge results from vector and keyword search using Reciprocal Rank Fusion (RRF).
        /// RRF formula: score(d) = Σ 1 / (k + rank(d))
        /// </summary>
        private List<DocumentSearchResult> MergeResultsWithRRF(
            List<DocumentSearchResult> vectorResults,
            List<DocumentSearchResult> keywordResults,
            int k = 60)
        {
            var rrfScores = new Dictionary<string, (DocumentSearchResult result, double score)>();

            // Vector search 결과에 RRF 점수 부여
            for (int i = 0; i < vectorResults.Count; i++)
            {
                var result = vectorResults[i];
                var rrfScore = 1.0 / (k + i + 1); // rank는 1부터 시작

                if (!rrfScores.ContainsKey(result.Id))
                {
                    rrfScores[result.Id] = (result, rrfScore);
                }
                else
                {
                    var existing = rrfScores[result.Id];
                    rrfScores[result.Id] = (existing.result, existing.score + rrfScore);
                }
            }

            // Keyword search 결과에 RRF 점수 부여
            for (int i = 0; i < keywordResults.Count; i++)
            {
                var result = keywordResults[i];
                var rrfScore = 1.0 / (k + i + 1);

                if (!rrfScores.ContainsKey(result.Id))
                {
                    rrfScores[result.Id] = (result, rrfScore);
                }
                else
                {
                    var existing = rrfScores[result.Id];
                    rrfScores[result.Id] = (existing.result, existing.score + rrfScore);
                }
            }

            // RRF 점수로 정렬하여 반환
            return rrfScores
                .OrderByDescending(x => x.Value.score)
                .Select(x =>
                {
                    var result = x.Value.result;
                    result.Score = x.Value.score; // RRF 점수로 업데이트
                    return result;
                })
                .ToList();
        }

        /// <summary>
        /// Perform vector search only (no keyword matching).
        /// </summary>
        public async Task<List<DocumentSearchResult>> VectorSearchAsync(
            string searchText,
            int top,
            string category)
        {
            var embeddings = await _embeddingService.GetEmbeddingsAsync(searchText);
            if (embeddings.Length == 0)
            {
                return new List<DocumentSearchResult>();
            }

            var vectorSearchOptions = new VectorSearchOptions<DocumentChunk>
            {
                Filter = r => r.Category.Contains(category)
            };


            var searchCollection = _vectorStore.GetCollection<int, DocumentChunk>(_tableName);
            var searchResults = searchCollection.SearchAsync(embeddings[0], top: top, vectorSearchOptions);

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
        /// Extract keywords from Korean text using morphological analysis.
        /// Extracts nouns, verbs, adjectives, and proper nouns.
        /// </summary>
        /// <param name="text">Text to analyze</param>
        /// <returns>Array of extracted keywords</returns>
        public string[] ExtractKeywords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            try
            {
                Result[] results = _kiwi.Analyze(text);
                var keywords = new List<string>();

                foreach (Result result in results)
                {
                    foreach (Token token in result.morphs)
                    {
                        // Extract meaningful keywords: Nouns (NN*), Verbs (VV), Adjectives (VA), Proper Nouns (NNP)
                        if (token.tag.StartsWith("NN") ||  // General nouns
                            token.tag.StartsWith("VV") ||  // Verbs
                            token.tag.StartsWith("VA") ||  // Adjectives
                            token.tag.StartsWith("NNP"))   // Proper nouns
                        {
                            // Filter out single-character words for better quality
                            if (token.form.Length > 1)
                            {
                                keywords.Add(token.form);
                            }
                        }
                    }
                }

                return keywords.Distinct().ToArray();
            }
            catch
            {
                // If morphological analysis fails, return empty array
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Search for handover documents with optional keywords.
        /// If keywords are not provided, automatically extracts them using morphological analysis.
        /// </summary>
        public async Task<List<DocumentSearchResult>> SearchDocumentsAsync(
            string query,
            string[] keywords = null,
            int top = 3,
            string category=null)
        {
            // Auto-extract keywords if not provided
            if (keywords == null || keywords.Length == 0)
            {
                keywords = ExtractKeywords(query);
            }

            // Use hybrid search if keywords are available, otherwise vector search only
            if (keywords != null && keywords.Length > 0)
            {
                return await HybridSearchAsync(query, keywords, top);
            }
            else
            {
                return await VectorSearchAsync(query, top, category);
            }
        }

        public void Dispose()
        {
            _embeddingService?.Dispose();
            _vectorStore?.Dispose();
        }
    }
}
