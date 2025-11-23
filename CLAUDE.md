# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WPF chat application ("TroubleShooter") with RAG (Retrieval-Augmented Generation) capabilities using on-device Korean embeddings and PostgreSQL pgvector for document search.

**Tech Stack**: .NET 9.0 WPF (SDK 9.0.305), ONNX Runtime (KURE-v1 embeddings), PostgreSQL with pgvector, Semantic Kernel

## Build and Run Commands

```bash
# Build the solution
dotnet build WpfApp1.sln

# Run the WPF application
dotnet run --project WpfApp1.csproj

# Run the console sLM test program (standalone LLM inference)
dotnet run sLM.cs

# Build for Release
dotnet build WpfApp1.sln -c Release

# Clean build artifacts
dotnet clean WpfApp1.sln
```

## Architecture

### Core Services

**EmbeddingService** (`EmbeddingService.cs`):
- On-device Korean text embeddings using KURE-v1 model (BAAI/bge-m3 finetuned)
- ONNX Runtime inference with DirectML GPU acceleration fallback to CPU
- 1024-dimension embeddings with mean pooling and L2 normalization
- Model files located in `model/` directory (ONNX model + tokenizer)
- Key method: `GetEmbeddingsAsync(params string[] sentences)` returns `float[][]`

**RAGService** (`RAGService.cs`):
- Vector database integration using PostgreSQL pgvector via Semantic Kernel
- Hybrid search combining vector similarity + keyword matching (RRF)
- Schema: `DocumentChunk` with id, text, metadata_, embedding (1024-dim)
- Default connection string: `Host=localhost;Database=postgres;Username=postgres;Password=password`
- Table name: `data_contextual_rag_vectors` (default, configurable via constructor)
- Key methods:
  - `VectorSearchAsync(string searchText, int top = 5)` - semantic search only
  - `HybridSearchAsync(string searchText, string[] keywords, int top = 5)` - semantic + keyword using `IKeywordHybridSearchable`
  - `SearchDocumentsAsync(...)` - unified search interface (auto-selects hybrid/vector based on keywords)

**MainWindow** (`MainWindow.xaml/.cs`):
- Chat interface with message bubbles (Claude icon ✱ / User)
- Fully functional RAG search - user messages trigger semantic search via RAGService
- Korean language interface with placeholder text "답글..."
- Send message: Enter key (Shift+Enter for newline)
- Displays formatted search results with similarity scores and metadata

**sLM.cs**:
- Standalone console program for testing ONNX Runtime GenAI
- Uses `model/kanana_int4/` directory (separate from embedding model)
- Direct LLM inference with streaming output (ChatML format: `<|im_start|>user\n...<|im_end|>`)
- Not integrated with WPF app - for testing/debugging only

### Data Flow

1. User sends message → `SendMessage()` in MainWindow
2. Query → `RAGService.SearchDocumentsAsync()` → `EmbeddingService.GetEmbeddingsAsync()`
3. EmbeddingService → KURE-v1 ONNX model → 1024-dim embeddings
4. RAGService → PostgreSQL pgvector → semantic/hybrid search results
5. Results formatted and displayed in chat UI with similarity scores
6. (Future) LLM integration for generative responses using retrieved context

## Key Technical Details

**ONNX Model Inference**:
- Model: `model/model_quint8_avx2.onnx` (quantized for performance)
- Tokenizer: `model/tokenizer.json`
- GPU acceleration via DirectML if available, falls back to CPU
- Mean pooling over token embeddings, L2 normalized output

**pgvector Integration**:
- Uses Semantic Kernel's `PostgresVectorStore` and `NpgsqlDataSource` with `.UseVector()`
- Table name: `data_contextual_rag_vectors` (configurable)
- HNSW index for fast approximate nearest neighbor search
- Cosine distance metric for similarity
- `IKeywordHybridSearchable<DocumentChunk>` for hybrid search support

**Vector Store Schema**:
```csharp
[VectorStoreKey(StorageName = "id")] int Id
[VectorStoreData(StorageName = "text")] string Text
[VectorStoreData(StorageName = "metadata_")] string Metadata  // JSON serialized
[VectorStoreVector(1024, CosineDistance, Hnsw, StorageName = "embedding")] ReadOnlyMemory<float>? Embedding
```

## Important Notes

- **Two separate models**:
  - Embedding model: `model/model_quint8_avx2.onnx` + `model/tokenizer.json` (for RAG)
  - LLM model: `model/kanana_int4/` (for sLM.cs standalone test only)
- **Database credentials**: Default connection in RAGService constructor can be overridden via constructor parameter
- **Model files**: Must exist in `model/` directory relative to executable (copied via .csproj)
- **Korean language**: UI and embedding model optimized for Korean text
- **Current functionality**: RAG search is fully implemented and working - displays search results from PostgreSQL
- **Future enhancement**: Generative AI responses using retrieved context (not yet implemented)
- **DirectML**: Requires compatible GPU, gracefully falls back to CPU if unavailable
- **Platform**: x64 only (see `<PlatformTarget>x64</PlatformTarget>` in .csproj)

## Development Patterns

- **Async/await**: All embedding and search operations are async
- **IDisposable**: Both EmbeddingService and RAGService implement proper resource cleanup
- **XAML styling**: Inline styles in MainWindow.xaml.cs, color palette (#FAFAF9, #E5E5E5, #D9795A for Claude icon)
- **Korean text**: UI labels and messages in Korean
- **Error handling**: Try-catch blocks in MainWindow.SendMessage() display user-friendly error messages
- **UI state management**: `_isProcessing` flag prevents multiple concurrent searches
