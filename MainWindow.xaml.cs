using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly RAGService _ragService;
        private readonly LLMService _llmService;
        private bool _isProcessing = false;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize services
            _ragService = new RAGService();
            _llmService = new LLMService();

            LoadInitialMessage();
            InitializeModelsAsync();
        }

        private async void InitializeModelsAsync()
        {
            await Task.Run(() =>
            {
                _ragService.InitializeModel();
                _llmService.InitializeModel();
            });
            AddClaudeMessage("모델이 준비되었습니다. 무엇을 도와드릴까요?");
        }

        private void LoadInitialMessage()
        {
            AddClaudeMessage("안녕하세요! 무엇을 도와드릴까요?");
        }

        private void AddClaudeMessage(string message)
        {
            // Claude 메시지 블록 생성
            var messageBlock = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                Padding = new Thickness(60, 30, 60, 30),
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var stackPanel = new StackPanel();

            // 아이콘과 타이틀
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var icon = new TextBlock
            {
                Text = "✱",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 122, 90)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            headerPanel.Children.Add(icon);

            stackPanel.Children.Add(headerPanel);

            // 메시지 내용 (TextBox를 읽기 전용으로 사용하여 복사 가능하게)
            var messageText = new TextBox
            {
                Text = message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15),
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Cursor = Cursors.IBeam
            };

            stackPanel.Children.Add(messageText);

            messageBlock.Child = stackPanel;
            ChatMessagesPanel.Children.Add(messageBlock);
        }

        private void AddUserMessage(string message)
        {
            // 사용자 메시지 블록 생성
            var messageBlock = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(250, 250, 249)),
                Padding = new Thickness(60, 30, 60, 30),
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var stackPanel = new StackPanel();

            // 타이틀
            var header = new TextBlock
            {
                Text = "You",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                Margin = new Thickness(0, 0, 0, 10)
            };

            stackPanel.Children.Add(header);

            // 메시지 내용 (TextBox를 읽기 전용으로 사용하여 복사 가능하게)
            var messageText = new TextBox
            {
                Text = message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Cursor = Cursors.IBeam
            };

            stackPanel.Children.Add(messageText);

            messageBlock.Child = stackPanel;
            ChatMessagesPanel.Children.Add(messageBlock);
        }

        private Border CreateActionButton(string content)
        {
            var button = new TextBlock
            {
                Text = content,
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand
            };

            var border = new Border
            {
                Child = button,
                Padding = new Thickness(5),
                Background = Brushes.Transparent
            };

            return border;
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void MessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                SendMessage();
            }
        }

        private void MessageInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (MessageInput.Text == "답글...")
            {
                MessageInput.Text = "";
                MessageInput.Foreground = new SolidColorBrush(Color.FromRgb(26, 26, 26));
            }
        }

        private void MessageInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageInput.Text))
            {
                MessageInput.Text = "답글...";
                MessageInput.Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153));
            }
        }

        private async void SendMessage()
        {
            string message = MessageInput.Text?.Trim();

            if (string.IsNullOrEmpty(message) || message == "답글..." || _isProcessing)
                return;

            _isProcessing = true;

            // 사용자 메시지 추가
            AddUserMessage(message);

            MessageInput.Text = "답글...";
            MessageInput.Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153));

            // 로딩 메시지 표시
            var loadingBorder = AddLoadingMessage();

            try
            {
                // 선택된 카테고리 가져오기
                string? selectedCategory = null;
                if (CategoryComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    selectedCategory = selectedItem.Tag?.ToString();
                    if (string.IsNullOrEmpty(selectedCategory))
                    {
                        selectedCategory = null; // "전체" 선택 시 null로 처리
                    }
                }

                // 1. 데이터베이스에서 검색 (category 필터 적용)
                var searchResults = await _ragService.SearchDocumentsAsync(message, top: 1, category: selectedCategory);

                // 로딩 메시지 제거
                ChatMessagesPanel.Children.Remove(loadingBorder);

                // 2. 스트리밍 메시지 블록 생성 (빈 상태로 시작)
                var streamingMessageBlock = CreateStreamingMessageBlock();
                var messageTextBlock = streamingMessageBlock.Item1;
                var messageContainer = streamingMessageBlock.Item2;
                var fullResponse = new StringBuilder();

                if (searchResults.Count == 0)
                {
                    // 검색 결과가 없으면 LLM이 직접 답변
                    await _llmService.GenerateStreamingResponseAsync(
                        message,
                        token =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                fullResponse.Append(token);
                                var text = fullResponse.ToString();

                                // 종료 토큰 제거
                                text = text.Replace("<|im_end|>", "")
                                          .Replace("<|endoftext|>", "")
                                          .Replace("</s>", "");

                                messageTextBlock.Text = text;
                            });
                        });
                }
                else
                {
                    // 2. 검색 결과를 컨텍스트로 변환
                    var context = BuildContext(searchResults);

                    // 3. LLM에 컨텍스트와 함께 질문 전달 (스트리밍)
                    await _llmService.GenerateStreamingResponseAsync(
                        message,
                        token =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                fullResponse.Append(token);
                                var text = fullResponse.ToString();

                                // 종료 토큰 제거
                                text = text.Replace("<|im_end|>", "")
                                          .Replace("<|endoftext|>", "")
                                          .Replace("</s>", "");

                                messageTextBlock.Text = text;
                            });
                        },
                        context);

                    // 4. 스트리밍 완료 후 출처 정보 추가
                    Dispatcher.Invoke(() =>
                    {
                        var cleanResponse = fullResponse.ToString()
                            .Replace("<|im_end|>", "")
                            .Replace("<|endoftext|>", "")
                            .Replace("</s>", "");

                        var finalResponse = FormatResponseWithSources(cleanResponse, searchResults);
                        messageTextBlock.Text = finalResponse;
                    });
                }
            }
            catch (Exception ex)
            {
                // 로딩 메시지 제거
                ChatMessagesPanel.Children.Remove(loadingBorder);

                AddClaudeMessage($"오류가 발생했습니다: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private Border AddLoadingMessage(string message = "검색 중...")
        {
            var loadingBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                Padding = new Thickness(60, 30, 60, 30),
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var stackPanel = new StackPanel();

            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var icon = new TextBlock
            {
                Text = "✱",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 122, 90)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            headerPanel.Children.Add(icon);
            stackPanel.Children.Add(headerPanel);

            var loadingText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                FontStyle = FontStyles.Italic
            };

            stackPanel.Children.Add(loadingText);
            loadingBorder.Child = stackPanel;
            ChatMessagesPanel.Children.Add(loadingBorder);

            return loadingBorder;
        }

        /// <summary>
        /// Creates a message block for streaming responses.
        /// Returns a tuple of (TextBox for message content, Border container).
        /// </summary>
        private (TextBox, Border) CreateStreamingMessageBlock()
        {
            var messageBlock = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                Padding = new Thickness(60, 30, 60, 30),
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var stackPanel = new StackPanel();

            // 아이콘과 타이틀
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var icon = new TextBlock
            {
                Text = "✱",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 122, 90)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            headerPanel.Children.Add(icon);
            stackPanel.Children.Add(headerPanel);

            // 메시지 내용 (스트리밍으로 업데이트될 TextBox - 복사 가능)
            var messageText = new TextBox
            {
                Text = "",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15),
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Cursor = Cursors.IBeam
            };

            stackPanel.Children.Add(messageText);

            // 하단 액션 버튼들
            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var copyButton = CreateActionButton("📋");
            var likeButton = CreateActionButton("👍");
            var dislikeButton = CreateActionButton("👎");
            var retryButton = new TextBlock
            {
                Text = "재시도",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };

            actionPanel.Children.Add(copyButton);
            actionPanel.Children.Add(likeButton);
            actionPanel.Children.Add(dislikeButton);
            actionPanel.Children.Add(retryButton);

            stackPanel.Children.Add(actionPanel);

            messageBlock.Child = stackPanel;
            ChatMessagesPanel.Children.Add(messageBlock);

            return (messageText, messageBlock);
        }

        /// <summary>
        /// 검색 결과를 LLM 컨텍스트로 변환
        /// </summary>
        private string BuildContext(List<RAGService.DocumentSearchResult> results)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                sb.AppendLine($"[문서 {i + 1}]");
                sb.AppendLine(result.Text);

                if (i < results.Count - 1)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// LLM 답변과 출처 문서를 함께 포맷팅
        /// </summary>
        private string FormatResponseWithSources(string llmResponse, List<RAGService.DocumentSearchResult> sources)
        {
            var sb = new StringBuilder();

            // LLM 답변
            sb.AppendLine(llmResponse.Trim());
            sb.AppendLine();
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();
            sb.AppendLine($"📚 참고 문서 ({sources.Count}건):");
            sb.AppendLine();

            // 출처 문서
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                sb.AppendLine($"【{i + 1}】 {source.Text.Substring(0, Math.Min(100, source.Text.Length))}...");

                if (source.Score.HasValue)
                {
                    sb.AppendLine($"   유사도: {source.Score.Value:F4}");
                }

                // 원문 텍스트 표시 (전체 텍스트 또는 일부)
                sb.AppendLine($"   출처: {source.Text}");

                if (i < sources.Count - 1)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }
}