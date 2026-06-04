using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DevelopmentTools.Modules.SheetTools.SheetViewPlacer
{
    /// <summary>
    /// SheetViewPlacerWindow.xaml 的互動邏輯
    /// </summary>
    public partial class SheetViewPlacerWindow : Window
    {
        private readonly SheetViewPlacerViewModel _viewModel;
        
        // 拖曳狀態變數
        private System.Windows.Point _startPoint;
        private TreeItemViewModel _draggedItem;

        // 畫布拖曳狀態變數
        private bool _isDraggingInCanvas = false;
        private System.Windows.Point _dragStartMousePos;
        private double _dragStartCanvasX;
        private double _dragStartCanvasY;
        private PlacedViewItemViewModel _draggedCanvasItem;

        public SheetViewPlacerWindow(Autodesk.Revit.DB.Document doc)
        {
            InitializeComponent();
            _viewModel = new SheetViewPlacerViewModel(doc, this);
            this.DataContext = _viewModel;
        }

        #region TreeView 拖曳 (Drag & Drop) 事件處理

        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem tvi && tvi.Header is TreeItemViewModel item)
            {
                // 僅允許拖曳 View 與 Schedule 節點
                if (item.Type == "View" || item.Type == "Schedule")
                {
                    _startPoint = e.GetPosition(null);
                    _draggedItem = item;
                }
                else
                {
                    _draggedItem = null;
                }
            }
        }

        private void TreeViewItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null)
            {
                System.Windows.Point currentPos = e.GetPosition(null);
                Vector diff = _startPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // 執行拖曳
                    DragDrop.DoDragDrop(this.SheetTreeView, _draggedItem, DragDropEffects.Move);
                    _draggedItem = null;
                }
            }
        }

        private void TreeViewItem_DragOver(object sender, DragEventArgs e)
        {
            TreeItemViewModel target = GetItemFromEvent(e);
            
            // 僅允許將視圖拖曳放置於 "Sheet" (圖紙) 節點上
            if (target != null && target.Type == "Sheet")
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private void TreeViewItem_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;

            TreeItemViewModel targetSheet = GetItemFromEvent(e);
            if (targetSheet == null || targetSheet.Type != "Sheet")
            {
                return;
            }

            var draggedData = e.Data.GetData(typeof(TreeItemViewModel)) as TreeItemViewModel;
            if (draggedData == null)
            {
                return;
            }

            // 判斷來源並執行相對應的寫入操作
            bool success = false;
            string actionMsg = "";

            if (draggedData.Parent != null && draggedData.Parent.Type == "Sheet")
            {
                // 跨圖紙調整：從 A 圖紙移動到 B 圖紙
                if (draggedData.Parent.Id == targetSheet.Id)
                {
                    StatusText.Text = "狀態：視圖已在目標圖紙上。";
                    return;
                }

                StatusText.Text = $"狀態：正在將視圖移轉至圖紙 {targetSheet.Name}...";
                success = _viewModel.MoveViewBetweenSheets(draggedData.Id, draggedData.Parent.Id, targetSheet.Id);
                actionMsg = $"✓ 成功將視圖「{draggedData.Name}」移轉至圖紙「{targetSheet.Name}」";
            }
            else
            {
                // 未放置視圖置入：拖放到圖紙
                StatusText.Text = $"狀態：正在將視圖放置至圖紙 {targetSheet.Name}...";
                success = _viewModel.PlaceViewOnSheet(draggedData.Id, targetSheet.Id);
                actionMsg = $"✓ 成功將視圖「{draggedData.Name}」放置於圖紙「{targetSheet.Name}」";
            }

            if (success)
            {
                _viewModel.LoadData();
                StatusText.Text = $"狀態：{actionMsg}";
            }
            else
            {
                StatusText.Text = "狀態：操作失敗。";
            }
        }

        // 當直接在 TreeView 空白區域 DragOver/Drop 時
        private void SheetTreeView_DragOver(object sender, DragEventArgs e)
        {
            // 必須在特定 TreeViewItem 上拖曳，故空白處不允許 drop
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void SheetTreeView_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// 從拖曳事件的來源中解析出 TreeItemViewModel 節點
        /// </summary>
        private TreeItemViewModel GetItemFromEvent(DragEventArgs e)
        {
            DependencyObject obj = e.OriginalSource as DependencyObject;
            while (obj != null && !(obj is TreeViewItem))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }
            if (obj is TreeViewItem tvi)
            {
                return tvi.Header as TreeItemViewModel;
            }
            return null;
        }

        #endregion

        #region TreeView 雙擊 / 鍵盤更名事件處理

        private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 避免雙擊事件冒泡向上傳遞到父節點，只有最底層被點擊的節點才響應
            e.Handled = true;

            if (sender is TreeViewItem tvi && tvi.Header is TreeItemViewModel item)
            {
                if (item.Type == "Sheet" || item.Type == "View" || item.Type == "Schedule")
                {
                    item.EditText = item.RawName;
                    item.IsEditing = true;
                }
            }
        }

        private void SheetTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                if (SheetTreeView.SelectedItem is TreeItemViewModel item)
                {
                    if (item.Type == "Sheet" || item.Type == "View" || item.Type == "Schedule")
                    {
                        e.Handled = true;
                        item.EditText = item.RawName;
                        item.IsEditing = true;
                    }
                }
            }
        }

        private void RenameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.IsVisible)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }

        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is TreeItemViewModel item)
            {
                CommitRename(item);
            }
        }

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is TreeItemViewModel item)
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    CommitRename(item);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    item.IsEditing = false;
                }
            }
        }

        private void CommitRename(TreeItemViewModel item)
        {
            if (!item.IsEditing) return;

            // 關閉編輯狀態
            item.IsEditing = false;

            string newName = item.EditText?.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                return;
            }

            if (newName != item.RawName)
            {
                StatusText.Text = $"狀態：正在重新命名「{item.RawName}」為「{newName}」...";
                bool success = _viewModel.RenameElement(item.Id, newName);
                if (success)
                {
                    _viewModel.LoadData();
                    StatusText.Text = $"狀態：✓ 成功重新命名為「{newName}」";
                }
                else
                {
                    StatusText.Text = "狀態：重新命名失敗。";
                }
            }
        }

        private void TreeViewItem_ToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.ToolTip is ToolTip toolTip && fe.DataContext is TreeItemViewModel item)
            {
                if (item.Type == "Sheet" || item.Type == "View" || item.Type == "Schedule")
                {
                    var stackPanel = toolTip.Content as StackPanel;
                    if (stackPanel == null) return;

                    var previewImg = stackPanel.Children.OfType<Image>().FirstOrDefault();
                    var loadingText = stackPanel.Children.OfType<TextBlock>().LastOrDefault();

                    if (previewImg != null && loadingText != null)
                    {
                        var bitmap = DevelopmentTools.Core.ViewPreviewCacheManager.GetPreviewImage(_viewModel.Doc, item.Id);
                        if (bitmap != null)
                        {
                            previewImg.Source = bitmap;
                            previewImg.Visibility = Visibility.Visible;
                            loadingText.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            previewImg.Visibility = Visibility.Collapsed;
                            loadingText.Text = "無法產生此視圖的預覽圖";
                            loadingText.Visibility = Visibility.Visible;
                        }
                    }
                }
                else
                {
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region 按鈕點擊事件

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "狀態：重新載入中...";
            _viewModel.LoadData();
            StatusText.Text = "狀態：已重新整理。";
        }

        private void NewSheetBtn_Click(object sender, RoutedEventArgs e)
        {
            // 推算建議編號
            string suggestNo = _viewModel.SuggestNextSheetNumber();

            // 彈出快速對話框
            NewSheetInputDialog dialog = new NewSheetInputDialog(suggestNo);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                StatusText.Text = $"狀態：正在建立圖紙 {dialog.SheetNumber}...";
                bool success = _viewModel.CreateNewSheet(dialog.SheetNumber, dialog.SheetName);
                if (success)
                {
                    _viewModel.LoadData();
                    StatusText.Text = $"狀態：✓ 成功建立圖紙 [{dialog.SheetNumber}] {dialog.SheetName}";
                }
                else
                {
                    StatusText.Text = "狀態：建立圖紙失敗。";
                }
            }
        }

        private void RemoveViewBtn_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = SheetTreeView.SelectedItem as TreeItemViewModel;
            if (selectedItem == null || (selectedItem.Type != "View" && selectedItem.Type != "Schedule"))
            {
                MessageBox.Show("請先在樹狀圖中選取某張圖紙下的視圖！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (selectedItem.Parent == null || selectedItem.Parent.Type != "Sheet")
            {
                MessageBox.Show("此視圖未放置在圖紙上，無法執行移除！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"確定要將視圖「{selectedItem.Name}」從圖紙「{selectedItem.Parent.Name}」中移除嗎？", 
                "確認移除", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                StatusText.Text = "狀態：正在移除視圖...";
                bool success = _viewModel.RemoveViewFromSheet(selectedItem.Id, selectedItem.Parent.Id);
                if (success)
                {
                    _viewModel.LoadData();
                    StatusText.Text = $"狀態：✓ 已成功將視圖從圖紙移除。";
                }
                else
                {
                    StatusText.Text = "狀態：移除失敗。";
                }
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        #endregion

        #region 圖紙內視圖位置調整與微調對齊排列事件

        private void SheetTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeItemViewModel item)
            {
                if (item.Type == "Sheet")
                {
                    _viewModel.SelectedSheetNode = item;
                }
                else if ((item.Type == "View" || item.Type == "Schedule") && item.Parent != null && item.Parent.Type == "Sheet")
                {
                    _viewModel.SelectedSheetNode = item.Parent;

                    // 在右側 ListBox 中選取對應視圖
                    var placedView = _viewModel.PlacedViews.FirstOrDefault(pv => pv.ViewId == item.Id);
                    if (placedView != null)
                    {
                        PlacedViewsListBox.SelectedItem = placedView;
                        PlacedViewsListBox.ScrollIntoView(placedView);
                    }
                }
                else
                {
                    _viewModel.SelectedSheetNode = null;
                }
            }
            else
            {
                _viewModel.SelectedSheetNode = null;
            }
        }

        private void PlacedViewsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedCount = PlacedViewsListBox.SelectedItems.Count;

            if (selectedCount == 1)
            {
                var singleItem = PlacedViewsListBox.SelectedItem as PlacedViewItemViewModel;
                if (singleItem != null)
                {
                    PosXTextBox.Text = singleItem.XMm.ToString("F0");
                    PosYTextBox.Text = singleItem.YMm.ToString("F0");
                    PosXTextBox.IsEnabled = true;
                    PosYTextBox.IsEnabled = true;
                }
            }
            else
            {
                PosXTextBox.Text = "";
                PosYTextBox.Text = "";
                PosXTextBox.IsEnabled = false;
                PosYTextBox.IsEnabled = false;
            }
        }

        private void PosXTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitCoordinateChange();
        }

        private void PosXTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitCoordinateChange();
            }
        }

        private void PosYTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitCoordinateChange();
        }

        private void PosYTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitCoordinateChange();
            }
        }

        private void CommitCoordinateChange()
        {
            if (PlacedViewsListBox.SelectedItems.Count != 1) return;

            var item = PlacedViewsListBox.SelectedItem as PlacedViewItemViewModel;
            if (item == null) return;

            if (double.TryParse(PosXTextBox.Text.Trim(), out double newX) &&
                double.TryParse(PosYTextBox.Text.Trim(), out double newY))
            {
                if (Math.Abs(newX - item.XMm) > 0.1 || Math.Abs(newY - item.YMm) > 0.1)
                {
                    StatusText.Text = "狀態：正在調整視圖位置...";
                    bool success = _viewModel.UpdatePlacedViewPosition(item, newX, newY);
                    if (success)
                    {
                        StatusText.Text = $"狀態：✓ 成功移動視圖至 ({newX:F0}, {newY:F0}) mm";
                    }
                    else
                    {
                        StatusText.Text = "狀態：位置調整失敗。";
                        PosXTextBox.Text = item.XMm.ToString("F0");
                        PosYTextBox.Text = item.YMm.ToString("F0");
                    }
                }
            }
            else
            {
                PosXTextBox.Text = item.XMm.ToString("F0");
                PosYTextBox.Text = item.YMm.ToString("F0");
            }
        }

        private double GetStepSize()
        {
            if (double.TryParse(StepSizeTextBox.Text.Trim(), out double step))
            {
                return step;
            }
            return 10.0;
        }

        private void TweakUpBtn_Click(object sender, RoutedEventArgs e)
        {
            TweakSelectedViews(0, GetStepSize());
        }

        private void TweakDownBtn_Click(object sender, RoutedEventArgs e)
        {
            TweakSelectedViews(0, -GetStepSize());
        }

        private void TweakLeftBtn_Click(object sender, RoutedEventArgs e)
        {
            TweakSelectedViews(-GetStepSize(), 0);
        }

        private void TweakRightBtn_Click(object sender, RoutedEventArgs e)
        {
            TweakSelectedViews(GetStepSize(), 0);
        }

        private void TweakSelectedViews(double dx, double dy)
        {
            var selectedItems = PlacedViewsListBox.SelectedItems.Cast<PlacedViewItemViewModel>().ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("請先選擇要微調的視圖！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StatusText.Text = "狀態：正在微調視圖位置...";
            _viewModel.OffsetPlacedViews(selectedItems, dx, dy);
            StatusText.Text = "狀態：✓ 微調位置完成";

            if (selectedItems.Count == 1)
            {
                PosXTextBox.Text = selectedItems[0].XMm.ToString("F0");
                PosYTextBox.Text = selectedItems[0].YMm.ToString("F0");
            }
        }

        private void AlignLeftBtn_Click(object sender, RoutedEventArgs e) { ExecuteAlign("Left"); }
        private void AlignRightBtn_Click(object sender, RoutedEventArgs e) { ExecuteAlign("Right"); }
        private void AlignTopBtn_Click(object sender, RoutedEventArgs e) { ExecuteAlign("Top"); }
        private void AlignBottomBtn_Click(object sender, RoutedEventArgs e) { ExecuteAlign("Bottom"); }
        private void AlignHCenterBtn_Click(object sender, RoutedEventArgs e) { ExecuteAlign("HCenter"); }
        private void AlignVCenterBtn_Click(object sender, RoutedEventArgs e) { ExecuteAlign("VCenter"); }

        private void ExecuteAlign(string alignType)
        {
            var selectedItems = PlacedViewsListBox.SelectedItems.Cast<PlacedViewItemViewModel>().ToList();
            if (selectedItems.Count < 2)
            {
                MessageBox.Show("請至少選擇兩個視圖進行對齊！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StatusText.Text = "狀態：正在對齊視圖...";
            _viewModel.AlignPlacedViews(selectedItems, alignType);
            StatusText.Text = "狀態：✓ 對齊視圖完成";

            if (selectedItems.Count == 1)
            {
                PosXTextBox.Text = selectedItems[0].XMm.ToString("F0");
                PosYTextBox.Text = selectedItems[0].YMm.ToString("F0");
            }
        }

        private void GridLayoutBtn_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = PlacedViewsListBox.SelectedItems.Cast<PlacedViewItemViewModel>().ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("請選擇要排列的視圖！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!int.TryParse(GridRowsTextBox.Text.Trim(), out int rows) || rows <= 0 ||
                !int.TryParse(GridColsTextBox.Text.Trim(), out int cols) || cols <= 0)
            {
                MessageBox.Show("請輸入有效的行列數！", "錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(GridGapXTextBox.Text.Trim(), out double gapX) ||
                !double.TryParse(GridGapYTextBox.Text.Trim(), out double gapY))
            {
                MessageBox.Show("請輸入有效的間距數值！", "錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(GridStartXTextBox.Text.Trim(), out double startX) ||
                !double.TryParse(GridStartYTextBox.Text.Trim(), out double startY))
            {
                MessageBox.Show("請輸入有效的起點座標數值！", "錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = "狀態：正在進行網格排列...";
            _viewModel.ArrangeViewsInGrid(selectedItems, rows, cols, gapX, gapY, startX, startY);
            StatusText.Text = "狀態：✓ 網格排列完成";

            if (selectedItems.Count == 1)
            {
                PosXTextBox.Text = selectedItems[0].XMm.ToString("F0");
                PosYTextBox.Text = selectedItems[0].YMm.ToString("F0");
            }
        }

        private void CanvasItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as FrameworkElement;
            if (border == null) return;

            var item = border.DataContext as PlacedViewItemViewModel;
            if (item == null) return;

            _isDraggingInCanvas = true;
            _draggedCanvasItem = item;
            _dragStartMousePos = e.GetPosition(PlacedViewsItemsControl);
            _dragStartCanvasX = item.CanvasX;
            _dragStartCanvasY = item.CanvasY;

            border.CaptureMouse();
            
            // 同時將其設為 PlacedViewsListBox 的選取項
            PlacedViewsListBox.SelectedItem = item;
            
            e.Handled = true;
        }

        private void CanvasItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingInCanvas || _draggedCanvasItem == null) return;

            var border = sender as FrameworkElement;
            if (border == null) return;

            System.Windows.Point currentMousePos = e.GetPosition(PlacedViewsItemsControl);
            double deltaX = currentMousePos.X - _dragStartMousePos.X;
            double deltaY = currentMousePos.Y - _dragStartMousePos.Y;

            // 即時移動 UI
            _draggedCanvasItem.CanvasX = _dragStartCanvasX + deltaX;
            _draggedCanvasItem.CanvasY = _dragStartCanvasY + deltaY;
        }

        private void CanvasItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingInCanvas || _draggedCanvasItem == null) return;

            var border = sender as FrameworkElement;
            if (border != null)
            {
                border.ReleaseMouseCapture();
            }

            _isDraggingInCanvas = false;

            // 計算新中心點並寫入 Revit
            double newCenterX_mm = _draggedCanvasItem.CanvasX + _draggedCanvasItem.WidthMm / 2;
            double newCenterY_mm = _draggedCanvasItem.SheetHeight - _draggedCanvasItem.CanvasY - _draggedCanvasItem.HeightMm / 2;

            StatusText.Text = "狀態：正在調整視圖位置...";
            bool success = _viewModel.UpdatePlacedViewPosition(_draggedCanvasItem, newCenterX_mm, newCenterY_mm);
            if (success)
            {
                StatusText.Text = $"狀態：✓ 成功移動視圖至 ({newCenterX_mm:F0}, {newCenterY_mm:F0}) mm";
                PosXTextBox.Text = newCenterX_mm.ToString("F0");
                PosYTextBox.Text = newCenterY_mm.ToString("F0");
            }
            else
            {
                StatusText.Text = "狀態：位置調整失敗，還原座標。";
                _draggedCanvasItem.CanvasX = _dragStartCanvasX;
                _draggedCanvasItem.CanvasY = _dragStartCanvasY;
            }

            _draggedCanvasItem = null;
            e.Handled = true;
        }

        #endregion
    }

    /// <summary>
    /// 一個輕量級的圖紙新增輸入對話視窗
    /// </summary>
    public class NewSheetInputDialog : Window
    {
        public string SheetNumber { get; private set; }
        public string SheetName { get; private set; }

        private TextBox _numTextBox;
        private TextBox _nameTextBox;

        public NewSheetInputDialog(string defaultNumber)
        {
            this.Title = "建立新圖紙";
            this.Width = 350;
            this.Height = 180;
            this.Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
            this.Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.ShowInTaskbar = false;
            this.ResizeMode = ResizeMode.NoResize;

            Grid mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 1. 編號
            StackPanel sp1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            sp1.Children.Add(new TextBlock { Text = "圖紙編號：", Width = 70, VerticalAlignment = VerticalAlignment.Center });
            _numTextBox = new TextBox 
            { 
                Text = defaultNumber, 
                Width = 220, 
                Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                CaretBrush = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                Padding = new Thickness(4),
                Height = 26
            };
            sp1.Children.Add(_numTextBox);
            Grid.SetRow(sp1, 0);
            mainGrid.Children.Add(sp1);

            // 2. 名稱
            StackPanel sp2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            sp2.Children.Add(new TextBlock { Text = "圖紙名稱：", Width = 70, VerticalAlignment = VerticalAlignment.Center });
            _nameTextBox = new TextBox 
            { 
                Text = "未命名圖紙", 
                Width = 220, 
                Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                CaretBrush = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                Padding = new Thickness(4),
                Height = 26
            };
            sp2.Children.Add(_nameTextBox);
            Grid.SetRow(sp2, 1);
            mainGrid.Children.Add(sp2);

            // 3. 按鈕
            StackPanel sp3 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button okBtn = new Button 
            { 
                Content = "確定", 
                Width = 70, 
                Height = 26, 
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                FontWeight = FontWeights.Bold
            };
            okBtn.Click += (s, e) =>
            {
                this.SheetNumber = _numTextBox.Text.Trim();
                this.SheetName = _nameTextBox.Text.Trim();
                this.DialogResult = true;
                this.Close();
            };

            Button cancelBtn = new Button 
            { 
                Content = "取消", 
                Width = 70, 
                Height = 26,
                Background = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
            };
            cancelBtn.Click += (s, e) => { this.DialogResult = false; this.Close(); };

            sp3.Children.Add(okBtn);
            sp3.Children.Add(cancelBtn);
            Grid.SetRow(sp3, 2);
            mainGrid.Children.Add(sp3);

            this.Content = mainGrid;
        }
    }
}
