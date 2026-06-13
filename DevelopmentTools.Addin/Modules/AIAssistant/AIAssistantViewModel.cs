using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Modules.AIAssistant
{
    public class ChatMessage : INotifyPropertyChanged
    {
        public string Role { get; set; }   // "user" | "assistant" | "system"
        public string Content { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsUser => Role == "user";
        public bool IsAssistant => Role == "assistant";
        public bool IsSystem => Role == "system";

        // AI Revit 指令
        public string RawCommandJson { get; set; }
        public bool HasCommand => !string.IsNullOrEmpty(RawCommandJson);

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AIAssistantViewModel : INotifyPropertyChanged
    {
        private ExternalEvent _commandEvent;
        private AICommandHandler _commandHandler;
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // ── API 設定 ───────────────────────────────────────────────────
        private AIApiSettings _settings;

        public string ApiKey
        {
            get => _settings.ApiKey;
            set { _settings.ApiKey = value; OnPropertyChanged(); }
        }

        public string ApiEndpoint
        {
            get => _settings.ApiEndpoint;
            set { _settings.ApiEndpoint = value; OnPropertyChanged(); }
        }

        public string ModelName
        {
            get => _settings.ModelName;
            set { _settings.ModelName = value; OnPropertyChanged(); }
        }

        // ── UI 狀態 ───────────────────────────────────────────────────────
        private bool _isApiReady = false;
        public bool IsApiReady
        {
            get => _isApiReady;
            set
            {
                _isApiReady = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsApiNotReady));
                OnPropertyChanged(nameof(StatusText));
            }
        }
        public bool IsApiNotReady => !IsApiReady;
        public string StatusText => IsApiReady ? "✅ AI 助理已就緒" : "🚧  API 未連線，請點擊「API 設定」輸入金鑰連線";

        private bool _isConfigVisible = true;
        public bool IsConfigVisible
        {
            get => _isConfigVisible;
            set { _isConfigVisible = value; OnPropertyChanged(); }
        }

        private string _elementContext = "";
        public string ElementContext
        {
            get => _elementContext;
            set { _elementContext = value; OnPropertyChanged(); }
        }

        public bool HasElementContext => !string.IsNullOrWhiteSpace(ElementContext);

        private int _selectedCount = 0;
        public int SelectedCount
        {
            get => _selectedCount;
            set { _selectedCount = value; OnPropertyChanged(); }
        }

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

        // ── 指令 ───────────────────────────────────────────────────────
        public ICommand SendMessageCommand { get; }
        public ICommand ClearChatCommand { get; }
        public ICommand ConnectApiCommand { get; }
        public ICommand ToggleConfigCommand { get; }
        public ICommand SubmitAnalysisCommand { get; }
        public ICommand ExecuteRevitCommand { get; }

        public AIAssistantViewModel()
        {
            // 載入本地設定
            _settings = AIApiSettings.Load();
            IsApiReady = _settings.IsApiReady;
            IsConfigVisible = !IsApiReady; // 如果已經是 Ready 狀態就預設隱藏

            SendMessageCommand = new RelayCommand(ExecuteSendMessage, _ => IsApiReady && !IsLoading && !string.IsNullOrWhiteSpace(UserInput));
            ClearChatCommand = new RelayCommand(_ => Messages.Clear());
            ConnectApiCommand = new RelayCommand(ExecuteConnectApi, _ => !IsLoading);
            ToggleConfigCommand = new RelayCommand(_ => IsConfigVisible = !IsConfigVisible);
            SubmitAnalysisCommand = new RelayCommand(ExecuteSubmitAnalysis, _ => IsApiReady && !IsLoading && HasElementContext);
            ExecuteRevitCommand = new RelayCommand(ExecuteRevitAction, _ => _commandEvent != null && _commandHandler != null);

            Messages.Add(new ChatMessage
            {
                Role = "system",
                Content = "👋  歡迎使用 AI 模型分析助手！\n\n1. 請在右上角「⚙ API 設定」中，輸入自訂 API Key 與端點後進行確認連線。\n2. 在 Revit 中選取元件，點擊左下方「取得 Revit 選取元件」取得模型 Context。\n3. 您可透過「送出分析」或直接以自然語言詢問 AI 元件的屬性狀況，甚至發送指令讓 AI 自動執行參數修改！",
                Timestamp = DateTime.Now
            });
        }

        public void InjectRevitCommand(ExternalEvent commandEvent, AICommandHandler commandHandler)
        {
            _commandEvent = commandEvent;
            _commandHandler = commandHandler;
            if (_commandHandler != null)
            {
                _commandHandler.OnExecutionCompleted = (msg) =>
                {
                    App.Log($"[AICommandHandler] {msg}");
                    // 在對話中插入 system 回應
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Messages.Add(new ChatMessage { Role = "system", Content = msg });
                    });
                };
            }
        }

        private async void ExecuteConnectApi(object _)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                TaskDialog.Show("AI 助手", "請輸入 API Key 後再進行連線。");
                return;
            }

            IsLoading = true;
            Messages.Add(new ChatMessage { Role = "system", Content = "⏳ 正在與自訂 AI 伺服器進行握手測試..." });

            try
            {
                bool success = await TestConnectionAsync();
                if (success)
                {
                    IsApiReady = true;
                    _settings.IsApiReady = true;
                    _settings.Save(); // 儲存加密設定到本地
                    IsConfigVisible = false; // 連線成功自動收合
                    Messages.Add(new ChatMessage { Role = "system", Content = "✅ 連線測試成功！API 已準備就緒，您可以開始與模型對話了。" });
                }
                else
                {
                    IsApiReady = false;
                    Messages.Add(new ChatMessage { Role = "system", Content = "❌ 連線失敗。請確認 Endpoint 網址與 API Key 是否正確且支援跨網域連線。" });
                }
            }
            catch (Exception ex)
            {
                IsApiReady = false;
                Messages.Add(new ChatMessage { Role = "system", Content = $"❌ 連線錯誤：{ex.Message}" });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<bool> TestConnectionAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
            if (!string.IsNullOrEmpty(ApiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            }

            // 發送極簡 Ping 請求
            var payload = new
            {
                model = ModelName,
                messages = new[] { new { role = "user", content = "ping" } },
                max_tokens = 5
            };

            string json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        private void ExecuteSubmitAnalysis(object _)
        {
            if (!HasElementContext) return;
            UserInput = "請幫我分析目前選取的元件，檢查其參數是否有不合理之處或提出優化建議。";
            ExecuteSendMessage(null);
        }

        private async void ExecuteSendMessage(object _)
        {
            if (string.IsNullOrWhiteSpace(UserInput)) return;

            string rawInput = UserInput;
            var userMsg = new ChatMessage { Role = "user", Content = rawInput };
            Messages.Add(userMsg);
            UserInput = "";
            IsLoading = true;

            try
            {
                var assistantReply = await CallLlmApiAsync(rawInput);
                
                // 解析 Revit 指令
                var parsedMsg = ParseRevitCommand(assistantReply);
                Messages.Add(parsedMsg);
            }
            catch (Exception ex)
            {
                Messages.Add(new ChatMessage
                {
                    Role = "system",
                    Content = $"❌ 發生對話錯誤：{ex.Message}",
                    Timestamp = DateTime.Now
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<string> CallLlmApiAsync(string userMsgContent)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
            if (!string.IsNullOrEmpty(ApiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            }

            // 建立 System Prompt
            string systemPrompt = "你是一個專業的 BIM 專家與 Revit 二次開發助手。\n" +
                                 "現在使用者選取了 Revit 專案中的一些元件，元件屬性資料會以「Context (元件資料)」格式提供。\n" +
                                 "你可以協助使用者進行統計、裝修衝突檢查、幾何排磚起點建議。\n" +
                                 "如果你認為需要修改 Revit 中的元件參數、選取元件或刪除元件，請務必在你的回覆末尾，以下列 JSON 格式包含一條或多條指令（以 [REVIT_COMMAND] 與 [/REVIT_COMMAND] 括起來）：\n" +
                                 "[REVIT_COMMAND]\n" +
                                 "{\n" +
                                 "  \"action\": \"set_parameter\",\n" +
                                 "  \"element_ids\": [12345, 67890],\n" +
                                 "  \"parameter_name\": \"Comments\",\n" +
                                 "  \"value\": \"你的修改值\"\n" +
                                 "}\n" +
                                 "[/REVIT_COMMAND]\n\n" +
                                 "支援的 action 種類：\n" +
                                 "1. \"set_parameter\"：修改元件參數。必須提供 parameter_name (如 \"Comments\", \"Base Finish\", \"Tile_Joint_Width\" 等) 與 value (新值)。若為長度參數（例如縫隙、高度、寬度），請務必以 mm 為單位填寫純數字，外掛會自動轉換為 Revit 的內部英呎單位。\n" +
                                 "2. \"select_elements\"：在 Revit 視圖中選取這些元件。必須提供 element_ids。\n" +
                                 "3. \"delete_elements\"：在 Revit 中刪除這些元件。必須提供 element_ids。\n\n" +
                                 "請提供專業、清晰的中文分析。";

            // 收集近 10 條對話歷史
            var apiMessages = new List<object>();
            apiMessages.Add(new { role = "system", content = systemPrompt });

            // 注入 Element Context 作為第一條使用者提示
            if (HasElementContext)
            {
                apiMessages.Add(new { role = "user", content = $"[Context (元件資料)]\n{ElementContext}" });
                apiMessages.Add(new { role = "assistant", content = "我已收到您選取的 Revit 元件資料 Context，我會將其作為分析背景基礎。" });
            }

            // 注入歷史消息
            int startIdx = Math.Max(0, Messages.Count - 10);
            for (int i = startIdx; i < Messages.Count; i++)
            {
                var msg = Messages[i];
                if (msg.IsSystem) continue;
                string roleName = msg.Role;
                // 如果有指令，在發送歷史時把 RawCommand 包含回去，讓 AI 保持上下文狀態
                string text = msg.Content;
                if (msg.HasCommand)
                {
                    text += $"\n[REVIT_COMMAND]\n{msg.RawCommandJson}\n[/REVIT_COMMAND]";
                }
                apiMessages.Add(new { role = roleName, content = text });
            }

            // 注入當前對話
            apiMessages.Add(new { role = "user", content = userMsgContent });

            var payload = new
            {
                model = ModelName,
                messages = apiMessages,
                temperature = 0.5
            };

            string json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                string errContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"HTTP {response.StatusCode}: {errContent}");
            }

            string replyJson = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(replyJson))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                    {
                        return content.GetString();
                    }
                }
            }

            throw new Exception("無法從 API 回應中解析出回覆內容。");
        }

        private ChatMessage ParseRevitCommand(string replyText)
        {
            var msg = new ChatMessage
            {
                Role = "assistant",
                Timestamp = DateTime.Now
            };

            // 使用 Regular Expression 提取 [REVIT_COMMAND] 標籤
            var regex = new Regex(@"\[REVIT_COMMAND\](.*?)\[/REVIT_COMMAND\]", RegexOptions.Singleline);
            var match = regex.Match(replyText);

            if (match.Success)
            {
                msg.RawCommandJson = match.Groups[1].Value.Trim();
                // 從顯示文字中剔除指令標籤
                msg.Content = regex.Replace(replyText, "").Trim();
            }
            else
            {
                msg.Content = replyText;
            }

            return msg;
        }

        private void ExecuteRevitAction(object parameter)
        {
            if (_commandEvent == null || _commandHandler == null) return;
            if (parameter is ChatMessage msg && msg.HasCommand)
            {
                _commandHandler.CommandJson = msg.RawCommandJson;
                _commandEvent.Raise();
                App.Log($"[AIAssistantViewModel] Raised Revit command execution: {msg.RawCommandJson}");
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
