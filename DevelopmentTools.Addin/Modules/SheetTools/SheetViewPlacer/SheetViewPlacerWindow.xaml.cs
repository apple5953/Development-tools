using System;
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
