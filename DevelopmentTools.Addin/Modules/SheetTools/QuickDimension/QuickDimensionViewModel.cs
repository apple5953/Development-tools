using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace DevelopmentTools.Modules.SheetTools.QuickDimension
{
    /// <summary>
    /// 快速尺寸標註事件處理器 (Modeless 視窗代理)
    /// </summary>
    public class QuickDimensionEventHandler : IExternalEventHandler
    {
        private readonly QuickDimensionViewModel _viewModel;

        public QuickDimensionEventHandler(QuickDimensionViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                _viewModel.ExecuteTagging(app);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"標註過程發生錯誤：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public string GetName() => "QuickDimensionEventHandler";
    }

    /// <summary>
    /// 快速尺寸標註工具的主 ViewModel
    /// </summary>
    public class QuickDimensionViewModel : INotifyPropertyChanged
    {
        private readonly Document _doc;
        private readonly Window _window;
        private readonly ExternalEvent _externalEvent;

        // 標註模式：0 = 柱中心, 1 = 牆中心, 2 = 開口邊到邊
        private int _selectedMode = 0;
        public int SelectedMode
        {
            get => _selectedMode;
            set { _selectedMode = value; OnPropertyChanged(); UpdateStatusText(); }
        }

        // 尺寸標註樣式
        public List<DimensionType> DimensionTypes { get; }
        private DimensionType _selectedDimensionType;
        public DimensionType SelectedDimensionType
        {
            get => _selectedDimensionType;
            set { _selectedDimensionType = value; OnPropertyChanged(); }
        }

        // 偏移距離 (公釐，預設 900mm)
        private double _offsetDistance = 900;
        public double OffsetDistance
        {
            get => _offsetDistance;
            set { _offsetDistance = value; OnPropertyChanged(); }
        }

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // 命令
        public ICommand StartTaggingCommand { get; }
        public ICommand CloseCommand { get; }

        public QuickDimensionViewModel(Document doc, Window window)
        {
            _doc = doc;
            _window = window;
            _externalEvent = ExternalEvent.Create(new QuickDimensionEventHandler(this));

            // 1. 取得專案內所有的 DimensionType
            DimensionTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear)
                .OrderBy(t => t.Name)
                .ToList();

            if (DimensionTypes.Any())
            {
                _selectedDimensionType = DimensionTypes.FirstOrDefault(t => t.Name.Contains("標註")) ?? DimensionTypes.First();
            }

            // 2. 初始化命令
            StartTaggingCommand = new RelayCommand(() => _externalEvent.Raise());
            CloseCommand = new RelayCommand(() => _window.Close());

            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            if (SelectedMode == 0)
                StatusText = "提示：點擊「開始標註」後選取多個柱子，自動標註柱中心到中心。";
            else if (SelectedMode == 1)
                StatusText = "提示：點擊「開始標註」後選取多道平行牆體，自動標註牆中心到中心。";
            else
                StatusText = "提示：點擊「開始標註」後選取門、窗或開口，自動標註開口寬度與淨距。";
        }

        /// <summary>
        /// 執行標註的核心方法 (在 Revit 執行緒中呼叫)
        /// </summary>
        public void ExecuteTagging(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            View activeView = uidoc.ActiveView;

            if (activeView.ViewType != ViewType.FloorPlan && activeView.ViewType != ViewType.EngineeringPlan && activeView.ViewType != ViewType.CeilingPlan)
            {
                MessageBox.Show("請切換至平面圖、結構平面圖或天花板平面圖視圖後再執行此標註工具。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. 隱藏視窗，方便使用者在 Revit 中進行選取
            _window.Dispatcher.Invoke(() => _window.Hide());

            try
            {
                if (SelectedMode == 0)
                {
                    TagColumns(uidoc, activeView);
                }
                else if (SelectedMode == 1)
                {
                    TagWalls(uidoc, activeView);
                }
                else
                {
                    TagOpenings(uidoc, activeView);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // 使用者按 ESC 取消選取，安靜退出
            }
            catch (Exception ex)
            {
                MessageBox.Show($"建立標註時發生異常：{ex.Message}", "標註錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 2. 選取完成或出錯後，重新顯示視窗
                _window.Dispatcher.Invoke(() =>
                {
                    _window.Show();
                    _window.Activate();
                    _window.Focus();
                });
            }
        }

        // --- 柱中心到柱中心標註 ---
        private void TagColumns(UIDocument uidoc, View view)
        {
            var selFilter = new ElementSelectionFilter<FamilyInstance>(e => 
                e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns || 
                e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Columns);

            IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                ObjectType.Element,
                selFilter,
                "請依序選取多個柱子以建立中心標註 (選完後按 Esc 或在空白處點擊完成)："
            );

            if (pickedRefs.Count < 2) return;

            // 取得柱子元素列表
            var cols = pickedRefs
                .Select(r => _doc.GetElement(r.ElementId) as FamilyInstance)
                .Where(c => c != null)
                .ToList();

            // 取得柱子中心定位點
            var pts = cols.Select(c => (c.Location as LocationPoint)?.Point).Where(p => p != null).ToList();
            if (pts.Count < 2) return;

            // 分析排列方向 (水平或垂直)
            double minX = pts.Min(p => p.X);
            double maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y);
            double maxY = pts.Max(p => p.Y);

            bool isHorizontal = (maxX - minX) >= (maxY - minY);

            // 提取中心參照
            ReferenceArray refArray = new ReferenceArray();
            foreach (var col in cols)
            {
                Reference colRef = GetColumnCenterReference(col, isHorizontal);
                if (colRef != null)
                {
                    refArray.Append(colRef);
                }
            }

            if (refArray.Size < 2)
            {
                MessageBox.Show("未能提取出足夠的柱中心幾何參照，無法建立標註。", "標註提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 建立尺寸定位線 (與中心連線平行，並往垂直方向偏移)
            XYZ p1 = pts.First();
            XYZ p2 = pts.Last();
            XYZ dir = (p2 - p1).Normalize();
            XYZ normal = isHorizontal ? XYZ.BasisY : XYZ.BasisX; // 垂直於排列方向

            // 換算偏移 (公釐轉為英呎，Revit 內部幾何單位)
            double offsetFeet = (OffsetDistance / 304.8);
            XYZ offsetVec = normal * offsetFeet;

            XYZ lineStart = p1 + offsetVec;
            XYZ lineEnd = p2 + offsetVec;
            Line dimLine = Line.CreateBound(lineStart, lineEnd);

            using (Transaction trans = new Transaction(_doc, "快速標註-柱中心"))
            {
                trans.Start();
                Dimension dim = _doc.Create.NewDimension(view, dimLine, refArray);
                if (dim != null && SelectedDimensionType != null)
                {
                    dim.DimensionType = SelectedDimensionType;
                }
                trans.Commit();
            }
        }

        private Reference GetColumnCenterReference(FamilyInstance col, bool isHorizontal)
        {
            // 嘗試取得 Center 強參照面
            IList<Reference> refs = col.GetReferences(isHorizontal 
                ? FamilyInstanceReferenceType.CenterLeftRight 
                : FamilyInstanceReferenceType.CenterFrontBack);

            if (refs != null && refs.Count > 0)
                return refs[0];

            // 備選做法：找任何包含 Center 字樣的參照
            foreach (FamilyInstanceReferenceType type in Enum.GetValues(typeof(FamilyInstanceReferenceType)))
            {
                if (type.ToString().Contains("Center"))
                {
                    var tempRefs = col.GetReferences(type);
                    if (tempRefs.Count > 0) return tempRefs[0];
                }
            }

            return null;
        }

        // --- 牆中心到牆中心標註 ---
        private void TagWalls(UIDocument uidoc, View view)
        {
            var selFilter = new ElementSelectionFilter<Wall>(e => true);
            IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                ObjectType.Element,
                selFilter,
                "請選取多道平行的牆體以建立中心標註："
            );

            if (pickedRefs.Count < 2) return;

            var walls = pickedRefs
                .Select(r => _doc.GetElement(r.ElementId) as Wall)
                .Where(w => w != null)
                .ToList();

            ReferenceArray refArray = new ReferenceArray();
            List<XYZ> pts = new List<XYZ>();

            foreach (var w in walls)
            {
                // 用牆體 Element 本身產生的 Reference，在 Revit Aligned Dimension 中代表其中心定位線
                Reference r = new Reference(w);
                refArray.Append(r);

                // 取得牆中心線中點作為定位基準
                if (w.Location is LocationCurve lc && lc.Curve is Line line)
                {
                    pts.Add(line.Evaluate(0.5, true));
                }
            }

            if (refArray.Size < 2 || pts.Count < 2) return;

            // 尺寸線定位：過各牆中點的平均點，平行於牆方向或垂直於牆方向
            XYZ avgPt = new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
            
            // 取得第一道牆的方向向量
            XYZ wallDir = XYZ.BasisX;
            if (walls[0].Location is LocationCurve lc0 && lc0.Curve is Line line0)
            {
                wallDir = line0.Direction.Normalize();
            }

            XYZ normal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize(); // 垂直於牆面的法向量

            double offsetFeet = (OffsetDistance / 304.8);
            XYZ offsetVec = normal * offsetFeet;

            XYZ lineStart = avgPt + offsetVec;
            XYZ lineEnd = lineStart + wallDir * 10.0; // 建立一條尺寸定位輔助線
            Line dimLine = Line.CreateBound(lineStart, lineEnd);

            using (Transaction trans = new Transaction(_doc, "快速標註-牆中心"))
            {
                trans.Start();
                Dimension dim = _doc.Create.NewDimension(view, dimLine, refArray);
                if (dim != null && SelectedDimensionType != null)
                {
                    dim.DimensionType = SelectedDimensionType;
                }
                trans.Commit();
            }
        }

        // --- 開口邊到邊標註 (門/窗/開口) ---
        private void TagOpenings(UIDocument uidoc, View view)
        {
            var selFilter = new ElementSelectionFilter<FamilyInstance>(e => 
                e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors || 
                e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows);

            IList<Reference> pickedRefs = uidoc.Selection.PickObjects(
                ObjectType.Element,
                selFilter,
                "請選取同一面牆上的多個門或窗："
            );

            if (pickedRefs.Count == 0) return;

            var items = pickedRefs
                .Select(r => _doc.GetElement(r.ElementId) as FamilyInstance)
                .Where(fi => fi != null)
                .ToList();

            ReferenceArray refArray = new ReferenceArray();
            List<XYZ> pts = new List<XYZ>();

            // 尋找各門窗的 Left 與 Right 參照平面
            foreach (var item in items)
            {
                var leftRefs = item.GetReferences(FamilyInstanceReferenceType.Left);
                var rightRefs = item.GetReferences(FamilyInstanceReferenceType.Right);

                if (leftRefs.Count > 0 && rightRefs.Count > 0)
                {
                    refArray.Append(leftRefs[0]);
                    refArray.Append(rightRefs[0]);
                }

                if (item.Location is LocationPoint lp)
                {
                    pts.Add(lp.Point);
                }
            }

            if (refArray.Size < 2)
            {
                MessageBox.Show("未能提取出開口邊界的左右參照面，請確認門窗族內部是否包含 Left/Right 強參照面。", "標註提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 計算平均點與尺寸定位線 (沿著牆方向，並往外側偏移)
            XYZ avgPt = pts.Count > 0 ? new XYZ(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z)) : XYZ.Zero;
            
            // 藉由門窗 Host (牆) 的方向來決定對齊方向
            XYZ wallDir = XYZ.BasisX;
            if (items[0].Host is Wall hostWall && hostWall.Location is LocationCurve lc && lc.Curve is Line line)
            {
                wallDir = line.Direction.Normalize();
            }

            XYZ normal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();
            double offsetFeet = (OffsetDistance / 304.8);
            XYZ offsetVec = normal * offsetFeet;

            XYZ lineStart = avgPt + offsetVec;
            XYZ lineEnd = lineStart + wallDir * 10.0;
            Line dimLine = Line.CreateBound(lineStart, lineEnd);

            using (Transaction trans = new Transaction(_doc, "快速標註-開口邊"))
            {
                trans.Start();
                Dimension dim = _doc.Create.NewDimension(view, dimLine, refArray);
                if (dim != null && SelectedDimensionType != null)
                {
                    dim.DimensionType = SelectedDimensionType;
                }
                trans.Commit();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 泛用選取過濾器
    /// </summary>
    public class ElementSelectionFilter<T> : ISelectionFilter where T : Element
    {
        private readonly Func<T, bool> _validator;

        public ElementSelectionFilter(Func<T, bool> validator)
        {
            _validator = validator;
        }

        public bool AllowElement(Element elem)
        {
            if (elem is T tElem)
            {
                return _validator(tElem);
            }
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    /// <summary>
    /// RelayCommand 簡化版類別
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
