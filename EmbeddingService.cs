using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tokenizers.DotNet;
using System.Diagnostics;
using System.IO;
using System.Numerics;

namespace WpfApp1
{
    public class EmbeddingService : IDisposable
    {
        // KURE-v1 model (BAAI/bge-m3 finetuned for Korean)
        private readonly string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model");
        private InferenceSession? _inferenceSession;
        private Tokenizer? _tokenizer;

        public bool IsModelReady => _inferenceSession != null;

        /// <summary>
        /// Initialize the ONNX model for KURE-v1 embeddings.
        /// </summary>
        public void InitModel()
        {
            if (_inferenceSession != null)
            {
                return;
            }

            var sessionOptions = new SessionOptions
            {
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_INFO
            };

            // Use DirectML for GPU acceleration if available
            try
            {
                sessionOptions.AppendExecutionProvider_DML(0);
            }
            catch
            {
                Debug.WriteLine("DirectML not available, using CPU");
            }

            string modelPath = Path.Combine(modelDir, "model_quint8_avx2.onnx");
            _inferenceSession = new InferenceSession(modelPath, sessionOptions);

            // Initialize tokenizer
            string tokenizerPath = Path.Combine(modelDir, "tokenizer.json");
            _tokenizer = new Tokenizer(tokenizerPath);

            Debug.WriteLine("KURE-v1 model and tokenizer loaded successfully");
        }

        /// <summary>
        /// Generate embeddings using KURE-v1 model on-device.
        /// </summary>
        public async Task<float[][]> GetEmbeddingsAsync(params string[] sentences)
        {
            if (!IsModelReady || _tokenizer == null)
                InitModel();

            // ===== 1. Tokenize =====
            var encodings = sentences.Select(s => _tokenizer!.Encode(s)).ToList();
            int maxLen = encodings.Max(e => e.Length);
            var inputIds = new List<long>();
            var attMask = new List<long>();

            foreach (var ids in encodings)
            {
                int pad = maxLen - ids.Length;
                inputIds.AddRange(ids.Select(i => (long)i));
                inputIds.AddRange(Enumerable.Repeat(0L, pad));
                attMask.AddRange(Enumerable.Repeat(1L, ids.Length));
                attMask.AddRange(Enumerable.Repeat(0L, pad));
            }

            int batch = sentences.Length;
            int seqLen = maxLen;

            var inputIdsTensor = new DenseTensor<long>(inputIds.ToArray(), new[] { batch, seqLen });
            var attMaskTensor = new DenseTensor<long>(attMask.ToArray(), new[] { batch, seqLen });

            // ===== 2. Run =====
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attMaskTensor)
            };

            using var output = await Task.Run(() => _inferenceSession.Run(inputs));

            var lastHiddenValue = output.First();
            var lastHiddenTensor = lastHiddenValue.AsTensor<float>();
            var hiddenData = lastHiddenTensor.ToArray();
            var outputShape = Array.ConvertAll(lastHiddenTensor.Dimensions.ToArray(), d => (long)d);

            var pooled = MeanPooling(hiddenData, attMask.ToArray(), outputShape);
            var normalized = NormalizeAndDivide(pooled, outputShape);

            return Enumerable
                .Chunk(normalized, normalized.Length / sentences.Length)
                .Select(x => x.ToArray())
                .ToArray();
        }


        /// <summary>
        /// Mean pooling for sentence embeddings.
        /// </summary>
        private static float[] MeanPooling(float[] embeddings, long[] attentionMask, long[] shape)
        {
            long batchSize = shape[0];
            int sequenceLength = (int)shape[1];
            int embeddingSize = (int)shape[2];
            float[] result = new float[batchSize * embeddingSize];

            for (int batch = 0; batch < batchSize; batch++)
            {
                Vector<float> sumMask = Vector<float>.Zero;
                Vector<float>[] sumEmbeddings = new Vector<float>[embeddingSize];

                for (int i = 0; i < embeddingSize; i++)
                    sumEmbeddings[i] = Vector<float>.Zero;

                for (int seq = 0; seq < sequenceLength; seq++)
                {
                    long mask = attentionMask[batch * sequenceLength + seq];
                    if (mask == 0)
                        continue;

                    for (int emb = 0; emb < embeddingSize; emb++)
                    {
                        int index = batch * sequenceLength * embeddingSize + seq * embeddingSize + emb;
                        float value = embeddings[index];
                        sumEmbeddings[emb] += new Vector<float>(value);
                    }
                    sumMask += new Vector<float>(1);
                }

                for (int emb = 0; emb < embeddingSize; emb++)
                {
                    float sum = Vector.Dot(sumEmbeddings[emb], Vector<float>.One);
                    float maskSum = Vector.Dot(sumMask, Vector<float>.One);
                    result[batch * embeddingSize + emb] = sum / maskSum;
                }
            }

            return result;
        }

        /// <summary>
        /// Normalize embeddings using L2 norm.
        /// </summary>
        private static float[] NormalizeAndDivide(float[] sentenceEmbeddings, long[] shape)
        {
            long numSentences = shape[0];
            int embeddingSize = (int)shape[2];

            float[] result = new float[sentenceEmbeddings.Length];
            int vectorSize = Vector<float>.Count;

            // Compute Frobenius (L2) norms
            float[] norms = new float[numSentences];

            for (int i = 0; i < numSentences; i++)
            {
                Vector<float> sumSquares = Vector<float>.Zero;
                for (int j = 0; j < embeddingSize; j += vectorSize)
                {
                    int index = i * embeddingSize + j;
                    Vector<float> vec = new Vector<float>(sentenceEmbeddings, index);
                    sumSquares += vec * vec;
                }
                norms[i] = (float)Math.Sqrt(Vector.Dot(sumSquares, Vector<float>.One));
                norms[i] = Math.Max(norms[i], 1e-12f);
            }

            // Normalize and divide
            for (int i = 0; i < numSentences; i++)
            {
                float norm = norms[i];
                for (int j = 0; j < embeddingSize; j += vectorSize)
                {
                    int index = i * embeddingSize + j;
                    Vector<float> vec = new Vector<float>(sentenceEmbeddings, index);
                    Vector<float> normalizedVec = vec / new Vector<float>(norm);
                    normalizedVec.CopyTo(result, index);
                }
            }

            return result;
        }

        public void Dispose()
        {
            _inferenceSession?.Dispose();
        }
    }
}
