using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly RAGService _ragService;
        private bool _isProcessing = false;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize RAGService
            _ragService = new RAGService();

            LoadInitialMessage();
            InitializeModelAsync();
        }

        private async void InitializeModelAsync()
        {
            await Task.Run(() => _ragService.InitializeModel());
            AddClaudeMessage("모델이 준비되었습니다. 무엇을 검색하시겠습니까?");
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

            // 메시지 내용
            var messageText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 15)
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

            // 메시지 내용
            var messageText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22
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
                // 데이터베이스에서 검색
                var searchResults = await _ragService.SearchDocumentsAsync(message, top: 5);

                // 로딩 메시지 제거
                ChatMessagesPanel.Children.Remove(loadingBorder);

                if (searchResults.Count == 0)
                {
                    AddClaudeMessage("검색 결과가 없습니다. 다른 검색어를 시도해주세요.");
                }
                else
                {
                    // 검색 결과 포맷팅
                    var resultMessage = FormatSearchResults(searchResults);
                    AddClaudeMessage(resultMessage);
                }
            }
            catch (Exception ex)
            {
                // 로딩 메시지 제거
                ChatMessagesPanel.Children.Remove(loadingBorder);

                AddClaudeMessage($"검색 중 오류가 발생했습니다: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private Border AddLoadingMessage()
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
                Text = "검색 중...",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                FontStyle = FontStyles.Italic
            };

            stackPanel.Children.Add(loadingText);
            loadingBorder.Child = stackPanel;
            ChatMessagesPanel.Children.Add(loadingBorder);

            return loadingBorder;
        }

        private string FormatSearchResults(List<RAGService.DocumentSearchResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"검색 결과 {results.Count}건을 찾았습니다:\n");

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                sb.AppendLine($"【{i + 1}】");
                sb.AppendLine(result.Text);

                if (result.Score.HasValue)
                {
                    sb.AppendLine($"(유사도: {result.Score.Value:F4})");
                }

                var metadata = result.GetMetadataDict();
                if (metadata.Count > 0)
                {
                    sb.Append("메타데이터: ");
                    sb.AppendLine(string.Join(", ", metadata.Select(kv => $"{kv.Key}: {kv.Value}")));
                }

                if (i < results.Count - 1)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }
}