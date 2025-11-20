# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WPF chat application ("TroubleShooter") with RAG (Retrieval-Augmented Generation) capabilities using on-device Korean embeddings and PostgreSQL pgvector for document search.

**Tech Stack**: .NET 9.0 WPF, ONNX Runtime (KURE-v1 embeddings), PostgreSQL with pgvector, Semantic Kernel

## Build and Run Commands

```bash
# Build the solution
dotnet build WpfApp1.sln

# Run the application
dotnet run --project WpfApp1.csproj

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
- Schema: `DocumentChunk` with id, text, metadata, embedding (1024-dim), text_search_tsv
- Connection string hardcoded: `Host=localhost;Database=vector_db;Username=postgres;Password=Bluegood166*!`
- Key methods:
  - `VectorSearchAsync(string searchText, int top = 5)` - semantic search only
  - `HybridSearchAsync(string searchText, string[] keywords, int top = 5)` - semantic + keyword
  - `SearchDocumentsAsync(...)` - unified search interface

**MainWindow** (`MainWindow.xaml/.cs`):
- Chat interface with message bubbles (Claude/User)
- UI currently displays mock responses (AI integration pending)
- Korean language interface with placeholder text "답글..."
- Send message: Enter key (Shift+Enter for newline)

### Data Flow

1. User sends message → `SendMessage()` in MainWindow
2. (Planned) Query → RAGService → EmbeddingService → KURE-v1 model → embeddings
3. (Planned) Embeddings → PostgreSQL pgvector → search results → Context for LLM
4. (Planned) LLM response → Display in chat UI

## Key Technical Details

**ONNX Model Inference**:
- Model: `model/model_quint8_avx2.onnx` (quantized for performance)
- Tokenizer: `model/tokenizer.json`
- GPU acceleration via DirectML if available, falls back to CPU
- Mean pooling over token embeddings, L2 normalized output

**pgvector Integration**:
- Uses Semantic Kernel's `PostgresVectorStore` abstraction
- Collection name: `knowledge_base` (table name)
- HNSW index for fast approximate nearest neighbor search
- Cosine distance metric for similarity

**Vector Store Schema**:
```csharp
[VectorStoreKey] int Id
[VectorStoreData] string Text
[VectorStoreData] string Metadata  // JSON serialized
[VectorStoreVector(1024, CosineDistance, Hnsw)] ReadOnlyMemory<float> Embedding
```

## Important Notes

- **Database credentials**: Currently hardcoded in RAGService constructor - should be moved to configuration
- **Model files**: Must exist in `model/` directory relative to executable
- **Korean language**: UI and model optimized for Korean text
- **AI integration**: Chat UI complete, but AI response generation not yet implemented (line 215 in MainWindow.xaml.cs)
- **DirectML**: Requires compatible GPU, gracefully falls back to CPU if unavailable

## Development Patterns

- **Async/await**: All embedding and search operations are async
- **IDisposable**: Both EmbeddingService and RAGService implement proper resource cleanup
- **XAML styling**: Inline styles with rounded corners, drop shadows, color palette (#FAFAF9, #E5E5E5, #7CB991)
- **Korean text**: UI labels and messages in Korean, code comments in Korean
