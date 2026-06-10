using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public class DT_TileElevationGeneratorViewModel : INotifyPropertyChanged
    {
        private readonly ExternalCommandData _commandData;
        private readonly Document _doc;
        private readonly UIDocument _uidoc;
        
        private readonly GeneratorExternalEventHandler _externalEventHandler;
        private readonly ExternalEvent _externalEvent;

        public GeneratorSettings Settings { get; set; } = new GeneratorSettings();
        
        // UI 綁定列表與選取項
        public List<ViewTemplateItem> ViewTemplates { get; private set; }
        
        // 新增幾何參數繫結
        public double ViewDepth
        {
            get => Settings.ViewDepth;
            set
            {
                Settings.ViewDepth = value;
                OnPropertyChanged();
            }
        }

        public double WallOffset
        {
            get => Settings.WallOffset;
            set
            {
                Settings.WallOffset = value;
                OnPropertyChanged();
            }
        }

        public double BottomOffset
        {
            get => Settings.BottomOffset;
            set
            {
                Settings.BottomOffset = value;
                OnPropertyChanged();
            }
        }

        public double TopOffset
        {
            get => Settings.TopOffset;
            set
            {
                Settings.TopOffset = value;
                OnPropertyChanged();
            }
        }

        public double SideExtension
        {
            get => Settings.SideExtension;
            set
            {
                Settings.SideExtension = value;
                OnPropertyChanged();
            }
        }

        // 新增圖框與視埠繫結
        public List<ComboboxItem> TitleBlocks { get; private set; }
        
        private ComboboxItem _selectedTitleBlock;
        public ComboboxItem SelectedTitleBlock
        {
            get => _selectedTitleBlock;
            set
            {
                _selectedTitleBlock = value;
                OnPropertyChanged();
            }
        }

        public List<ComboboxItem> ViewportTypes { get; private set; }
        
        private ComboboxItem _selectedViewportType;
        public ComboboxItem SelectedViewportType
        {
            get => _selectedViewportType;
            set
            {
                _selectedViewportType = value;
                OnPropertyChanged();
            }
        }
        
        private ViewTemplateItem _selectedTemplate;
        public ViewTemplateItem SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                _selectedTemplate = value;
                Settings.SelectedViewTemplateId = value?.Id ?? ElementId.InvalidElementId;
                OnPropertyChanged();
            }
        }

        public string NamePrefix
        {
            get => Settings.NamePrefix;
            set
            {
                Settings.NamePrefix = value;
                OnPropertyChanged();
            }
        }

        private string _statusText = DevelopmentTools.Core.LanguageManager.Instance["Elevation_StatusReady"];
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private string _selectedElementText = DevelopmentTools.Core.LanguageManager.Instance["Elevation_NoElement"];
        public string SelectedElementText
        {
            get => _selectedElementText;
            set { _selectedElementText = value; OnPropertyChanged(); }
        }

        private bool _isFloorMode = true;
        public bool IsFloorMode
        {
            get => _isFloorMode;
            set
            {
                _isFloorMode = value;
                if (value)
                {
                    Settings.SourceMode = SourceMode.Floor;
                    SelectedElementText = SelectedFloors.Count > 0 ? "已選取 " + SelectedFloors.Count + " 個幾何元素" : DevelopmentTools.Core.LanguageManager.Instance["Elevation_NoFloor"];
                    var firstFloor = SelectedFloors.FirstOrDefault(e => e is Floor) as Floor;
                    if (firstFloor != null)
                    {
                        string roomNum = DetectRoomNumber(firstFloor);
                        NamePrefix = !string.IsNullOrEmpty(roomNum) ? $"TE_{roomNum}" : "TE";
                    }
                }
                OnPropertyChanged();
            }
        }

        public bool IsWallMode
        {
            get => !_isFloorMode;
            set
            {
                _isFloorMode = !value;
                if (value)
                {
                    Settings.SourceMode = SourceMode.Wall;
                    SelectedElementText = SelectedWalls.Count > 0 ? $"{SelectedWalls.Count}" + DevelopmentTools.Core.LanguageManager.Instance["Elevation_WallsSelectedSuffix"] : DevelopmentTools.Core.LanguageManager.Instance["Elevation_NoWalls"];
                    if (SelectedWalls.Count > 0)
                    {
                        string roomNum = DetectRoomNumber(SelectedWalls);
                        NamePrefix = !string.IsNullOrEmpty(roomNum) ? $"TE_{roomNum}" : "TE";
                    }
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFloorMode));
            }
        }

        // 用於儲存 Revit 選定物件
        public List<Element> SelectedFloors { get; private set; } = new List<Element>();
        public List<Wall> SelectedWalls { get; private set; } = new List<Wall>();

        // 暫存中間產物
        private List<WallElevationData> _tempWallDataList = new List<WallElevationData>();
        private List<ViewSection> _tempCreatedViews = new List<ViewSection>();
        private System.Collections.Generic.Dictionary<ElementId, WallElevationData> _viewToDataMap = new System.Collections.Generic.Dictionary<ElementId, WallElevationData>();

        // 步驟是否完成的狀態 flags
        private bool _isStep1Ok = false;
        private bool _isStep2Ok = false;
        private bool _isStep3Ok = false;
        private bool _isStep4Ok = false;
        private bool _isStep5Ok = false;

        public bool IsStep2Enabled => _isStep1Ok;
        public bool IsStep3Enabled => _isStep2Ok;
        public bool IsStep4Enabled => _isStep2Ok;
        public bool IsStep5Enabled => _isStep2Ok && _tempCreatedViews.Count > 0;

        private void RefreshStepButtons()
        {
            OnPropertyChanged(nameof(IsStep2Enabled));
            OnPropertyChanged(nameof(IsStep3Enabled));
            OnPropertyChanged(nameof(IsStep4Enabled));
            OnPropertyChanged(nameof(IsStep5Enabled));
        }

        public bool IsConfirmed { get; private set; } = false;

        // 用於控制視窗顯示/隱藏的委派事件
        public Action RequestHide { get; set; }
        public Action RequestShow { get; set; }
        public Action RequestClose { get; set; }

        public ICommand SelectSourceCommand { get; }
        public ICommand GenerateCommand { get; }
        public ICommand OpenHelpCommand { get; }
        
        public ICommand Step1AnalyzeCommand { get; }
        public ICommand Step2CreateCommand { get; }
        public ICommand Step3ApplyTemplateCommand { get; }
        public ICommand Step4RenameCommand { get; }
        public ICommand Step5CreateSheetCommand { get; }
        public ICommand CloseWindowCommand { get; }

        public DT_TileElevationGeneratorViewModel(ExternalCommandData commandData)
        {
            _commandData = commandData;
            _uidoc = commandData.Application.ActiveUIDocument;
            _doc = _uidoc.Document;

            _externalEventHandler = new GeneratorExternalEventHandler(this);
            _externalEvent = ExternalEvent.Create(_externalEventHandler);

            // 1. 取得 Section View Templates 並包裝為 ViewTemplateItem
            var templates = ViewTemplateSelector.GetSectionViewTemplates(_doc);
            var items = new List<ViewTemplateItem>();
            items.Add(new ViewTemplateItem { View = null }); // 插入無樣板選項
            foreach (var view in templates)
            {
                items.Add(new ViewTemplateItem { View = view });
            }
            ViewTemplates = items;
            _selectedTemplate = items[0];
            Settings.SelectedViewTemplateId = _selectedTemplate.Id;

            // 載入圖框類型
            var tbs = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .OrderBy(x => x.Name)
                .ToList();

            var tbItems = new List<ComboboxItem>();
            tbItems.Add(new ComboboxItem(ElementId.InvalidElementId, "<無圖框 - 建立空白圖紙>"));
            foreach (var tb in tbs)
            {
                tbItems.Add(new ComboboxItem(tb.Id, $"{tb.FamilyName}: {tb.Name}"));
            }
            TitleBlocks = tbItems;
            _selectedTitleBlock = tbItems.FirstOrDefault();

            // 載入視埠類型 (Viewport Types)
            var vps = new FilteredElementCollector(_doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .Where(x => (x.FamilyName != null && (x.FamilyName.Equals("Viewport", StringComparison.OrdinalIgnoreCase) 
                                                  || x.FamilyName.Equals("視埠", StringComparison.OrdinalIgnoreCase)
                                                  || x.FamilyName.Equals("視口", StringComparison.OrdinalIgnoreCase)))
                         || (x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Viewports))
                .OrderBy(x => x.Name)
                .ToList();

            var vpItems = new List<ComboboxItem>();
            foreach (var vp in vps)
            {
                vpItems.Add(new ComboboxItem(vp.Id, vp.Name));
            }
            ViewportTypes = vpItems;
            _selectedViewportType = vpItems.FirstOrDefault();

            // 2. 初始化 Commands
            SelectSourceCommand = new RelayCommand(OnSelectSource);
            GenerateCommand = new RelayCommand(OnGenerate);
            OpenHelpCommand = new RelayCommand(OnOpenHelp);
            
            Step1AnalyzeCommand = new RelayCommand(OnStep1Analyze);
            Step2CreateCommand = new RelayCommand(OnStep2Create);
            Step3ApplyTemplateCommand = new RelayCommand(OnStep3ApplyTemplate);
            Step4RenameCommand = new RelayCommand(OnStep4Rename);
            Step5CreateSheetCommand = new RelayCommand(OnStep5CreateSheet);
            CloseWindowCommand = new RelayCommand(OnCloseWindow);
        }

        private void OnSelectSource()
        {
            try
            {
                // 隱藏設定視窗
                RequestHide?.Invoke();

                if (IsFloorMode)
                {
                    StatusText = DevelopmentTools.Core.LanguageManager.Instance["Elevation_PromptSelectFloor"];
                    var references = _uidoc.Selection.PickObjects(ObjectType.Element, "Select Floor(s) and any intersecting trench elements");
                    if (references != null && references.Count > 0)
                    {
                        SelectedFloors.Clear();
                        foreach (var reference in references)
                        {
                            var elem = _doc.GetElement(reference);
                            if (elem != null)
                            {
                                SelectedFloors.Add(elem);
                            }
                        }
                        SelectedElementText = "已選取 " + SelectedFloors.Count + " 個幾何元素";
                        StatusText = DevelopmentTools.Core.LanguageManager.Instance["Elevation_FloorSuccess"];
                        
                        // 自動偵測房號 (取第一個有效樓板)
                        var firstFloor = SelectedFloors.FirstOrDefault(e => e is Floor) as Floor;
                        if (firstFloor != null)
                        {
                            string roomNum = DetectRoomNumber(firstFloor);
                            NamePrefix = !string.IsNullOrEmpty(roomNum) ? $"TE_{roomNum}" : "TE";
                        }
                    }
                }
                else
                {
                    StatusText = DevelopmentTools.Core.LanguageManager.Instance["Elevation_PromptSelectWalls"];
                    var references = _uidoc.Selection.PickObjects(ObjectType.Element, new WallSelectionFilter(), "Select multiple Wall elements");
                    if (references != null && references.Count > 0)
                    {
                        SelectedWalls.Clear();
                        foreach (var reference in references)
                        {
                            var wall = _doc.GetElement(reference) as Wall;
                            if (wall != null)
                            {
                                SelectedWalls.Add(wall);
                            }
                        }
                        SelectedElementText = $"{SelectedWalls.Count}" + DevelopmentTools.Core.LanguageManager.Instance["Elevation_WallsSelectedSuffix"];
                        StatusText = DevelopmentTools.Core.LanguageManager.Instance["Elevation_WallsSuccess"];
                        
                        // 自動偵測房號
                        string roomNum = DetectRoomNumber(SelectedWalls);
                        NamePrefix = !string.IsNullOrEmpty(roomNum) ? $"TE_{roomNum}" : "TE";
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                StatusText = DevelopmentTools.Core.LanguageManager.Instance["Elevation_SelectionCancelled"];
            }
            catch (Exception ex)
            {
                StatusText = DevelopmentTools.Core.LanguageManager.Instance["Elevation_SelectionError"] + ex.Message;
            }
            finally
            {
                RequestShow?.Invoke();
            }
        }

        private void OnGenerate()
        {
            _externalEventHandler.SetAction(() =>
            {
                try
                {
                    StatusText = "開始一鍵自動生成流程...";

                    // 1. 分析幾何
                    _tempWallDataList.Clear();
                    _tempCreatedViews.Clear();
                    _isStep1Ok = false;
                    _isStep2Ok = false;
                    _isStep3Ok = false;
                    _isStep4Ok = false;

                    if (IsFloorMode)
                    {
                        if (SelectedFloors.Count == 0)
                        {
                            MessageBox.Show("請先選擇至少一個地板或實體元件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        _tempWallDataList = WallElevationDataBuilder.BuildDataFromFloorsAndSolids(_doc, SelectedFloors, Settings);
                    }
                    else
                    {
                        if (SelectedWalls.Count == 0)
                        {
                            MessageBox.Show("請先選取至少一面牆元件 (Walls)！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        XYZ referenceCenter = GetWallsAverageCenter(SelectedWalls);
                        _tempWallDataList = WallElevationDataBuilder.BuildData(_doc, SelectedWalls, Settings, referenceCenter);
                    }

                    if (_tempWallDataList.Count == 0)
                      {
                        StatusText = "[步驟 1 失敗] 未分析出任何有效的幾何邊界！";
                        RefreshStepButtons();
                        return;
                    }
                    _isStep1Ok = true;

                    // 2. 建立視圖
                    using (var tx = new Transaction(_doc, "DT: Create Section Views"))
                    {
                        tx.Start();
                        _viewToDataMap.Clear();
                        foreach (var wallData in _tempWallDataList)
                        {
                            string tempName = $"DT_TempElevation_{Guid.NewGuid().ToString().Substring(0, 8)}";
                            var view = WallElevationViewCreator.CreateElevationView(_doc, wallData, Settings, tempName);
                            if (view != null)
                            {
                                _tempCreatedViews.Add(view);
                                _viewToDataMap[view.Id] = wallData;
                            }
                        }
                        tx.Commit();
                        _isStep2Ok = true;
                    }

                    // 3. 套用樣板
                    ElementId templateId = SelectedTemplate?.Id ?? ElementId.InvalidElementId;
                    if (templateId != ElementId.InvalidElementId)
                    {
                        using (var tx = new Transaction(_doc, "DT: Apply View Templates"))
                        {
                            tx.Start();
                            foreach (var view in _tempCreatedViews)
                            {
                                view.ViewTemplateId = templateId;
                            }
                            tx.Commit();
                            _isStep3Ok = true;
                        }
                    }
                    else
                    {
                        _isStep3Ok = true;
                    }

                    // 4. 重新命名
                    using (var tx = new Transaction(_doc, "DT: Rename Section Views"))
                    {
                        tx.Start();
                        int count = 0;
                        for (int i = 0; i < _tempCreatedViews.Count; i++)
                        {
                            var view = _tempCreatedViews[i];
                            string finalName = ElevationNamingService.GenerateViewName(_doc, NamePrefix, i);
                            view.Name = finalName;
                            count++;
                        }
                        tx.Commit();
                        _isStep4Ok = true;
                    }

                    // 5. 建立圖紙並置入視圖
                    using (var tx = new Transaction(_doc, "DT: Create Sheet and Place Viewports"))
                    {
                        tx.Start();

                        ElementId tbId = SelectedTitleBlock?.Id ?? ElementId.InvalidElementId;
                        ViewSheet sheet = ViewSheet.Create(_doc, tbId);

                        if (sheet == null)
                        {
                            throw new InvalidOperationException("無法建立圖紙。");
                        }

                        string sheetNum = GenerateUniqueSheetNumber(_doc);
                        sheet.SheetNumber = sheetNum;
                        sheet.Name = $"磁磚展開圖_{NamePrefix}";

                        double xCurrent = 0.5; // feet (圖紙左邊起始邊界)
                        double yPos = 1.0;     // feet (圖紙基準高度，樓板完成面對齊線)

                        int placedCount = 0;
                        for (int i = 0; i < _tempCreatedViews.Count; i++)
                        {
                            var view = _tempCreatedViews[i];
                            WallElevationData data = null;
                            _viewToDataMap.TryGetValue(view.Id, out data);

                            if (Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id))
                            {
                                // 1. 暫存原先註解隱藏狀態，並將其隱藏以取得乾淨的裁剪區 Outline
                                bool origAnnHidden = view.AreAnnotationCategoriesHidden;
                                view.AreAnnotationCategoriesHidden = true;

                                // 2. 在臨時位置建立 Viewport，以讀取 Outline
                                Viewport vp = Viewport.Create(_doc, sheet.Id, view.Id, new XYZ(0, 0, 0));
                                if (SelectedViewportType != null && SelectedViewportType.Id != ElementId.InvalidElementId)
                                {
                                    vp.ChangeTypeId(SelectedViewportType.Id);
                                }
                                Outline cropBoxOutline = vp.GetBoxOutline();

                                double w = cropBoxOutline.MaximumPoint.X - cropBoxOutline.MinimumPoint.X;
                                double h = cropBoxOutline.MaximumPoint.Y - cropBoxOutline.MinimumPoint.Y;

                                // 3. 計算樓高對齊的 Y 偏移量
                                double scale = (double)view.Scale;
                                if (scale <= 0) scale = 50.0;

                                double yFloorOffset = 0.0;
                                if (data != null)
                                {
                                    double originZ = data.MidPoint.Z + data.WallHeight / 2.0;
                                    yFloorOffset = (data.LevelElevation - originZ) / scale;
                                }

                                // 4. 精確設定 Viewport BoxCenter
                                double xCenter = xCurrent + (w / 2.0);
                                double yCenter = yPos - yFloorOffset;
                                vp.SetBoxCenter(new XYZ(xCenter, yCenter, 0));

                                // 5. 恢復視圖註解狀態
                                view.AreAnnotationCategoriesHidden = origAnnHidden;

                                xCurrent += w;
                                placedCount++;
                            }
                        }

                        tx.Commit();
                        _isStep5Ok = true;
                        StatusText = $"[一鍵產生成功] 成功建立圖紙 {sheetNum} 並放置了 {placedCount} 個展開圖剖面！";
                        RefreshStepButtons();
                        MessageBox.Show($"展開圖與圖紙建置成功！\n圖紙編號：{sheetNum}\n共放置了 {placedCount} 個視圖。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"一鍵生成錯誤: {ex.Message}";
                    RefreshStepButtons();
                }
            });

            _externalEvent.Raise();
        }

        // --- 逐步製作分解方法 ---
        private void OnStep1Analyze()
        {
            try
            {
                _tempWallDataList.Clear();
                _tempCreatedViews.Clear();
                _isStep1Ok = false;
                _isStep2Ok = false;
                _isStep3Ok = false;
                _isStep4Ok = false;

                if (IsFloorMode)
                {
                    if (SelectedFloors.Count == 0)
                    {
                        MessageBox.Show("請先選擇至少一個地板或實體元件！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    _tempWallDataList = WallElevationDataBuilder.BuildDataFromFloorsAndSolids(_doc, SelectedFloors, Settings);
                }
                else
                {
                    if (SelectedWalls.Count == 0)
                    {
                        MessageBox.Show("請先選取至少一面牆元件 (Walls)！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    XYZ referenceCenter = GetWallsAverageCenter(SelectedWalls);
                    _tempWallDataList = WallElevationDataBuilder.BuildData(_doc, SelectedWalls, Settings, referenceCenter);
                }

                if (_tempWallDataList.Count == 0)
                {
                    StatusText = "[步驟 1 失敗] 未分析出 any 有效的幾何邊界！";
                    RefreshStepButtons();
                    return;
                }

                _isStep1Ok = true;
                StatusText = $"[步驟 1 成功] 成功分析出 {_tempWallDataList.Count} 條邊界定位線幾何數據。請執行 [步驟 2]。";
                RefreshStepButtons();
            }
            catch (Exception ex)
            {
                StatusText = $"[步驟 1 錯誤] 分析幾何失敗: {ex.Message}";
                RefreshStepButtons();
            }
        }

        private void OnStep2Create()
        {
            if (!_isStep1Ok || _tempWallDataList.Count == 0) return;

            _externalEventHandler.SetAction(() =>
            {
                using (var tx = new Transaction(_doc, "DT: Create Section Views"))
                {
                    try
                    {
                        tx.Start();
                        _tempCreatedViews.Clear();
                        _viewToDataMap.Clear();

                        foreach (var wallData in _tempWallDataList)
                        {
                            string tempName = $"DT_TempElevation_{Guid.NewGuid().ToString().Substring(0, 8)}";
                            var view = WallElevationViewCreator.CreateElevationView(_doc, wallData, Settings, tempName);
                            if (view != null)
                            {
                                _tempCreatedViews.Add(view);
                                _viewToDataMap[view.Id] = wallData;
                            }
                        }

                        tx.Commit();
                        _isStep2Ok = true;
                        StatusText = $"[步驟 2 成功] 成功建立 {_tempCreatedViews.Count} 個剖面視圖。請執行 [步驟 3] 或 [步驟 4]。";
                        RefreshStepButtons();
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        StatusText = $"[步驟 2 錯誤] 建立視圖失敗: {ex.Message}";
                        RefreshStepButtons();
                    }
                }
            });

            _externalEvent.Raise();
        }

        private void OnStep3ApplyTemplate()
        {
            if (!_isStep2Ok || _tempCreatedViews.Count == 0) return;

            _externalEventHandler.SetAction(() =>
            {
                using (var tx = new Transaction(_doc, "DT: Apply View Templates"))
                {
                    try
                    {
                        tx.Start();

                        ElementId templateId = SelectedTemplate?.Id ?? ElementId.InvalidElementId;
                        if (templateId == ElementId.InvalidElementId)
                        {
                            StatusText = "[步驟 3 提示] 未選取任何視圖樣板，已略過套用步驟。";
                            tx.Commit();
                            _isStep3Ok = true;
                            RefreshStepButtons();
                            return;
                        }

                        int count = 0;
                        foreach (var view in _tempCreatedViews)
                        {
                            view.ViewTemplateId = templateId;
                            count++;
                        }

                        tx.Commit();
                        _isStep3Ok = true;
                        StatusText = $"[步驟 3 成功] 已套用樣板「{SelectedTemplate.Name}」至 {count} 個剖面視圖。";
                        RefreshStepButtons();
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        StatusText = $"[步驟 3 錯誤] 套用樣板失敗: {ex.Message}";
                        RefreshStepButtons();
                    }
                }
            });

            _externalEvent.Raise();
        }

        private void OnStep4Rename()
        {
            if (!_isStep2Ok || _tempCreatedViews.Count == 0) return;

            _externalEventHandler.SetAction(() =>
            {
                using (var tx = new Transaction(_doc, "DT: Rename Section Views"))
                {
                    try
                    {
                        tx.Start();

                        int count = 0;
                        for (int i = 0; i < _tempCreatedViews.Count; i++)
                        {
                            var view = _tempCreatedViews[i];
                            string finalName = ElevationNamingService.GenerateViewName(_doc, NamePrefix, i);
                            view.Name = finalName;
                            count++;
                        }

                        tx.Commit();
                        _isStep4Ok = true;
                        StatusText = $"[步驟 4 成功] 成功命名 {count} 個剖面視圖！展開圖建置工作已完全結束。";
                        RefreshStepButtons();

                        MessageBox.Show($"展開圖建置成功！\n共生成並命名了 {count} 個剖面視圖。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        StatusText = $"[步驟 4 錯誤] 視圖命名失敗: {ex.Message}";
                        RefreshStepButtons();
                    }
                }
            });

            _externalEvent.Raise();
        }

        private void OnStep5CreateSheet()
        {
            if (!_isStep2Ok || _tempCreatedViews.Count == 0) return;

            _externalEventHandler.SetAction(() =>
            {
                using (var tx = new Transaction(_doc, "DT: Create Sheet and Place Viewports"))
                {
                    try
                    {
                        tx.Start();

                        ElementId tbId = SelectedTitleBlock?.Id ?? ElementId.InvalidElementId;
                        ViewSheet sheet = ViewSheet.Create(_doc, tbId);

                        if (sheet == null)
                        {
                            throw new InvalidOperationException("無法建立圖紙。");
                        }

                        string sheetNum = GenerateUniqueSheetNumber(_doc);
                        sheet.SheetNumber = sheetNum;
                        sheet.Name = $"磁磚展開圖_{NamePrefix}";

                        double xCurrent = 0.5; // feet (圖紙左邊起始邊界)
                        double yPos = 1.0;     // feet (圖紙基準高度，樓板完成面對齊線)

                        int placedCount = 0;
                        for (int i = 0; i < _tempCreatedViews.Count; i++)
                        {
                            var view = _tempCreatedViews[i];
                            WallElevationData data = null;
                            _viewToDataMap.TryGetValue(view.Id, out data);

                            if (Viewport.CanAddViewToSheet(_doc, sheet.Id, view.Id))
                            {
                                // 1. 暫存原先註解隱藏狀態，並將其隱藏以取得乾淨的裁剪區 Outline
                                bool origAnnHidden = view.AreAnnotationCategoriesHidden;
                                view.AreAnnotationCategoriesHidden = true;

                                // 2. 在臨時位置建立 Viewport，以讀取 Outline
                                Viewport vp = Viewport.Create(_doc, sheet.Id, view.Id, new XYZ(0, 0, 0));
                                if (SelectedViewportType != null && SelectedViewportType.Id != ElementId.InvalidElementId)
                                {
                                    vp.ChangeTypeId(SelectedViewportType.Id);
                                }
                                Outline cropBoxOutline = vp.GetBoxOutline();

                                double w = cropBoxOutline.MaximumPoint.X - cropBoxOutline.MinimumPoint.X;
                                double h = cropBoxOutline.MaximumPoint.Y - cropBoxOutline.MinimumPoint.Y;

                                // 3. 計算樓高對齊的 Y 偏移量
                                double scale = (double)view.Scale;
                                if (scale <= 0) scale = 50.0;

                                double yFloorOffset = 0.0;
                                if (data != null)
                                {
                                    double originZ = data.MidPoint.Z + data.WallHeight / 2.0;
                                    yFloorOffset = (data.LevelElevation - originZ) / scale;
                                }

                                // 4. 精確設定 Viewport BoxCenter
                                double xCenter = xCurrent + (w / 2.0);
                                double yCenter = yPos - yFloorOffset;
                                vp.SetBoxCenter(new XYZ(xCenter, yCenter, 0));

                                // 5. 恢復視圖註解狀態
                                view.AreAnnotationCategoriesHidden = origAnnHidden;

                                xCurrent += w;
                                placedCount++;
                            }
                        }

                        tx.Commit();
                        _isStep5Ok = true;
                        StatusText = $"[步驟 5 成功] 已建立圖紙 {sheetNum}，並放置了 {placedCount} 個剖面視圖。";
                        RefreshStepButtons();
                        MessageBox.Show($"圖紙建置成功！\n圖紙編號：{sheetNum}\n共放置了 {placedCount} 個視圖。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        StatusText = $"[步驟 5 錯誤] 建立圖紙失敗: {ex.Message}";
                        RefreshStepButtons();
                    }
                }
            });

            _externalEvent.Raise();
        }

        private string GenerateUniqueSheetNumber(Document doc)
        {
            var sheetNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>();
            foreach (var s in sheets)
            {
                sheetNumbers.Add(s.SheetNumber);
            }

            int index = 101;
            while (true)
            {
                string num = $"T-{index}";
                if (!sheetNumbers.Contains(num))
                {
                    return num;
                }
                index++;
            }
        }

        private void OnCloseWindow()
        {
            IsConfirmed = true;
            RequestClose?.Invoke();
        }

        private void OnOpenHelp()
        {
            TaskDialog td = new TaskDialog("展開圖生成器 - 使用指南");
            td.MainInstruction = "磁磚展開圖生成器 新手快速入門";
            td.MainContent = "本工具專為快速建立室內空間牆面之磁磚展開圖設計，提供以下兩種來源模式：\n\n" +
                             "1. Floor 模式 (自動相鄰牆模式)：\n" +
                             "   - 選擇此模式並點選「選取 Revit 目標物件」，在 Revit 視圖中選取一個地板元件 (Floor)。\n" +
                             "   - 系統將自動抓取與該地板相鄰的牆面，以地板中心為原點進行「順時針排序」，並依序以 A、B、C、D 後綴進行展開圖命名 (如 TE_101_A, TE_101_B)。\n\n" +
                             "2. Wall 模式 (手動多選牆模式)：\n" +
                             "   - 選擇此模式並點選「選取 Revit 目標物件」，在 Revit 中多選幾面牆體，按右上角「完成」儲存。\n\n" +
                             "3. 參數說明：\n" +
                             "   - View Template: 生成展開圖剖面時自動套用的視圖樣板。\n" +
                             "   - View Depth: 剖切深度（向牆體內部看的深度，預設 600mm）。\n" +
                             "   - Wall Offset: 剖切線位置在牆體表面前方的內縮距離 (預設 30mm)。\n" +
                             "   - Bottom Offset: 展開圖裁剪框底部向下拉的距離 (如考慮裝修高差或地磚，預設 0mm)。\n" +
                             "   - Name Prefix: 展開圖剖面命名首碼 (如輸入房間編號 TE_101，生成 TE_101_A 等)。\n\n" +
                             "設定完成後，點擊下方「一鍵產生磁磚展開圖」即可自動生成！";
            td.Show();
        }

        // --- 房號自動偵測輔助函式 ---
        private string DetectRoomNumber(Floor floor)
        {
            if (floor == null) return null;
            try
            {
                XYZ floorCenter = GetFloorCenter(floor);
                XYZ checkPoint = new XYZ(floorCenter.X, floorCenter.Y, floorCenter.Z + 2.0); // 向上拉高 2 呎
                return GetRoomNumberAtPoint(checkPoint);
            }
            catch
            {
                return null;
            }
        }

        private string DetectRoomNumber(List<Wall> walls)
        {
            if (walls == null || walls.Count == 0) return null;
            try
            {
                var wall = walls[0];
                var wallLoc = wall.Location as LocationCurve;
                if (wallLoc == null || wallLoc.Curve == null) return null;
                XYZ mid = wallLoc.Curve.Evaluate(0.5, true);
                
                XYZ averageCenter = GetWallsAverageCenter(walls);
                XYZ toCenter = new XYZ(averageCenter.X - mid.X, averageCenter.Y - mid.Y, 0).Normalize();
                
                XYZ checkPoint = mid + toCenter * 2.0;
                checkPoint = new XYZ(checkPoint.X, checkPoint.Y, mid.Z + 2.0);
                return GetRoomNumberAtPoint(checkPoint);
            }
            catch
            {
                return null;
            }
        }

        private string GetRoomNumberAtPoint(XYZ pt)
        {
            try
            {
                Room room = _doc.GetRoomAtPoint(pt);
                if (room != null) return room.Number;

                var roomCollector = new FilteredElementCollector(_doc)
                    .OfClass(typeof(SpatialElement))
                    .OfCategory(BuiltInCategory.OST_Rooms);

                foreach (SpatialElement elem in roomCollector)
                {
                    if (elem is Room r && r.Area > 0 && r.IsPointInRoom(pt))
                    {
                        return r.Number;
                    }
                }
            }
            catch
            {
                // ignored
            }
            return null;
        }

        private static XYZ GetFloorCenter(Floor floor)
        {
            var bbox = floor.get_BoundingBox(null);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) / 2.0;
            }
            var loc = floor.Location as LocationPoint;
            if (loc != null) return loc.Point;
            return XYZ.Zero;
        }

        private static XYZ GetWallsAverageCenter(List<Wall> walls)
        {
            double sumX = 0, sumY = 0, sumZ = 0;
            int count = 0;
            foreach (var wall in walls)
            {
                var wallLoc = wall.Location as LocationCurve;
                if (wallLoc != null && wallLoc.Curve != null)
                {
                    XYZ mid = (wallLoc.Curve.GetEndPoint(0) + wallLoc.Curve.GetEndPoint(1)) / 2.0;
                    sumX += mid.X;
                    sumY += mid.Y;
                    sumZ += mid.Z;
                    count++;
                }
            }
            if (count == 0) return XYZ.Zero;
            return new XYZ(sumX / count, sumY / count, sumZ / count);
        }

        // --- INotifyPropertyChanged 實作 ---
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private class WallSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Wall;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
        private class FloorSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }

    // --- Revit API Context 執行緒事件代理 ---
    public class GeneratorExternalEventHandler : IExternalEventHandler
    {
        private readonly DT_TileElevationGeneratorViewModel _viewModel;
        private Action _action;

        public GeneratorExternalEventHandler(DT_TileElevationGeneratorViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public void SetAction(Action action)
        {
            _action = action;
        }

        public void Execute(UIApplication app)
        {
            if (_action != null)
            {
                try
                {
                    _action.Invoke();
                }
                catch (Exception ex)
                {
                    _viewModel.StatusText = $"執行失敗: {ex.Message}";
                }
            }
        }

        public string GetName() => "TileElevationGeneratorExternalEvent";
    }

    // --- RelayCommand 簡化版 ---
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
