using Microsoft.ML.OnnxRuntimeGenAI;
using System.IO;
using System.Text;

namespace WpfApp1
{
    public class LLMService : IDisposable
    {
        private readonly string _modelPath;
        private Model? _model;
        private Tokenizer? _tokenizer;
        private bool _isInitialized = false;

        public LLMService(string? modelPath = null)
        {
            modelPath ??= Path.Combine("model", "kanana_cpu");
            _modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelPath);
        }

        /// <summary>
        /// Initialize the LLM model.
        /// </summary>
        public void InitializeModel()
        {
            if (_isInitialized)
                return;

            try
            {
                _model = new Model(_modelPath);
                _tokenizer = new Tokenizer(_model);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"LLM 모델 초기화 실패. 모델 경로를 확인하세요: {_modelPath}\n" +
                    $"오류: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generate a streaming response with callback for each token.
        /// </summary>
        public async Task GenerateStreamingResponseAsync(
            string userPrompt,
            Action<string> onTokenReceived,
            string? context = null,
            int maxLength = 5000)
        {
            if (!_isInitialized || _model == null || _tokenizer == null)
            {
                InitializeModel();
            }

            // Build prompt with context if provided
            string fullPrompt;
            if (!string.IsNullOrEmpty(context))
            {
                fullPrompt = $"<|im_start|>system\nPlease refer to the following document and answer the user's question.:\n\n{context} \n\nuser\n{userPrompt} \n\nassistant\n";
            }
            else
            {
                fullPrompt = $"<|im_start|>user\n{userPrompt}<|im_end|>\n<|im_start|>assistant\n";
            }

            await Task.Run(() =>
            {
                var sequences = _tokenizer!.Encode(fullPrompt);

                using var generatorParams = new GeneratorParams(_model!);
                generatorParams.SetSearchOption("max_length", maxLength);

                using var generator = new Generator(_model!, generatorParams);
                generator.AppendTokenSequences(sequences);

                using var tokenizerStream = _tokenizer.CreateStream();
                var fullText = new StringBuilder();

                while (!generator.IsDone())
                {
                    generator.GenerateNextToken();
                    var token = generator.GetSequence(0)[^1];
                    var decodedToken = tokenizerStream.Decode(token);

                    fullText.Append(decodedToken);

                    onTokenReceived(decodedToken);

                    var currentText = fullText.ToString();
                    if (currentText.Contains("<|im_end|>") ||
                        currentText.Contains("<|endoftext|>") ||
                        currentText.Contains("|im_end|>") ||
                        currentText.Contains("|im_end|") ||
                        currentText.Contains("</s>"))
                    {
                        break;
                    }
                }
            });
        }

        public void Dispose()
        {
            _tokenizer?.Dispose();
            _model?.Dispose();
        }
    }
}
