using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OpeningDetailAddin.Models;
using OpeningDetailAddin.Services;

namespace OpeningDetailAddin.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly UIApplication _uiApp;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        private List<TitleBlockModel> _allTitleBlocks;

        // 1. 圖框雙層下拉選單：族群 與 類型
        public ObservableCollection<string> TitleBlockFamilyNames { get; set; }
        public ObservableCollection<TitleBlockModel> FilteredTitleBlockTypes { get; set; }

        private string _selectedTitleBlockFamily;
        public string SelectedTitleBlockFamily
        {
            get { return _selectedTitleBlockFamily; }
            set 
            { 
                _selectedTitleBlockFamily = value; 
                OnPropertyChanged("SelectedTitleBlockFamily"); 
                UpdateTitleBlockTypes();
            }
        }

        private TitleBlockModel _selectedTitleBlock;
        public TitleBlockModel SelectedTitleBlock
        {
            get { return _selectedTitleBlock; }
            set { _selectedTitleBlock = value; OnPropertyChanged("SelectedTitleBlock"); }
        }

        // 2. 視埠調整下拉選單 (視埠標題類型)
        public ObservableCollection<ViewportTypeModel> ViewportTypes { get; set; }

        private ViewportTypeModel _selectedViewportType;
        public ViewportTypeModel SelectedViewportType
        {
            get { return _selectedViewportType; }
            set { _selectedViewportType = value; OnPropertyChanged("SelectedViewportType"); }
        }

        // 3. 圖紙排版佈局下拉選單
        public ObservableCollection<string> LayoutModes { get; set; }

        private string _selectedLayoutMode;
        public string SelectedLayoutMode
        {
            get { return _selectedLayoutMode; }
            set { _selectedLayoutMode = value; OnPropertyChanged("SelectedLayoutMode"); }
        }

        // 4. 平面索引圖裁剪控制下拉選單 (解決塞不進圖紙問題)
        public ObservableCollection<string> CropModes { get; set; }

        private string _selectedCropMode;
        public string SelectedCropMode
        {
            get { return _selectedCropMode; }
            set { _selectedCropMode = value; OnPropertyChanged("SelectedCropMode"); }
        }

        // 5. 視圖樣板下拉選單
        public ObservableCollection<ViewTemplateModel> SectionViewTemplates { get; set; }
        public ObservableCollection<ViewTemplateModel> PlanViewTemplates { get; set; }
        public ObservableCollection<ViewTemplateModel> View3DViewTemplates { get; set; }

        private ViewTemplateModel _selectedSectionViewTemplate;
        public ViewTemplateModel SelectedSectionViewTemplate
        {
            get { return _selectedSectionViewTemplate; }
            set { _selectedSectionViewTemplate = value; OnPropertyChanged("SelectedSectionViewTemplate"); }
        }

        private ViewTemplateModel _selectedPlanViewTemplate;
        public ViewTemplateModel SelectedPlanViewTemplate
        {
            get { return _selectedPlanViewTemplate; }
            set { _selectedPlanViewTemplate = value; OnPropertyChanged("SelectedPlanViewTemplate"); }
        }

        private ViewTemplateModel _selectedView3DViewTemplate;
        public ViewTemplateModel SelectedView3DViewTemplate
        {
            get { return _selectedView3DViewTemplate; }
            set { _selectedView3DViewTemplate = value; OnPropertyChanged("SelectedView3DViewTemplate"); }
        }

        // 6. 開口元件篩選
        public ObservableCollection<string> AvailableFamilyNames { get; set; }
        public ObservableCollection<string> AvailableTypeNames { get; set; }

        private string _selectedFamilyName;
        public string SelectedFamilyName
        {
            get { return _selectedFamilyName; }
            set 
            { 
                _selectedFamilyName = value; 
                OnPropertyChanged("SelectedFamilyName"); 
                UpdateTypeNames();
                ScanOpenings();
            }
        }

        private string _selectedTypeName;
        public string SelectedTypeName
        {
            get { return _selectedTypeName; }
            set 
            { 
                _selectedTypeName = value; 
                OnPropertyChanged("SelectedTypeName"); 
                ScanOpenings();
            }
        }

        // 7. 階層式統計
        public ObservableCollection<LevelGroup> LevelGroups { get; set; }

        private int _sectionScale;
        public int SectionScale
        {
            get { return _sectionScale; }
            set { _sectionScale = value; OnPropertyChanged("SectionScale"); }
        }

        private int _keyPlanScale;
        public int KeyPlanScale
        {
            get { return _keyPlanScale; }
            set { _keyPlanScale = value; OnPropertyChanged("KeyPlanScale"); }
        }

        private int _view3DScale;
        public int View3DScale
        {
            get { return _view3DScale; }
            set { _view3DScale = value; OnPropertyChanged("View3DScale"); }
        }

        private double _offsetMm;
        public double OffsetMm
        {
            get { return _offsetMm; }
            set { _offsetMm = value; OnPropertyChanged("OffsetMm"); }
        }

        private string _sheetNumberPrefix;
        public string SheetNumberPrefix
        {
            get { return _sheetNumberPrefix; }
            set { _sheetNumberPrefix = value; OnPropertyChanged("SheetNumberPrefix"); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get { return _statusMessage; }
            set { _statusMessage = value; OnPropertyChanged("StatusMessage"); }
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get { return _isProcessing; }
            set { _isProcessing = value; OnPropertyChanged("IsProcessing"); }
        }

        private bool _skipExisting = true;
        public bool SkipExisting
        {
            get { return _skipExisting; }
            set 
            { 
                _skipExisting = value; 
                OnPropertyChanged("SkipExisting"); 
            }
        }

        public ICommand RefreshScanCommand { get; private set; }
        public ICommand SelectAllCommand { get; private set; }
        public ICommand DeselectAllCommand { get; private set; }
        public ICommand SelectUnprocessedCommand { get; private set; }
        public ICommand GenerateCommand { get; private set; }

        public Action CloseAction { get; set; }

        public MainViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _uiDoc = uiApp.ActiveUIDocument;
            _doc = _uiDoc.Document;

            _sectionScale = 30;
            _keyPlanScale = 200;
            _view3DScale = 25;
            _offsetMm = 400;
            _sheetNumberPrefix = "OP-";
            _statusMessage = "準備就緒。";

            _allTitleBlocks = new List<TitleBlockModel>();
            TitleBlockFamilyNames = new ObservableCollection<string>();
            FilteredTitleBlockTypes = new ObservableCollection<TitleBlockModel>();

            ViewportTypes = new ObservableCollection<ViewportTypeModel>();
            LayoutModes = new ObservableCollection<string>();
            CropModes = new ObservableCollection<string>();

            SectionViewTemplates = new ObservableCollection<ViewTemplateModel>();
            PlanViewTemplates = new ObservableCollection<ViewTemplateModel>();
            View3DViewTemplates = new ObservableCollection<ViewTemplateModel>();

            AvailableFamilyNames = new ObservableCollection<string>();
            AvailableTypeNames = new ObservableCollection<string>();
            LevelGroups = new ObservableCollection<LevelGroup>();

            RefreshScanCommand = new RelayCommand(delegate(object o) 
            { 
                RefreshProjectSettings();
                ScanOpenings(); 
            });
            SelectAllCommand = new RelayCommand(delegate(object o) { SetAllSelection(true); });
            DeselectAllCommand = new RelayCommand(delegate(object o) { SetAllSelection(false); });
            SelectUnprocessedCommand = new RelayCommand(delegate(object o) { SelectUnprocessedOnly(); });
            GenerateCommand = new RelayCommand(ExecuteGenerate, CanExecuteGenerate);

            LoadInitialData();
        }

        private void LoadInitialData()
        {
            // 1. 讀取圖框
            _allTitleBlocks = SheetService.GetAvailableTitleBlocks(_doc);
            var famNames = _allTitleBlocks
                .Select(delegate(TitleBlockModel tb) { return tb.FamilyName; })
                .Distinct()
                .OrderBy(delegate(string name) { return name; })
                .ToList();

            TitleBlockFamilyNames.Clear();
            foreach (var fn in famNames) TitleBlockFamilyNames.Add(fn);

            string defaultFam = TitleBlockFamilyNames.FirstOrDefault(delegate(string fn) 
            { 
                return fn.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0; 
            }) ?? TitleBlockFamilyNames.FirstOrDefault();

            _selectedTitleBlockFamily = defaultFam;
            OnPropertyChanged("SelectedTitleBlockFamily");
            UpdateTitleBlockTypes();

            // 2. 讀取視埠標題類型 (下拉選單)
            var vpTypes = SheetService.GetAvailableViewportTypes(_doc);
            ViewportTypes.Clear();
            foreach (var vt in vpTypes) ViewportTypes.Add(vt);
            SelectedViewportType = ViewportTypes.Count > 1 ? ViewportTypes[1] : ViewportTypes.FirstOrDefault();

            // 3. 圖紙排版方式下拉選單
            LayoutModes.Clear();
            LayoutModes.Add("✨ 三合一標準版 (左側剖面 + 右上平面索引 + 右下3D視圖)");
            LayoutModes.Add("橫向平鋪 (左側剖面 + 中間3D視圖 + 右側平面索引)");
            SelectedLayoutMode = LayoutModes.FirstOrDefault();

            // 4. 平面索引圖裁剪模式下拉選單 (解決塞不進圖紙問題)
            CropModes.Clear();
            CropModes.Add("🎯 開口局部區域裁剪 (16m×12m，保證塞入A3且清晰呈現)");
            CropModes.Add("🏢 全棟建築外廓裁剪 (緊貼全棟建築外廓，去除遠方雜訊)");
            CropModes.Add("📄 繼承原始樓層視圖裁切");
            SelectedCropMode = CropModes.FirstOrDefault();

            // 5. 讀取視圖樣板 (剖面、索引、3D)
            var secTemplates = ViewTemplateService.GetSectionViewTemplates(_doc);
            SectionViewTemplates.Clear();
            foreach (var st in secTemplates) SectionViewTemplates.Add(st);
            SelectedSectionViewTemplate = SectionViewTemplates.FirstOrDefault();

            var planTemplates = ViewTemplateService.GetPlanViewTemplates(_doc);
            PlanViewTemplates.Clear();
            foreach (var pt in planTemplates) PlanViewTemplates.Add(pt);
            SelectedPlanViewTemplate = PlanViewTemplates.FirstOrDefault();

            var view3DTemplates = ViewTemplateService.Get3DViewTemplates(_doc);
            View3DViewTemplates.Clear();
            foreach (var vt in view3DTemplates) View3DViewTemplates.Add(vt);
            SelectedView3DViewTemplate = View3DViewTemplates.FirstOrDefault();

            // 6. 搜尋專案中宿主為牆的元件族群
            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_MechanicalEquipment
            };

            var wallHostedInstances = new List<FamilyInstance>();
            foreach (var cat in categories)
            {
                var elements = new FilteredElementCollector(_doc)
                    .OfCategory(cat)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(delegate(FamilyInstance fi) { return fi.Host is Wall; });

                wallHostedInstances.AddRange(elements);
            }

            var familyNames = wallHostedInstances
                .Select(delegate(FamilyInstance fi) { return fi.Symbol != null ? fi.Symbol.FamilyName : null; })
                .Where(delegate(string name) { return !string.IsNullOrEmpty(name); })
                .Distinct()
                .OrderBy(delegate(string name) { return name; })
                .ToList();

            AvailableFamilyNames.Clear();
            AvailableFamilyNames.Add("全部族群");
            foreach (var fn in familyNames) AvailableFamilyNames.Add(fn);

            string matchedFamily = AvailableFamilyNames.FirstOrDefault(delegate(string f) 
            { 
                return f.IndexOf("百葉開口", StringComparison.OrdinalIgnoreCase) >= 0; 
            }) ?? AvailableFamilyNames.FirstOrDefault(delegate(string f) 
            { 
                return f.IndexOf("開口元件", StringComparison.OrdinalIgnoreCase) >= 0; 
            }) ?? AvailableFamilyNames.FirstOrDefault();

            _selectedFamilyName = matchedFamily;
            OnPropertyChanged("SelectedFamilyName");

            UpdateTypeNames();
            ScanOpenings();
        }

        private void UpdateTitleBlockTypes()
        {
            FilteredTitleBlockTypes.Clear();
            if (string.IsNullOrEmpty(SelectedTitleBlockFamily)) return;

            var types = _allTitleBlocks
                .Where(delegate(TitleBlockModel tb) { return tb.FamilyName == SelectedTitleBlockFamily; })
                .OrderBy(delegate(TitleBlockModel tb) { return tb.TypeName; });

            foreach (var t in types) FilteredTitleBlockTypes.Add(t);

            SelectedTitleBlock = FilteredTitleBlockTypes.FirstOrDefault(delegate(TitleBlockModel t) 
            { 
                return t.TypeName.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0; 
            }) ?? FilteredTitleBlockTypes.FirstOrDefault();
        }

        private void UpdateTypeNames()
        {
            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_MechanicalEquipment
            };

            var instances = new List<FamilyInstance>();
            foreach (var cat in categories)
            {
                var elements = new FilteredElementCollector(_doc)
                    .OfCategory(cat)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(delegate(FamilyInstance fi) { return fi.Host is Wall; });

                instances.AddRange(elements);
            }

            if (!string.IsNullOrEmpty(SelectedFamilyName) && SelectedFamilyName != "全部族群")
            {
                instances = instances.Where(delegate(FamilyInstance fi) 
                { 
                    return fi.Symbol != null && fi.Symbol.FamilyName == SelectedFamilyName; 
                }).ToList();
            }

            var typeNames = instances
                .Select(delegate(FamilyInstance fi) { return fi.Symbol != null ? fi.Symbol.Name : null; })
                .Where(delegate(string n) { return !string.IsNullOrEmpty(n); })
                .Distinct()
                .OrderBy(delegate(string n) { return n; })
                .ToList();

            AvailableTypeNames.Clear();
            AvailableTypeNames.Add("全部類型");
            foreach (var tn in typeNames) AvailableTypeNames.Add(tn);

            _selectedTypeName = "全部類型";
            OnPropertyChanged("SelectedTypeName");
        }

        private void RefreshProjectSettings()
        {
            try
            {
                var prevVpId = SelectedViewportType != null ? SelectedViewportType.Id : null;
                var vpTypes = SheetService.GetAvailableViewportTypes(_doc);
                ViewportTypes.Clear();
                foreach (var vt in vpTypes) ViewportTypes.Add(vt);

                ViewportTypeModel matchVp = null;
                if (prevVpId != null)
                {
                    matchVp = ViewportTypes.FirstOrDefault(delegate(ViewportTypeModel m) { return m.Id == prevVpId; });
                }
                SelectedViewportType = matchVp ?? (ViewportTypes.Count > 1 ? ViewportTypes[1] : ViewportTypes.FirstOrDefault());

                // 重新載入視圖樣板清單
                var secTemplates = ViewTemplateService.GetSectionViewTemplates(_doc);
                SectionViewTemplates.Clear();
                foreach (var st in secTemplates) SectionViewTemplates.Add(st);
                if (SelectedSectionViewTemplate == null) SelectedSectionViewTemplate = SectionViewTemplates.FirstOrDefault();

                var planTemplates = ViewTemplateService.GetPlanViewTemplates(_doc);
                PlanViewTemplates.Clear();
                foreach (var pt in planTemplates) PlanViewTemplates.Add(pt);
                if (SelectedPlanViewTemplate == null) SelectedPlanViewTemplate = PlanViewTemplates.FirstOrDefault();

                var view3DTemplates = ViewTemplateService.Get3DViewTemplates(_doc);
                View3DViewTemplates.Clear();
                foreach (var vt in view3DTemplates) View3DViewTemplates.Add(vt);
                if (SelectedView3DViewTemplate == null) SelectedView3DViewTemplate = View3DViewTemplates.FirstOrDefault();
            }
            catch { }
        }

        private void ScanOpenings()
        {
            LevelGroups.Clear();

            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_MechanicalEquipment
            };

            var instances = new List<FamilyInstance>();
            foreach (var cat in categories)
            {
                var elements = new FilteredElementCollector(_doc)
                    .OfCategory(cat)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(delegate(FamilyInstance fi) { return fi.Host is Wall; });

                instances.AddRange(elements);
            }

            if (!string.IsNullOrEmpty(SelectedFamilyName) && SelectedFamilyName != "全部族群")
            {
                instances = instances.Where(delegate(FamilyInstance fi) 
                { 
                    return fi.Symbol != null && fi.Symbol.FamilyName == SelectedFamilyName; 
                }).ToList();
            }

            if (!string.IsNullOrEmpty(SelectedTypeName) && SelectedTypeName != "全部類型")
            {
                instances = instances.Where(delegate(FamilyInstance fi) 
                { 
                    return fi.Symbol != null && fi.Symbol.Name == SelectedTypeName; 
                }).ToList();
            }

            ExistingItemCache cache = ExistingItemDetectorService.BuildCache(_doc);
            var groupedByLevel = instances.GroupBy(delegate(FamilyInstance fi) { return fi.LevelId; });

            foreach (var lvlGroup in groupedByLevel)
            {
                Level level = _doc.GetElement(lvlGroup.Key) as Level;
                LevelGroup levelItem = new LevelGroup();
                levelItem.Level = level;

                var groupedByWall = lvlGroup.GroupBy(delegate(FamilyInstance fi) 
                { 
                    return fi.Host != null ? fi.Host.Id : null; 
                });

                foreach (var wallGrp in groupedByWall)
                {
                    if (wallGrp.Key == null) continue;
                    Wall wall = _doc.GetElement(wallGrp.Key) as Wall;
                    if (wall == null) continue;

                    string wallTypeName = wall.WallType != null ? wall.WallType.Name : wall.Name;
                    double wallThicknessMm = GeometryService.FeetToMm(wall.Width);
                    double wallLengthMm = GeometryService.GetWallLengthMm(wall);
                    double wallHeightMm = GeometryService.GetWallHeightMm(wall);

                    WallGroup wallItem = new WallGroup();
                    wallItem.Wall = wall;
                    wallItem.WallName = wall.Name;
                    wallItem.WallTypeName = wallTypeName;
                    wallItem.WallThicknessMm = wallThicknessMm;
                    wallItem.WallLengthMm = wallLengthMm;
                    wallItem.WallHeightMm = wallHeightMm;
                    wallItem.ParentLevel = levelItem;

                    foreach (var inst in wallGrp)
                    {
                        OpeningDimensionInfo dim = GeometryService.GetOpeningDimensionsMm(inst);
                        double sill = GeometryService.GetOpeningSillHeightMm(inst);
                        LocationPoint lp = inst.Location as LocationPoint;

                        string mark = "OP";
                        Parameter pMark = inst.get_Parameter(BuiltInParameter.DOOR_NUMBER) 
                                          ?? inst.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                        if (pMark != null && !string.IsNullOrEmpty(pMark.AsString()))
                        {
                            mark = pMark.AsString();
                        }

                        ViewSheet existingSheet = ExistingItemDetectorService.FindExistingSheet(_doc, inst.Id, cache);
                        ViewSection existingSec = ExistingItemDetectorService.FindExistingSectionView(_doc, inst.Id, cache);
                        ViewPlan existingKey = ExistingItemDetectorService.FindExistingKeyPlan(_doc, inst.Id, cache);
                        View3D existing3D = ExistingItemDetectorService.FindExisting3DView(_doc, inst.Id, cache);

                        OpeningItem item = new OpeningItem();
                        item.Instance = inst;
                        item.FamilyName = inst.Symbol != null ? inst.Symbol.FamilyName : string.Empty;
                        item.TypeName = inst.Symbol != null ? inst.Symbol.Name : string.Empty;
                        item.Mark = mark;
                        item.LocationPoint = lp != null ? lp.Point : XYZ.Zero;
                        item.WidthMm = dim.WidthMm;
                        item.HeightMm = dim.HeightMm;
                        item.SillHeightMm = sill;
                        item.ParentWall = wallItem;
                        item.HasExistingSheet = (existingSheet != null);
                        item.ExistingSheetNumber = existingSheet != null ? existingSheet.SheetNumber : null;
                        item.HasExistingViews = (existingSec != null || existingKey != null || existing3D != null);

                        // 智慧預選：若已建圖說且啟動「略過已建立項目」，則預設不選取；全新開口則預設勾選
                        bool defaultSelected = true;
                        if (SkipExisting && (item.HasExistingSheet || item.HasExistingViews))
                        {
                            defaultSelected = false;
                        }
                        item.SetIsSelected(defaultSelected, false);

                        item.PropertyChanged += delegate(object s, PropertyChangedEventArgs e)
                        {
                            if (e.PropertyName == "IsSelected")
                            {
                                UpdateSelectionStatus();
                            }
                        };

                        wallItem.Openings.Add(item);
                    }

                    wallItem.VerifyCheckState();
                    levelItem.Walls.Add(wallItem);
                }

                if (levelItem.Walls.Count > 0)
                {
                    levelItem.VerifyCheckState();
                    LevelGroups.Add(levelItem);
                }
            }

            UpdateSelectionStatus();
        }

        private void SetAllSelection(bool isSelected)
        {
            foreach (var lvl in LevelGroups)
            {
                lvl.IsSelected = isSelected;
            }
            UpdateSelectionStatus();
        }

        private void SelectUnprocessedOnly()
        {
            foreach (var lvl in LevelGroups)
            {
                foreach (var wall in lvl.Walls)
                {
                    foreach (var op in wall.Openings)
                    {
                        bool isUnprocessed = !op.HasExistingSheet && !op.HasExistingViews;
                        op.SetIsSelected(isUnprocessed, false);
                    }
                    wall.VerifyCheckState();
                }
                lvl.VerifyCheckState();
            }
            UpdateSelectionStatus();
        }

        private void UpdateSelectionStatus()
        {
            int selectedCount = 0;
            int totalCount = 0;
            foreach (var lvl in LevelGroups)
            {
                foreach (var wall in lvl.Walls)
                {
                    foreach (var op in wall.Openings)
                    {
                        totalCount++;
                        if (op.IsSelected) selectedCount++;
                    }
                }
            }

            StatusMessage = string.Format("共掃描到 {0} 處開孔，目前已勾選 {1} 處準備出圖。", totalCount, selectedCount);
        }

        private bool CanExecuteGenerate(object obj)
        {
            return SelectedTitleBlock != null && LevelGroups.Count > 0 && !IsProcessing;
        }

        private void ExecuteGenerate(object obj)
        {
            if (SelectedTitleBlock == null)
            {
                MessageBox.Show("請先選擇圖框！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int totalSelected = 0;
            foreach (var lvl in LevelGroups)
            {
                foreach (var wall in lvl.Walls)
                {
                    foreach (var op in wall.Openings)
                    {
                        if (op.IsSelected) totalSelected++;
                    }
                }
            }

            if (totalSelected == 0)
            {
                MessageBox.Show("您尚未勾選任何開口！請在上方清單中勾選欲製作圖紙的樓層或開口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProcessing = true;
            int successSheets = 0;

            // 判斷裁剪模式
            string cropModeKey = "Zone";
            if (SelectedCropMode != null && SelectedCropMode.IndexOf("全棟", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cropModeKey = "Building";
            }
            else if (SelectedCropMode != null && SelectedCropMode.IndexOf("原始", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cropModeKey = "Original";
            }

            // 判斷排版模式
            string layoutModeKey = "TriView";
            if (SelectedLayoutMode != null && SelectedLayoutMode.IndexOf("橫向", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                layoutModeKey = "Horizontal";
            }

            ElementId vpTypeId = SelectedViewportType != null ? SelectedViewportType.Id : ElementId.InvalidElementId;

            ExistingItemCache genCache = ExistingItemDetectorService.BuildCache(_doc);
            int skippedSheets = 0;

            using (Transaction trans = new Transaction(_doc, "批次建立開孔剖面、索引圖與3D視圖"))
            {
                trans.Start();

                try
                {
                    foreach (var lvlGroup in LevelGroups)
                    {
                        foreach (var wallGroup in lvlGroup.Walls)
                        {
                            foreach (var opItem in wallGroup.Openings)
                            {
                                if (!opItem.IsSelected) continue;

                                // 0. 若啟用「略過已建立項目」，且該開口已有對應圖紙，直接略過 (防止重複建圖)
                                if (SkipExisting)
                                {
                                    ViewSheet existingSheet = ExistingItemDetectorService.FindExistingSheet(
                                        _doc, opItem.Instance != null ? opItem.Instance.Id : null, genCache);
                                    if (existingSheet != null)
                                    {
                                        skippedSheets++;
                                        continue;
                                    }
                                }

                                StatusMessage = string.Format("正在製作 ({0}/{1}): {2} ➔ {3} ➔ {4}...", 
                                    successSheets + 1, totalSelected, lvlGroup.LevelName, wallGroup.WallName, opItem.DisplayName);

                                // 1. 建立整道牆的開口剖面圖 (若已有視圖則複用，不重複建立)
                                ViewSection sectionView = SectionService.CreateWallOpeningSection(
                                    _doc, 
                                    wallGroup.Wall, 
                                    opItem.Instance, 
                                    SectionScale, 
                                    OffsetMm,
                                    SelectedSectionViewTemplate,
                                    genCache);

                                // 2. 建立精確裁剪之樓層平面索引圖 (若已有視圖則複用，不重複建立)
                                ViewPlan keyPlan = KeyPlanService.CreateKeyPlan(
                                    _doc, 
                                    lvlGroup.Level, 
                                    wallGroup.Wall, 
                                    opItem.Instance, 
                                    KeyPlanScale,
                                    SelectedPlanViewTemplate,
                                    cropModeKey,
                                    genCache);

                                // 3. 建立開口元件之專屬 3D 選取方塊視圖 (若已有視圖則複用，不重複建立)
                                View3D view3D = View3DService.CreateOpening3DView(
                                    _doc, 
                                    wallGroup.Wall, 
                                    opItem.Instance, 
                                    View3DScale, 
                                    800,
                                    SelectedView3DViewTemplate,
                                    genCache);

                                // 4. 建立 A3 圖紙並精密排版 (剖面 + 索引 + 3D 三合一)
                                ViewSheet sheet = SheetService.CreateOpeningSheet(
                                    _doc, 
                                    SelectedTitleBlock.Id, 
                                    opItem, 
                                    wallGroup, 
                                    lvlGroup, 
                                    sectionView, 
                                    keyPlan, 
                                    view3D,
                                    vpTypeId,
                                    SheetNumberPrefix,
                                    layoutModeKey,
                                    SkipExisting,
                                    genCache);

                                opItem.HasExistingSheet = true;
                                opItem.ExistingSheetNumber = sheet != null ? sheet.SheetNumber : null;
                                opItem.HasExistingViews = true;

                                successSheets++;
                            }
                        }
                    }

                    trans.Commit();
                    
                    string finishMsg = string.Format("全部完成！已成功為所勾選之 {0} 處開孔建立專屬圖紙 (含剖面+索引+3D)。", successSheets);
                    if (skippedSheets > 0)
                    {
                        finishMsg += string.Format("\n（已自動略過 {0} 處已存在圖紙之開孔，無重複建立）", skippedSheets);
                    }
                    StatusMessage = finishMsg.Replace("\n", " ");
                    MessageBox.Show(finishMsg, "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    if (CloseAction != null) CloseAction();
                }
                catch (Exception ex)
                {
                    if (trans.HasStarted()) trans.RollBack();
                    StatusMessage = string.Format("生成失敗：{0}", ex.Message);
                    TaskDialog.Show("生成錯誤", string.Format("執行發生異常：\n{0}\n\n{1}", ex.Message, ex.StackTrace));
                }
                finally
                {
                    IsProcessing = false;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
