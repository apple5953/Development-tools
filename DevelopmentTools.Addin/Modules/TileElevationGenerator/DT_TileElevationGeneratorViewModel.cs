using System;
using System.Collections.Generic;
using System.ComponentModel;
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

        public GeneratorSettings Settings { get; set; } = new GeneratorSettings();
        
        // UI 綁定列表與選取項
        public List<View> ViewTemplates { get; private set; }
        
        private View _selectedTemplate;
        public View SelectedTemplate
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

        private string _statusText = "Ready. Please select Floor or Walls.";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private string _selectedElementText = "No element selected.";
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
                    SelectedElementText = SelectedFloor != null ? $"Floor: {SelectedFloor.Name}" : "No Floor selected.";
                    if (SelectedFloor != null)
                    {
                        string roomNum = DetectRoomNumber(SelectedFloor);
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
                    SelectedElementText = SelectedWalls.Count > 0 ? $"{SelectedWalls.Count} Walls selected." : "No Walls selected.";
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
        public Floor SelectedFloor { get; private set; }
        public List<Wall> SelectedWalls { get; private set; } = new List<Wall>();

        public bool IsConfirmed { get; private set; } = false;

        // 用於控制視窗顯示/隱藏的委派事件
        public Action RequestHide { get; set; }
        public Action RequestShow { get; set; }
        public Action RequestClose { get; set; }

        public ICommand SelectSourceCommand { get; }
        public ICommand GenerateCommand { get; }
        public ICommand OpenHelpCommand { get; }

        public DT_TileElevationGeneratorViewModel(ExternalCommandData commandData)
        {
            _commandData = commandData;
            _uidoc = commandData.Application.ActiveUIDocument;
            _doc = _uidoc.Document;

            // 1. 取得 Section View Templates 並插入 null 代表無樣板
            var templates = ViewTemplateSelector.GetSectionViewTemplates(_doc);
            templates.Insert(0, null);
            ViewTemplates = templates;
            _selectedTemplate = null;
            Settings.SelectedViewTemplateId = ElementId.InvalidElementId;

            // 2. 初始化 Commands
            SelectSourceCommand = new RelayCommand(OnSelectSource);
            GenerateCommand = new RelayCommand(OnGenerate);
            OpenHelpCommand = new RelayCommand(OnOpenHelp);
        }

        private void OnSelectSource()
        {
            try
            {
                // 隱藏設定視窗
                RequestHide?.Invoke();

                if (IsFloorMode)
                {
                    StatusText = "Please click to select a Floor in Revit...";
                    var reference = _uidoc.Selection.PickObject(ObjectType.Element, new FloorSelectionFilter(), "Select a Floor element");
                    if (reference != null)
                    {
                        var floor = _doc.GetElement(reference) as Floor;
                        if (floor != null)
                        {
                            SelectedFloor = floor;
                            SelectedElementText = $"Floor: {floor.Name} (ID: {floor.Id})";
                            StatusText = "Floor selected successfully.";
                            
                            // 自動偵測房號
                            string roomNum = DetectRoomNumber(floor);
                            NamePrefix = !string.IsNullOrEmpty(roomNum) ? $"TE_{roomNum}" : "TE";
                        }
                    }
                }
                else
                {
                    StatusText = "Please select multiple Walls in Revit...";
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
                        SelectedElementText = $"{SelectedWalls.Count} Walls selected.";
                        StatusText = "Walls selected successfully.";
                        
                        // 自動偵測房號
                        string roomNum = DetectRoomNumber(SelectedWalls);
                        NamePrefix = !string.IsNullOrEmpty(roomNum) ? $"TE_{roomNum}" : "TE";
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                StatusText = "Selection cancelled by user.";
            }
            catch (Exception ex)
            {
                StatusText = $"Selection error: {ex.Message}";
            }
            finally
            {
                // 重現設定視窗
                RequestShow?.Invoke();
            }
        }

        private void OnGenerate()
        {
            if (IsFloorMode)
            {
                if (SelectedFloor == null)
                {
                    MessageBox.Show("請先選擇一個地板元件 (Floor)！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                if (SelectedWalls.Count == 0)
                {
                    MessageBox.Show("請先選取至少一面牆元件 (Walls)！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            IsConfirmed = true;
            RequestClose?.Invoke();
        }

        private void OnOpenHelp()
        {
            TaskDialog td = new TaskDialog("展開圖生成器 - 使用指南");
            td.MainInstruction = "磁磚展開圖生成器 (DT_TileElevationGenerator) 新手快速入門";
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

        // --- Revit 元素篩選器 ---
        private class FloorSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private class WallSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Wall;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
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
