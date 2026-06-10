using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace DevelopmentTools.Modules.AIAssistant
{
    public class ChatMessage
    {
        public string Role { get; set; }   // "user" | "assistant" | "system"
        public string Content { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsUser => Role == "user";
        public bool IsAssistant => Role == "assistant";
        public bool IsSystem => Role == "system";
    }

    public class AIAssistantViewModel : INotifyPropertyChanged
    {
        // ── 狀態 ───────────────────────────────────────────────────────
        private bool _isApiReady = false;
        public bool IsApiReady
        {
            get => _isApiReady;
            set { _isApiReady = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsApiNotReady)); OnPropertyChanged(nameof(StatusText)); }
        }
        public bool IsApiNotReady => !_isApiReady;
        public string StatusText => _isApiReady ? "✅ API 已連線" : "🚧  AI 引擎建置中，API 串接後即可使用";

        // ── 元件資料 ───────────────────────────────────────────────────
        private string _elementContext = "";
        public string ElementContext
        {
            get => _elementContext;
            set { _elementContext = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasElementContext)); }
        }
        public bool HasElementContext => !string.IsNullOrWhiteSpace(_elementContext);

        private int _selectedCount = 0;
        public int SelectedCount
        {
            get => _selectedCount;
            set { _selectedCount = value; OnPropertyChanged(); }
        }

        // ── 對話 ───────────────────────────────────────────────────────
        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();

        private string _userInput = "";
        public string UserInput
        {
            get => _userInput;
            set { _userInput = value; OnPropertyChanged(); }
        }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        // ── API 設定（預留） ────────────────────────────────────────────
        private string _apiKey = "";
        public string ApiKey
        {
            get => _apiKey;
            set { _apiKey = value; OnPropertyChanged(); }
        }

        private string _apiEndpoint = "https://api.openai.com/v1/chat/completions";
        public string ApiEndpoint
        {
            get => _apiEndpoint;
            set { _apiEndpoint = value; OnPropertyChanged(); }
        }

        private string _modelName = "gpt-4o";
        public string ModelName
        {
            get => _modelName;
            set { _modelName = value; OnPropertyChanged(); }
        }

        // ── 指令 ───────────────────────────────────────────────────────
        public ICommand SendMessageCommand { get; }
        public ICommand ClearChatCommand { get; }

        public AIAssistantViewModel()
        {
            SendMessageCommand = new RelayCommand(ExecuteSendMessage, CanSendMessage);
            ClearChatCommand = new RelayCommand(_ => Messages.Clear());

            Messages.Add(new ChatMessage
            {
                Role = "system",
                Content = "👋  歡迎使用 AI 模型分析助手！\n\n請先在 Revit 中選取元件，點擊「取得選取元件」後，AI 將自動分析並提供優化建議。\n\n⚠️  目前 AI 引擎尚在建置中，完成 API 串接後即可啟用。",
                Timestamp = DateTime.Now
            });
        }

        private bool CanSendMessage(object _) => IsApiReady && !IsLoading && !string.IsNullOrWhiteSpace(UserInput);

        private async void ExecuteSendMessage(object _)
        {
            if (string.IsNullOrWhiteSpace(UserInput)) return;

            var userMsg = new ChatMessage { Role = "user", Content = UserInput };
            Messages.Add(userMsg);
            UserInput = "";
            IsLoading = true;

            try
            {
                // TODO: 串接實際 API
                // var response = await CallLlmApiAsync(userMsg.Content);
                await System.Threading.Tasks.Task.Delay(500); // placeholder
                Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = "🚧  API 尚未串接，此為預留回應位置。",
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                Messages.Add(new ChatMessage
                {
                    Role = "system",
                    Content = $"❌  發生錯誤：{ex.Message}",
                    Timestamp = DateTime.Now
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ── INotifyPropertyChanged ─────────────────────────────────────
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── 簡易 RelayCommand ──────────────────────────────────────────────
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add => System.Windows.Input.CommandManager.RequerySuggested += value;
            remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
        }
    }
}
