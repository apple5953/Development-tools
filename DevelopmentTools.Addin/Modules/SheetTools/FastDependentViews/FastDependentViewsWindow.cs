using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using DevelopmentTools.Modules.SheetTools.FastDependentViews;

using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Grid = System.Windows.Controls.Grid;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Ellipse = System.Windows.Shapes.Ellipse;
using WpfVisibility = System.Windows.Visibility;

namespace DevelopmentTools.Modules.SheetTools.FastDependentViews
{
    public class FastDependentViewsWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;
        private readonly View _activeView;
        private List<ElementItem> _allItems = new List<ElementItem>();
        private bool _isInitialized = false;

        // UI 控制項引用
        private TextBlock txtParentViewName;
        private RadioButton rbRoom;
        private RadioButton rbSpace;
        private RadioButton rbFloor;
        private ComboBox comboBoundaryLocation;
        private StackPanel panelBoundaryLocation;
        private TextBox txtNamingPattern;
        private TextBlock txtPreviewName;
        private CheckBox chkSelectAll;
        private CheckBox chkVisibleOnly;
        private TextBox txtSearch;
        private TextBlock txtSearchPlaceholder;
        private ListView lstElements;
        private TextBlock txtStatus;
        private ProgressBar progress;
        private Button btnStart;
        private Button btnCancel;

        // 顏色與筆刷定義
        private readonly Brush _windowBg = new SolidColorBrush(Color.FromRgb(30, 30, 36));     // #1E1E24
        private readonly Brush _cardBg = new SolidColorBrush(Color.FromRgb(40, 40, 48));       // #282830
        private readonly Brush _borderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));  // #3F3F46
        private readonly Brush _textPrimary = Brushes.White;
        private readonly Brush _textSecondary = new SolidColorBrush(Color.FromRgb(170, 170, 170)); // #AAAAAA
        private readonly LinearGradientBrush _accentBrush;

        public FastDependentViewsWindow(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _doc = uiApp.ActiveUIDocument.Document;
            _activeView = _doc.ActiveView;

            // 初始化漸層色
            _accentBrush = new LinearGradientBrush(
                Color.FromRgb(0, 120, 212), // #0078D4
                Color.FromRgb(0, 183, 195), // #00B7C3
                new Point(0, 0), new Point(1, 1)
            );

            InitializeModernUI();

            txtParentViewName.Text = _activeView.Name;
            _isInitialized = true;

            LoadElements();
            UpdateNamingPreview();
        }

        private static string L(string key)
        {
            return LanguageManager.Instance[key];
        }

        /// <summary>
        /// 使用純 C# 動態建立並排版 WPF UI，省去 XAML 標記編譯器的依賴。
        /// </summary>
        private void InitializeModernUI()
        {
            // 1. 視窗基本設定 (無邊框、圓角、透明背景)
            this.Title = L("FastDep_Title");
            this.Height = 620;
            this.Width = 850;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.AllowsTransparency = true;
            this.WindowStyle = WindowStyle.None;
            this.Background = Brushes.Transparent;
            this.FontFamily = new FontFamily("Microsoft JhengHei, Segoe UI");
            this.Foreground = _textPrimary;

            // 2. 視窗最外層 Border (帶圓角與陰影)
            Border rootBorder = new Border
            {
                Background = _windowBg,
                CornerRadius = new CornerRadius(10),
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 15,
                    Direction = -90,
                    ShadowDepth = 2,
                    Opacity = 0.5
                }
            };

            // 3. 主 Grid 佈局 (標題列、內容區、底部列)
            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
            rootBorder.Child = mainGrid;
            this.Content = rootBorder;

            #region 標題列 (Row 0)
            Grid titleBar = new Grid { Background = new SolidColorBrush(Color.FromRgb(24, 24, 28)) };
            titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;

            // 標題與標誌
            StackPanel titlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0) };
            Ellipse logoDot = new Ellipse { Width = 10, Height = 10, Fill = _accentBrush, Margin = new Thickness(0, 0, 10, 0) };
            TextBlock titleText = new TextBlock { Text = L("FastDep_Title"), Foreground = _textPrimary, FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            TextBlock versionText = new TextBlock { Text = L("FastDep_Version"), Foreground = _textSecondary, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titlePanel.Children.Add(logoDot);
            titlePanel.Children.Add(titleText);
            titlePanel.Children.Add(versionText);
            titleBar.Children.Add(titlePanel);

            StackPanel titleActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };

            Button btnHelp = new Button
            {
                Content = L("TileSys_Tutorial"),
                Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(255, 204, 102)),
                Foreground = new SolidColorBrush(Color.FromRgb(17, 19, 24)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 7, 8, 0),
                FontSize = 12,
                FontWeight = FontWeights.Bold
            };
            btnHelp.Template = CreateButtonTemplate(new SolidColorBrush(Color.FromRgb(255, 204, 102)), new SolidColorBrush(Color.FromRgb(255, 217, 128)));
            btnHelp.ToolTip = L("TileSys_TutorialDesc");
            btnHelp.Click += HelpButton_Click;
            titleActions.Children.Add(btnHelp);

            // 關閉按鈕
            Button btnClose = new Button
            {
                Content = "✕",
                Width = 45,
                Height = 35,
                Background = Brushes.Transparent,
                Foreground = _textSecondary,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnClose.Template = CreateButtonTemplate(Brushes.Transparent, new SolidColorBrush(Color.FromRgb(180, 48, 48)));
            btnClose.Click += CloseButton_Click;
            titleActions.Children.Add(btnClose);
            titleBar.Children.Add(titleActions);

            mainGrid.Children.Add(titleBar);
            Grid.SetRow(titleBar, 0);
            #endregion

            #region 主要內容區 (Row 1)
            Grid contentGrid = new Grid { Margin = new Thickness(15, 10, 15, 10) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.Children.Add(contentGrid);
            Grid.SetRow(contentGrid, 1);

            // ================= 左欄：控制面板 =================
            StackPanel leftPanel = new StackPanel { Margin = new Thickness(0, 0, 15, 0) };
            contentGrid.Children.Add(leftPanel);
            Grid.SetColumn(leftPanel, 0);

            // 當前視圖卡片
            Border activeViewCard = new Border { Background = _cardBg, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 15) };
            StackPanel activeViewStack = new StackPanel();
            activeViewStack.Children.Add(new TextBlock { Text = L("FastDep_CurrentView"), Foreground = _textSecondary, FontSize = 11 });
            txtParentViewName = new TextBlock { Text = "", Foreground = _textPrimary, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            activeViewStack.Children.Add(txtParentViewName);
            activeViewCard.Child = activeViewStack;
            leftPanel.Children.Add(activeViewCard);

            // 區分依據 GroupBox
            GroupBox gpFilter = CreateDarkGroupBox(L("FastDep_FilterGroup"), new Thickness(0, 0, 0, 15));
            StackPanel gpFilterStack = new StackPanel();
            rbRoom = CreateRadioButton(L("FastDep_Rooms"), true);
            rbRoom.Checked += FilterType_Changed;
            rbSpace = CreateRadioButton(L("FastDep_Spaces"), false);
            rbSpace.Checked += FilterType_Changed;
            rbFloor = CreateRadioButton(L("FastDep_Floors"), false);
            rbFloor.Checked += FilterType_Changed;
            gpFilterStack.Children.Add(rbRoom);
            gpFilterStack.Children.Add(rbSpace);
            gpFilterStack.Children.Add(rbFloor);

            panelBoundaryLocation = new StackPanel { Margin = new Thickness(20, 10, 0, 0) };
            panelBoundaryLocation.Children.Add(new TextBlock { Text = L("FastDep_BoundaryLocation"), Foreground = _textSecondary, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            comboBoundaryLocation = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(238, 238, 238)),
                Foreground = Brushes.Black,
                BorderBrush = _borderBrush,
                Padding = new Thickness(6, 4, 6, 4)
            };
            comboBoundaryLocation.Resources[SystemColors.GrayTextBrushKey] = Brushes.Black;
            comboBoundaryLocation.Items.Add(new ComboBoxItem { Content = L("FastDep_WallFinish"), Background = Brushes.White, Foreground = Brushes.Black });
            comboBoundaryLocation.Items.Add(new ComboBoxItem { Content = L("FastDep_CoreCenter"), Background = Brushes.White, Foreground = Brushes.Black });
            comboBoundaryLocation.SelectedIndex = 0;
            comboBoundaryLocation.SelectionChanged += FilterType_Changed;
            panelBoundaryLocation.Children.Add(comboBoundaryLocation);
            gpFilterStack.Children.Add(panelBoundaryLocation);
            gpFilter.Content = gpFilterStack;
            leftPanel.Children.Add(gpFilter);

            // 命名規則 GroupBox
            GroupBox gpNaming = CreateDarkGroupBox(L("FastDep_NamingGroup"), new Thickness(0));
            StackPanel gpNamingStack = new StackPanel();
            txtNamingPattern = new TextBox { Text = "{ParentView} - {Name}", Background = new SolidColorBrush(Color.FromRgb(24, 24, 28)), Foreground = Brushes.White, BorderBrush = _borderBrush, CaretBrush = Brushes.White, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8) };
            txtNamingPattern.TextChanged += NamingPattern_TextChanged;
            gpNamingStack.Children.Add(txtNamingPattern);

            gpNamingStack.Children.Add(new TextBlock { Text = L("FastDep_InsertTagsHint"), Foreground = _textSecondary, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            WrapPanel tagPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            tagPanel.Children.Add(CreateTagButton("{ParentView}"));
            tagPanel.Children.Add(CreateTagButton("{Name}"));
            tagPanel.Children.Add(CreateTagButton("{Number}"));
            tagPanel.Children.Add(CreateTagButton("{Id}"));
            gpNamingStack.Children.Add(tagPanel);

            Border previewCard = new Border { Background = new SolidColorBrush(Color.FromRgb(24, 24, 28)), CornerRadius = new CornerRadius(4), Padding = new Thickness(8), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(45, 45, 55)) };
            StackPanel previewStack = new StackPanel();
            previewStack.Children.Add(new TextBlock { Text = L("FastDep_PreviewLabel"), Foreground = _textSecondary, FontSize = 11 });
            txtPreviewName = new TextBlock { Text = "", Foreground = new SolidColorBrush(Color.FromRgb(0, 183, 195)), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            previewStack.Children.Add(txtPreviewName);
            previewCard.Child = previewStack;
            gpNamingStack.Children.Add(previewCard);

            gpNaming.Content = gpNamingStack;
            leftPanel.Children.Add(gpNaming);

            // ================= 右欄：清單面板 =================
            Grid rightPanel = new Grid();
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            contentGrid.Children.Add(rightPanel);
            Grid.SetColumn(rightPanel, 1);

            // 工具列 (搜尋與全選)
            Grid toolbarGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rightPanel.Children.Add(toolbarGrid);
            Grid.SetRow(toolbarGrid, 0);

            StackPanel selectStack = new StackPanel { Orientation = Orientation.Horizontal };
            chkSelectAll = CreateCheckBox(L("FastDep_SelectAll"), true);
            chkSelectAll.Checked += SelectAll_Checked;
            chkSelectAll.Unchecked += SelectAll_Unchecked;
            chkVisibleOnly = CreateCheckBox(L("FastDep_VisibleOnly"), true);
            chkVisibleOnly.Margin = new Thickness(20, 0, 0, 0);
            chkVisibleOnly.Checked += VisibleOnly_Changed;
            chkVisibleOnly.Unchecked += VisibleOnly_Changed;
            selectStack.Children.Add(chkSelectAll);
            selectStack.Children.Add(chkVisibleOnly);
            toolbarGrid.Children.Add(selectStack);
            Grid.SetColumn(selectStack, 0);

            // 搜尋框
            Grid searchGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Right, Width = 220 };
            txtSearch = new TextBox { Background = new SolidColorBrush(Color.FromRgb(24, 24, 28)), Foreground = Brushes.White, BorderBrush = _borderBrush, CaretBrush = Brushes.White, Padding = new Thickness(25, 6, 8, 6) };
            txtSearch.TextChanged += Search_TextChanged;
            txtSearch.GotFocus += Search_GotFocus;
            txtSearch.LostFocus += Search_LostFocus;
            searchGrid.Children.Add(txtSearch);

            txtSearchPlaceholder = new TextBlock { Text = L("FastDep_SearchPlaceholder"), Foreground = _textSecondary, FontSize = 12, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0) };
            searchGrid.Children.Add(txtSearchPlaceholder);

            TextBlock searchIcon = new TextBlock { Text = "🔍", Foreground = _textSecondary, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 0) };
            searchGrid.Children.Add(searchIcon);

            toolbarGrid.Children.Add(searchGrid);
            Grid.SetColumn(searchGrid, 1);

            // ListView 清單
            Border listBorder = new Border { Background = _cardBg, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = _borderBrush, ClipToBounds = true };
            lstElements = new ListView
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                ItemContainerStyle = CreateListViewItemStyle()
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(lstElements, ScrollBarVisibility.Disabled);

            // 設定 GridView
            GridView gv = new GridView();
            
            // Column 1: Checkbox
            GridViewColumn colCheck = new GridViewColumn { Width = 45 };
            FrameworkElementFactory chkFactory = new FrameworkElementFactory(typeof(CheckBox));
            chkFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            chkFactory.SetBinding(CheckBox.IsEnabledProperty, new Binding("HasValidBoundary"));
            chkFactory.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            colCheck.CellTemplate = new DataTemplate { VisualTree = chkFactory };
            gv.Columns.Add(colCheck);

            // Column 2: Number
            GridViewColumn colNum = new GridViewColumn { Header = CreateColumnHeader(L("FastDep_ColNumber")), Width = 80 };
            FrameworkElementFactory numFactory = new FrameworkElementFactory(typeof(TextBlock));
            numFactory.SetBinding(TextBlock.TextProperty, new Binding("Number"));
            numFactory.SetValue(TextBlock.ForegroundProperty, _textSecondary);
            colNum.CellTemplate = new DataTemplate { VisualTree = numFactory };
            gv.Columns.Add(colNum);

            // Column 3: Name
            GridViewColumn colName = new GridViewColumn { Header = CreateColumnHeader(L("FastDep_ColName")), Width = 180 };
            FrameworkElementFactory nameFactory = new FrameworkElementFactory(typeof(TextBlock));
            nameFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            nameFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nameFactory.SetValue(TextBlock.ForegroundProperty, _textPrimary);
            colName.CellTemplate = new DataTemplate { VisualTree = nameFactory };
            gv.Columns.Add(colName);

            // Column 4: ID
            GridViewColumn colId = new GridViewColumn { Header = CreateColumnHeader(L("FastDep_ColId")), Width = 80 };
            FrameworkElementFactory idFactory = new FrameworkElementFactory(typeof(TextBlock));
            idFactory.SetBinding(TextBlock.TextProperty, new Binding("Id"));
            idFactory.SetValue(TextBlock.ForegroundProperty, _textSecondary);
            colId.CellTemplate = new DataTemplate { VisualTree = idFactory };
            gv.Columns.Add(colId);

            // Column 5: Status
            GridViewColumn colStatus = new GridViewColumn { Header = CreateColumnHeader(L("FastDep_ColStatus")), Width = 140 };
            FrameworkElementFactory statusFactory = new FrameworkElementFactory(typeof(TextBlock));
            statusFactory.SetBinding(TextBlock.TextProperty, new Binding("Status"));
            statusFactory.SetBinding(TextBlock.ForegroundProperty, new Binding("StatusColor"));
            statusFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            colStatus.CellTemplate = new DataTemplate { VisualTree = statusFactory };
            gv.Columns.Add(colStatus);

            lstElements.View = gv;
            listBorder.Child = lstElements;
            rightPanel.Children.Add(listBorder);
            Grid.SetRow(listBorder, 1);
            #endregion

            #region 底部狀態列與按鈕 (Row 2)
            Grid footer = new Grid { Background = new SolidColorBrush(Color.FromRgb(24, 24, 28)) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainGrid.Children.Add(footer);
            Grid.SetRow(footer, 2);

            // 左側進度列
            StackPanel progressPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 15, 0) };
            txtStatus = new TextBlock { Text = L("FastDep_Ready"), Foreground = _textSecondary, FontSize = 12 };
            progress = new ProgressBar { Height = 6, Margin = new Thickness(0, 6, 0, 0), Background = new SolidColorBrush(Color.FromRgb(43, 43, 54)), Foreground = _accentBrush, BorderThickness = new Thickness(0), Minimum = 0, Maximum = 100, Value = 0, Visibility = WpfVisibility.Collapsed };
            progressPanel.Children.Add(txtStatus);
            progressPanel.Children.Add(progress);
            footer.Children.Add(progressPanel);
            Grid.SetColumn(progressPanel, 0);

            // 右側按鈕
            StackPanel btnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 15, 0) };
            
            btnCancel = new Button { Content = L("FastDep_Cancel"), Width = 80, Height = 36, Background = new SolidColorBrush(Color.FromRgb(63, 63, 70)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0) };
            // 給按鈕圓角
            btnCancel.Template = CreateButtonTemplate(new SolidColorBrush(Color.FromRgb(63, 63, 70)), new SolidColorBrush(Color.FromRgb(79, 79, 86)));
            btnCancel.Click += CloseButton_Click;

            btnStart = new Button { Content = L("FastDep_Start"), Width = 120, Height = 36, Background = _accentBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            btnStart.Template = CreateButtonTemplate(_accentBrush, new SolidColorBrush(Color.FromRgb(16, 197, 209)));
            btnStart.Click += BtnStart_Click;

            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnStart);
            footer.Children.Add(btnPanel);
            Grid.SetColumn(btnPanel, 1);
            #endregion
        }

        private Button CreateTagButton(string text)
        {
            Button btn = new Button
            {
                Content = text,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 55)),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 183, 195)),
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand,
                FontSize = 11
            };
            btn.Template = CreateButtonTemplate(new SolidColorBrush(Color.FromRgb(45, 45, 55)), new SolidColorBrush(Color.FromRgb(61, 61, 77)));
            btn.Click += InsertTag_Click;
            return btn;
        }

        private GroupBox CreateDarkGroupBox(string header, Thickness margin)
        {
            return new GroupBox
            {
                Header = header,
                Background = _cardBg,
                BorderBrush = _borderBrush,
                Foreground = _textPrimary,
                Margin = margin,
                Padding = new Thickness(10)
            };
        }

        private RadioButton CreateRadioButton(string text, bool isChecked)
        {
            return new RadioButton
            {
                Content = text,
                IsChecked = isChecked,
                Foreground = _textPrimary,
                Background = Brushes.Transparent,
                BorderBrush = _borderBrush,
                Margin = new Thickness(0, 5, 0, 5),
                FontSize = 13
            };
        }

        private CheckBox CreateCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Foreground = _textPrimary,
                Background = Brushes.Transparent,
                BorderBrush = _borderBrush,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            };
        }

        private TextBlock CreateColumnHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = _textPrimary,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(2, 0, 2, 0)
            };
        }

        private Style CreateListViewItemStyle()
        {
            Style style = new Style(typeof(ListViewItem));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, _textPrimary));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(52, 57, 70))));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            style.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(4, 6, 4, 6)));

            Trigger selected = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(34, 74, 92))));
            selected.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, _textPrimary));
            style.Triggers.Add(selected);

            Trigger mouseOver = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            mouseOver.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(34, 38, 48))));
            style.Triggers.Add(mouseOver);

            return style;
        }

        private ControlTemplate CreateButtonTemplate(Brush normalBg, Brush hoverBg)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border), "border");
            border.SetValue(Border.BackgroundProperty, normalBg);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            template.VisualTree = border;

            Trigger mouseOver = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            mouseOver.Setters.Add(new Setter { TargetName = "border", Property = Border.BackgroundProperty, Value = hoverBg });
            template.Triggers.Add(mouseOver);

            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter { TargetName = "border", Property = Border.BackgroundProperty, Value = new SolidColorBrush(Color.FromRgb(63, 63, 70)) });
            template.Triggers.Add(disabled);

            return template;
        }

        #region 資料載入與過濾

        private void LoadElements()
        {
            if (!_isInitialized) return;

            _allItems.Clear();
            btnStart.IsEnabled = true;

            Type targetType = null;
            bool isFloor = false;

            if (rbRoom.IsChecked == true)
            {
                targetType = typeof(SpatialElement);
                panelBoundaryLocation.Visibility = WpfVisibility.Visible;
            }
            else if (rbSpace.IsChecked == true)
            {
                targetType = typeof(SpatialElement);
                panelBoundaryLocation.Visibility = WpfVisibility.Visible;
            }
            else if (rbFloor.IsChecked == true)
            {
                targetType = typeof(Floor);
                isFloor = true;
                panelBoundaryLocation.Visibility = WpfVisibility.Collapsed;
            }

            if (targetType == null) return;

            FilteredElementCollector collector;
            if (chkVisibleOnly.IsChecked == true)
            {
                collector = new FilteredElementCollector(_doc, _activeView.Id);
            }
            else
            {
                collector = new FilteredElementCollector(_doc);
            }

            collector.OfClass(targetType);

            foreach (Element elem in collector)
            {
                if (!isFloor)
                {
                    if (rbRoom.IsChecked == true && !(elem is Room))
                        continue;
                    if (rbSpace.IsChecked == true && !(elem is Space))
                        continue;

                    var spatial = elem as SpatialElement;
                    if (spatial.Area <= 0 || spatial.Location == null)
                        continue;
                }

                var item = new ElementItem
                {
                    Element = elem,
                    Id = elem.Id.ToString(),
                    Name = elem.Name
                };

                Room r = elem as Room;
                Space s = elem as Space;
                Floor f = elem as Floor;
                if (r != null)
                {
                    item.Number = r.Number;
                }
                else if (s != null)
                {
                    item.Number = s.Number;
                }
                else if (f != null)
                {
                    item.Number = "";
                    item.Name = f.Name;
                }

                bool hasBoundary = false;
                try
                {
                    CurveLoop boundary = null;
                    SpatialElement spatial = elem as SpatialElement;
                    Floor floor = elem as Floor;
                    if (spatial != null)
                    {
                        var loc = comboBoundaryLocation.SelectedIndex == 0 
                            ? SpatialElementBoundaryLocation.Finish 
                            : SpatialElementBoundaryLocation.Center;
                        boundary = RevitUtils.GetSpatialElementBoundary(spatial, loc);
                    }
                    else if (floor != null)
                    {
                        boundary = RevitUtils.GetFloorBoundary(floor);
                    }

                    hasBoundary = boundary != null;
                }
                catch
                {
                    hasBoundary = false;
                }

                if (hasBoundary)
                {
                    item.HasValidBoundary = true;
                    item.IsSelected = true;
                    item.Status = L("FastDep_ItemReady");
                    item.StatusColor = new SolidColorBrush(Color.FromRgb(0, 183, 195)); // 活力藍綠
                }
                else
                {
                    item.HasValidBoundary = false;
                    item.IsSelected = false;
                    item.Status = L("FastDep_InvalidBoundary");
                    item.StatusColor = new SolidColorBrush(Color.FromRgb(255, 140, 0)); // 橘黃
                }

                _allItems.Add(item);
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string searchTxt = txtSearch.Text.Trim().ToLower();
            List<ElementItem> filtered;

            if (string.IsNullOrEmpty(searchTxt))
            {
                filtered = _allItems;
            }
            else
            {
                filtered = _allItems.Where(item => 
                    item.Name.ToLower().Contains(searchTxt) || 
                    (item.Number != null && item.Number.ToLower().Contains(searchTxt)) ||
                    item.Id.Contains(searchTxt)
                ).ToList();
            }

            lstElements.ItemsSource = filtered;

            int selectedCount = filtered.Count(i => i.IsSelected && i.HasValidBoundary);
            txtStatus.Text = string.Format(L("FastDep_ResultCount"), filtered.Count, selectedCount);
        }

        #endregion

        #region 事件處理與互動

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string title = L("Tut_FastDependentViews_Title");
            TaskDialog td = new TaskDialog(title)
            {
                TitleAutoPrefix = false,
                MainInstruction = title,
                MainContent = L("Tut_FastDependentViews_Content")
            };
            td.Show();
        }

        private void FilterType_Changed(object sender, RoutedEventArgs e)
        {
            LoadElements();
            UpdateNamingPreview();
        }

        private void VisibleOnly_Changed(object sender, RoutedEventArgs e)
        {
            LoadElements();
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            ApplyFilter();
        }

        private void Search_GotFocus(object sender, RoutedEventArgs e)
        {
            txtSearchPlaceholder.Foreground = new SolidColorBrush(Color.FromRgb(0, 183, 195));
        }

        private void Search_LostFocus(object sender, RoutedEventArgs e)
        {
            txtSearchPlaceholder.Foreground = _textSecondary;
        }

        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            var filtered = lstElements.ItemsSource as List<ElementItem>;
            if (filtered == null) return;

            foreach (var item in filtered)
            {
                if (item.HasValidBoundary)
                {
                    item.IsSelected = true;
                }
            }
            ApplyFilter();
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            var filtered = lstElements.ItemsSource as List<ElementItem>;
            if (filtered == null) return;

            foreach (var item in filtered)
            {
                item.IsSelected = false;
            }
            ApplyFilter();
        }

        private void NamingPattern_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateNamingPreview();
        }

        private void InsertTag_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            string tag = btn.Content.ToString();
            int caretIndex = txtNamingPattern.CaretIndex;
            string text = txtNamingPattern.Text;

            txtNamingPattern.Text = text.Insert(caretIndex, tag);
            txtNamingPattern.CaretIndex = caretIndex + tag.Length;
            txtNamingPattern.Focus();
        }

        private void UpdateNamingPreview()
        {
            if (!_isInitialized) return;

            string pattern = txtNamingPattern.Text;
            string preview = "";

            if (_allItems.Count > 0)
            {
                var first = _allItems[0];
                preview = RevitUtils.FormatViewName(pattern, _activeView.Name, first.Name, first.Number, first.Id);
            }
            else
            {
                string name = rbRoom.IsChecked == true ? "客廳" : (rbSpace.IsChecked == true ? "辦公室" : "30cm RC 樓板");
                string num = rbFloor.IsChecked == true ? "" : "101";
                preview = RevitUtils.FormatViewName(pattern, _activeView.Name, name, num, "99999");
            }

            txtPreviewName.Text = preview;
        }

        #endregion

        #region 核心執行邏輯

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var itemsToCreate = _allItems.Where(i => i.IsSelected && i.HasValidBoundary).ToList();

            if (itemsToCreate.Count == 0)
            {
                MessageBox.Show(L("FastDep_SelectWarning"), L("Common_Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string namingPattern = txtNamingPattern.Text.Trim();
            if (string.IsNullOrEmpty(namingPattern))
            {
                MessageBox.Show(L("FastDep_NamingWarning"), L("Common_Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 停用控制
            btnStart.IsEnabled = false;
            btnCancel.IsEnabled = false;
            lstElements.IsEnabled = false;
            chkSelectAll.IsEnabled = false;
            chkVisibleOnly.IsEnabled = false;
            rbRoom.IsEnabled = false;
            rbSpace.IsEnabled = false;
            rbFloor.IsEnabled = false;
            comboBoundaryLocation.IsEnabled = false;
            txtNamingPattern.IsEnabled = false;

            progress.Visibility = WpfVisibility.Visible;
            progress.Value = 0;

            int boundaryLocationIndex = comboBoundaryLocation.SelectedIndex;
            int successCount = 0;

            using (TransactionGroup transGroup = new TransactionGroup(_doc, "快速建立從屬視圖"))
            {
                transGroup.Start();

                for (int i = 0; i < itemsToCreate.Count; i++)
                {
                    var item = itemsToCreate[i];

                    using (Transaction trans = new Transaction(_doc, string.Format("建立從屬視圖 - {0}", item.Name)))
                    {
                        trans.Start();

                        try
                        {
                            ElementId newViewId = _activeView.Duplicate(ViewDuplicateOption.AsDependent);
                            View depView = _doc.GetElement(newViewId) as View;

                            if (depView == null)
                                throw new Exception("視圖複製失敗");

                            string baseName = RevitUtils.FormatViewName(namingPattern, _activeView.Name, item.Name, item.Number, item.Id);
                            string uniqueName = RevitUtils.GetUniqueViewName(_doc, baseName);
                            depView.Name = uniqueName;

                            CurveLoop boundary = null;
                            SpatialElement spatial = item.Element as SpatialElement;
                            Floor floor = item.Element as Floor;
                            if (spatial != null)
                            {
                                var loc = boundaryLocationIndex == 0 
                                    ? SpatialElementBoundaryLocation.Finish 
                                    : SpatialElementBoundaryLocation.Center;
                                boundary = RevitUtils.GetSpatialElementBoundary(spatial, loc);
                            }
                            else if (floor != null)
                            {
                                boundary = RevitUtils.GetFloorBoundary(floor);
                            }

                            if (boundary != null)
                            {
                                CurveLoop projectedBoundary = RevitUtils.ProjectCurveLoopToZ(boundary, _activeView.Origin.Z);

                                ViewCropRegionShapeManager cropManager = depView.GetCropRegionShapeManager();
                                if (cropManager != null)
                                {
                                    cropManager.SetCropShape(projectedBoundary);
                                    depView.CropBoxActive = true;
                                    depView.CropBoxVisible = true;
                                }
                                else
                                {
                                    BoundingBoxXYZ bbox = item.Element.get_BoundingBox(null);
                                    if (bbox != null)
                                    {
                                        depView.CropBox = bbox;
                                        depView.CropBoxActive = true;
                                        depView.CropBoxVisible = true;
                                    }
                                }
                            }

                            trans.Commit();
                            successCount++;
                            item.Status = L("FastDep_SuccessStatus");
                            item.StatusColor = Brushes.LightGreen;
                        }
                        catch (Exception ex)
                        {
                            trans.RollBack();
                            item.Status = string.Format(L("FastDep_FailedStatus"), ex.Message);
                            item.StatusColor = Brushes.Tomato;
                        }
                    }

                    int progressPercent = (int)((double)(i + 1) / itemsToCreate.Count * 100);
                    progress.Value = progressPercent;
                    txtStatus.Text = string.Format(L("FastDep_Processing"), i + 1, itemsToCreate.Count, item.Name);

                    Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                }

                transGroup.Assimilate();
            }

            btnStart.IsEnabled = true;
            btnCancel.IsEnabled = true;
            lstElements.IsEnabled = true;
            chkSelectAll.IsEnabled = true;
            chkVisibleOnly.IsEnabled = true;
            rbRoom.IsEnabled = true;
            rbSpace.IsEnabled = true;
            rbFloor.IsEnabled = true;
            comboBoundaryLocation.IsEnabled = true;
            txtNamingPattern.IsEnabled = true;

            progress.Visibility = WpfVisibility.Collapsed;
            txtStatus.Text = string.Format(L("FastDep_DoneStatus"), successCount, itemsToCreate.Count - successCount);

            MessageBox.Show(string.Format(L("FastDep_CompleteMessage"), successCount), L("FastDep_CompleteTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            
            ApplyFilter();
        }

        #endregion
    }

    public class ElementItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _status;
        private Brush _statusColor;

        public Element Element { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public bool HasValidBoundary { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

        public string Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged("Status");
                }
            }
        }

        public Brush StatusColor
        {
            get { return _statusColor; }
            set
            {
                if (_statusColor != value)
                {
                    _statusColor = value;
                    OnPropertyChanged("StatusColor");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
