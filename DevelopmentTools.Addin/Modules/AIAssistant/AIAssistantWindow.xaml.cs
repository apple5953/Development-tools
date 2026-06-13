using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Modules.AIAssistant
{
    public partial class AIAssistantWindow : Window
    {
        private readonly AIAssistantViewModel _vm;
        private ExternalEvent _selectionEvent;
        private AISelectionHandler _selectionHandler;
        private ExternalEvent _commandEvent;
        private AICommandHandler _commandHandler;

        public AIAssistantWindow(ExternalEvent selectionEvent, AISelectionHandler handler, ExternalEvent commandEvent, AICommandHandler commandHandler)
        {
            InitializeComponent();
            _selectionEvent = selectionEvent;
            _selectionHandler = handler;
            _commandEvent = commandEvent;
            _commandHandler = commandHandler;

            _vm = new AIAssistantViewModel();
            _vm.InjectRevitCommand(commandEvent, commandHandler);
            DataContext = _vm;

            // 每次有新訊息就自動滾到底部
            _vm.Messages.CollectionChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    ChatScrollViewer.ScrollToBottom();
                }));
            };

            _selectionHandler.OnDataReady = (data, count) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _vm.ElementContext = data;
                    _vm.SelectedCount = count;
                });
            };
        }

        private void BtnGetSelection_Click(object sender, RoutedEventArgs e)
        {
            _selectionEvent.Raise();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (_vm.SendMessageCommand.CanExecute(null))
                    _vm.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    // ── ExternalEvent Handler：讀取 Revit 選取元件 ─────────────────────
    public class AISelectionHandler : IExternalEventHandler
    {
        public Action<string, int> OnDataReady { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                if (uiDoc == null) return;

                var doc = uiDoc.Document;
                var ids = uiDoc.Selection.GetElementIds();

                if (ids.Count == 0)
                {
                    OnDataReady?.Invoke("（尚未選取任何元件）", 0);
                    return;
                }

                var sb = new StringBuilder();
                int count = 0;

                foreach (var id in ids)
                {
                    var elem = doc.GetElement(id);
                    if (elem == null) continue;

                    count++;
                    sb.AppendLine($"── 元件 #{count} ──────────────");
                    sb.AppendLine($"類型：{elem.GetType().Name}");
#if REVIT2024 || REVIT2025 || REVIT2026
                    sb.AppendLine($"Id  ：{elem.Id.Value}");
#else
                    sb.AppendLine($"Id  ：{elem.Id.IntegerValue}");
#endif

                    if (elem.Category != null)
                        sb.AppendLine($"類別：{elem.Category.Name}");

                    // Element Name
                    string name = elem.Name;
                    if (!string.IsNullOrEmpty(name))
                        sb.AppendLine($"名稱：{name}");

                    // 讀取常見參數
                    TryAppendParam(sb, elem, "Area",        "面積");
                    TryAppendParam(sb, elem, "Volume",      "體積");
                    TryAppendParam(sb, elem, "Length",      "長度");
                    TryAppendParam(sb, elem, "Level",       "樓層");
                    TryAppendParam(sb, elem, "Base Finish", "地板裝修");
                    TryAppendParam(sb, elem, "Wall Finish", "牆面裝修");
                    TryAppendParam(sb, elem, "Ceiling Finish", "天花板裝修");
                    TryAppendParam(sb, elem, "Tile_Joint_Width", "磁磚縫寬");
                    TryAppendParam(sb, elem, "Number",      "編號");
                    TryAppendParam(sb, elem, "Comments",    "備註");

                    sb.AppendLine();

                    if (count >= 30)
                    {
                        sb.AppendLine($"... 以及其他 {ids.Count - count} 個元件（已截斷顯示）");
                        break;
                    }
                }

                OnDataReady?.Invoke(sb.ToString(), ids.Count);
            }
            catch (Exception ex)
            {
                OnDataReady?.Invoke($"讀取元件時發生錯誤：{ex.Message}", 0);
            }
        }

        private void TryAppendParam(StringBuilder sb, Autodesk.Revit.DB.Element elem, string paramName, string label)
        {
            try
            {
                var param = elem.LookupParameter(paramName);
                if (param == null) return;
                string val = param.AsValueString() ?? param.AsString() ?? "";
                if (!string.IsNullOrWhiteSpace(val))
                    sb.AppendLine($"{label}：{val}");
            }
            catch { }
        }

        public string GetName() => "AIAssistantSelectionHandler";
    }

    // ── BoolToVisibility Converter ─────────────────────────────────────
    public class BoolToVisibility : IValueConverter
    {
        public static readonly BoolToVisibility Instance = new BoolToVisibility(false);
        public static readonly BoolToVisibility InvertedInstance = new BoolToVisibility(true);

        private readonly bool _invert;
        private BoolToVisibility(bool invert) { _invert = invert; }

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool b = value is bool b2 && b2;
            if (_invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    }
}
