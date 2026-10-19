using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace BIMDrawingFactory.UI.Revit
{
    internal sealed class NativeTagDialogRequest
    {
        public string CategoryName { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public bool IncludeLinkedElements { get; set; }
    }

    internal sealed class NativeTagDialogResult
    {
        public bool Confirmed { get; set; }
        public string Message { get; set; }
    }

    internal static class NativeTagAllDialogAutomation
    {
        public static Task<NativeTagDialogResult> ConfirmAsync(NativeTagDialogRequest request,
            CancellationToken cancellationToken)
        {
            return Task.Run(() => ConfirmCore(request, cancellationToken), cancellationToken);
        }

        private static NativeTagDialogResult ConfirmCore(NativeTagDialogRequest request,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(4);
            int processId = Process.GetCurrentProcess().Id;
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var dialog = FindTagAllDialog(processId, request);
                    if (dialog != null)
                    {
                        string error;
                        if (!ConfigureDialog(dialog, request, out error))
                            return new NativeTagDialogResult { Confirmed = false, Message = error };
                        return new NativeTagDialogResult { Confirmed = true, Message = "Revit 內建全部加上標籤已自動確認" };
                    }
                }
                catch
                {
                    // The Revit window may be rebuilding its automation tree.
                }
                Thread.Sleep(120);
            }
            // A cancelled/expired fast attempt must never leave a late Revit
            // modal dialog blocking the next ExternalEvent.
            if (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var lateDialog = FindTagAllDialog(processId, request);
                    if (lateDialog != null) DismissWithCancel(lateDialog);
                }
                catch { }
            }
            return new NativeTagDialogResult
            {
                Confirmed = false,
                Message = cancellationToken.IsCancellationRequested
                    ? "內建標籤命令已取消"
                    : "4 秒內未偵測到 Revit「全部加上標籤」對話框"
            };
        }

        private static AutomationElement FindTagAllDialog(int processId, NativeTagDialogRequest request)
        {
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId));
            foreach (AutomationElement window in windows)
            {
                string name = SafeName(window);
                bool titleMatch = ContainsAny(name, "Tag All Not Tagged", "Tag All", "全部加上標籤",
                    "標註所有未標記", "標記所有未標記");
                bool isModal = false;
                object windowPattern;
                if (window.TryGetCurrentPattern(WindowPattern.Pattern, out windowPattern))
                    isModal = ((WindowPattern)windowPattern).Current.IsModal;
                if (!titleMatch && isModal)
                {
                    var categoryTokens = GetCategoryTokens(request.CategoryName);
                    titleMatch = window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                        .Cast<AutomationElement>()
                        .Any(e => categoryTokens.Any(token => SafeName(e)
                            .IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
                }
                if (titleMatch && FindOkButton(window) != null) return window;
            }
            return null;
        }

        private static bool ConfigureDialog(AutomationElement dialog, NativeTagDialogRequest request,
            out string error)
        {
            error = null;
            var categoryTokens = GetCategoryTokens(request.CategoryName);
            var checkBoxes = dialog.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox))
                .Cast<AutomationElement>().ToList();

            AutomationElement categoryCheckBox = checkBoxes.FirstOrDefault(c =>
                categoryTokens.Any(token => SafeName(c).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));

            if (categoryCheckBox == null)
            {
                var matchingRow = dialog.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                    .Cast<AutomationElement>()
                    .FirstOrDefault(e => categoryTokens.Any(token =>
                        SafeName(e).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
                if (matchingRow != null)
                    categoryCheckBox = matchingRow.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));
            }

            if (categoryCheckBox == null)
            {
                error = "已打開內建標籤對話框，但找不到指定品類：" + request.CategoryName;
                DismissWithCancel(dialog);
                return false;
            }

            SetToggle(categoryCheckBox, true);

            foreach (var checkBox in checkBoxes)
            {
                string name = SafeName(checkBox);
                if (checkBox.Equals(categoryCheckBox))
                    SetToggle(checkBox, true);
                else if (ContainsAny(name, "linked", "link", "連結", "鏈結"))
                    SetToggle(checkBox, request.IncludeLinkedElements);
                else if (ContainsAny(name, "leader", "引線"))
                    SetToggle(checkBox, false);
                else
                    SetToggle(checkBox, false);
            }

            var allObjects = dialog.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton))
                .Cast<AutomationElement>()
                .FirstOrDefault(r => ContainsAny(SafeName(r), "All objects", "current view", "目前視圖中的所有", "當前視圖中的所有"));
            if (allObjects != null)
            {
                object pattern;
                if (allObjects.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                    ((SelectionItemPattern)pattern).Select();
            }

            var ok = FindOkButton(dialog);
            if (ok == null)
            {
                error = "內建標籤對話框沒有可用的確定按鈕";
                DismissWithCancel(dialog);
                return false;
            }
            object invokePattern;
            if (!ok.TryGetCurrentPattern(InvokePattern.Pattern, out invokePattern))
            {
                error = "無法自動按下內建標籤對話框的確定";
                DismissWithCancel(dialog);
                return false;
            }
            ((InvokePattern)invokePattern).Invoke();
            return true;
        }

        private static void DismissWithCancel(AutomationElement dialog)
        {
            try
            {
                var cancel = dialog.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                    .Cast<AutomationElement>()
                    .FirstOrDefault(b => ContainsAny(SafeName(b), "Cancel", "取消"));
                object pattern;
                if (cancel != null && cancel.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                    ((InvokePattern)pattern).Invoke();
            }
            catch { }
        }

        private static AutomationElement FindOkButton(AutomationElement dialog)
        {
            return dialog.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                .Cast<AutomationElement>()
                .FirstOrDefault(b => ContainsAny(SafeName(b), "OK", "確定", "好"));
        }

        private static void SetToggle(AutomationElement element, bool desired)
        {
            object pattern;
            if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out pattern)) return;
            var toggle = (TogglePattern)pattern;
            bool isOn = toggle.Current.ToggleState == ToggleState.On;
            if (isOn != desired) toggle.Toggle();
        }

        private static string[] GetCategoryTokens(string categoryName)
        {
            if (string.Equals(categoryName, "Floors", StringComparison.OrdinalIgnoreCase))
                return new[] { "Floor", "Floor Tag", "樓板", "版標籤" };
            if (string.Equals(categoryName, "Walls", StringComparison.OrdinalIgnoreCase))
                return new[] { "Wall", "Wall Tag", "牆", "牆標籤" };
            if (string.Equals(categoryName, "Doors", StringComparison.OrdinalIgnoreCase))
                return new[] { "Door", "Door Tag", "門", "門標籤" };
            return new[] { categoryName ?? string.Empty };
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            return tokens.Any(t => !string.IsNullOrWhiteSpace(t) &&
                (value ?? string.Empty).IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string SafeName(AutomationElement element)
        {
            try { return element != null ? element.Current.Name ?? string.Empty : string.Empty; }
            catch { return string.Empty; }
        }
    }
}
