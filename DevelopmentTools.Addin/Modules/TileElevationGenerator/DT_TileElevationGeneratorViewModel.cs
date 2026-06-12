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
        public List<Level> ProjectLevels { get; private set; }

        private Level _selectedBaseLevel;
        public Level SelectedBaseLevel
        {
            get => _selectedBaseLevel;
            set
            {
                _selectedBaseLevel = value;
                OnPropertyChanged();
                UpdateElevationItemsGeometry();
            }
        }

        private Level _selectedTopLevel;
        public Level SelectedTopLevel
        {
            get => _selectedTopLevel;
            set
            {
                _selectedTopLevel = value;
                OnPropertyChanged();
                UpdateElevationItemsGeometry();
            }
        }

        private void UpdateElevationItemsGeometry()
        {
            if (_selectedBaseLevel == null) return;
            double baseElev = _selectedBaseLevel.Elevation;
            double topElev = _selectedTopLevel != null ? _selectedTopLevel.Elevation : (baseElev + 3000.0 / 304.8);
            double height = topElev - baseElev;

            foreach (var item in ElevationItems)
            {
                if (item.GeometryData != null)
                {
                    item.GeometryData.LevelElevation = baseElev;
                    item.GeometryData.WallHeight = height;
                    item.RaiseGeometryPropertiesChanged();
                }
            }
            RaiseDrawingPropertiesChanged();
        }
        
        // 新增幾何參數繫結
        public double ViewDepth
        {
            get => Settings.ViewDepth;
            set
            {
                Settings.ViewDepth = value;
                OnPropertyChanged();
                RaiseDrawingPropertiesChanged();
            }
        }

        public double WallOffset
        {
            get => Settings.WallOffset;
            set
            {
                Settings.WallOffset = value;
                OnPropertyChanged();
                RaiseDrawingPropertiesChanged();
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
                RaiseDrawingPropertiesChanged();
            }
        }

        // UI 剖面項目清單與選中項目
        public System.Collections.ObjectModel.ObservableCollection<WallElevationDataItem> ElevationItems { get; } = new System.Collections.ObjectModel.ObservableCollection<WallElevationDataItem>();

        private WallElevationDataItem _selectedElevationItem;
        public WallElevationDataItem SelectedElevationItem
        {
            get => _selectedElevationItem;
            set
            {
                if (_selectedElevationItem != null)
                {
                    _selectedElevationItem.PropertyChanged -= OnSelectedElevationItemPropertyChanged;
                }
                _selectedElevationItem = value;
                if (_selectedElevationItem != null)
                {
                    _selectedElevationItem.PropertyChanged += OnSelectedElevationItemPropertyChanged;
                }
                OnPropertyChanged();
                RaiseDrawingPropertiesChanged();
            }
        }

        private void OnSelectedElevationItemPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WallElevationDataItem.WallOffset) ||
                e.PropertyName == nameof(WallElevationDataItem.ViewDepth) ||
                e.PropertyName == nameof(WallElevationDataItem.SideExtension) ||
                e.PropertyName == nameof(WallElevationDataItem.TopOffset) ||
                e.PropertyName == nameof(WallElevationDataItem.BottomOffset) ||
                e.PropertyName == nameof(WallElevationDataItem.FlipDirection))
            {
                RaiseDrawingPropertiesChanged();
            }
        }

        // 批次套用當前參數為預設值並更新清單中所有項目
        public ICommand SetAsDefaultCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        private void OnSetAsDefault()
        {
            if (SelectedElevationItem == null) return;
            
            // 1. 儲存至全域 Settings 預設值
            Settings.WallOffset = SelectedElevationItem.WallOffset;
            Settings.ViewDepth = SelectedElevationItem.ViewDepth;
            Settings.SideExtension = SelectedElevationItem.SideExtension;
            Settings.TopOffset = SelectedElevationItem.TopOffset;
            Settings.BottomOffset = SelectedElevationItem.BottomOffset;
            Settings.FlipDirection = SelectedElevationItem.FlipDirection;

            // 2. 將此設定批次覆蓋到清單內的所有剖面項目
            foreach (var item in ElevationItems)
            {
                item.WallOffset = Settings.WallOffset;
                item.ViewDepth = Settings.ViewDepth;
                item.SideExtension = Settings.SideExtension;
                item.TopOffset = Settings.TopOffset;
                item.BottomOffset = Settings.BottomOffset;
                item.FlipDirection = Settings.FlipDirection;
            }

            RaiseDrawingPropertiesChanged();
            StatusText = $"[已更新預設值] 已將「{SelectedElevationItem.ViewName}」的參數設定為全域預設值，並套用到所有剖面。";
        }

        private void OnMoveUp()
        {
            if (SelectedElevationItem == null) return;
            int index = ElevationItems.IndexOf(SelectedElevationItem);
            if (index <= 0) return;

            ElevationItems.Move(index, index - 1);
            ReorderAndRenameViews();
        }

        private void OnMoveDown()
        {
            if (SelectedElevationItem == null) return;
            int index = ElevationItems.IndexOf(SelectedElevationItem);
            if (index < 0 || index >= ElevationItems.Count - 1) return;

            ElevationItems.Move(index, index + 1);
            ReorderAndRenameViews();
        }

        private void ReorderAndRenameViews()
        {
            for (int i = 0; i < ElevationItems.Count; i++)
            {
                var item = ElevationItems[i];
                string defaultName = ElevationNamingService.GenerateViewName(_doc, NamePrefix, i);
                item.ViewName = defaultName;
            }
            RaiseDrawingPropertiesChanged();
            StatusText = "已重新排列剖面順序並更新編號。";
        }

        // 立面示意圖高度預覽屬性 (模擬總樓高 3000mm 對應 60px 繪圖區)
        // 地板基底 Y = 85，天花頂部基準 Y = 85 - 60*ZoomFactor
        public double DrawFloorY => 85.0;

        public double DrawCeilingY => 85.0 - (60.0 * ZoomFactor);

        public double DrawCeilingTextTop => DrawCeilingY - 15.0;

        public double DrawSectionCropTop
        {
            get
            {
                double topOffset = SelectedElevationItem != null ? SelectedElevationItem.TopOffset : TopOffset;
                // 1mm ＝ 0.02px, TopOffset 往上移 (即 Y 變小) 且乘上縮放
                return DrawCeilingY - (topOffset * 0.02 * ZoomFactor);
            }
        }

        public double DrawSectionCropHeight
        {
            get
            {
                double topOffset = SelectedElevationItem != null ? SelectedElevationItem.TopOffset : TopOffset;
                double bottomOffset = SelectedElevationItem != null ? SelectedElevationItem.BottomOffset : BottomOffset;
                
                double cropBottom = 85.0 + (bottomOffset * 0.02 * ZoomFactor);
                double cropTop = DrawSectionCropTop;
                
                double h = cropBottom - cropTop;
                return h > 5 ? h : 5;
            }
        }

        // UI 向量繪圖 Binding 屬性
        private const double ScalePx = 0.15;
        private const double BaseWallY = 120; // 備用牆面 Y 位置
        private const double BaseWallX = 60;  // 備用牆面左端 X 位置
        private const double BaseWallWidth = 120; // 備用牆面寬度固定值

        public double DrawWallY => BaseWallY;
        public double DrawWallX => BaseWallX;
        public double DrawWallWidth => BaseWallWidth;

        // 計算選取邊界的實際平面多邊形座標串以在 UI Canvas 繪製
        public System.Windows.Media.PointCollection FloorPolygonPoints
        {
            get
            {
                var points = new System.Windows.Media.PointCollection();
                if (ElevationItems.Count == 0)
                {
                    // 備用預設矩形樓板
                    points.Add(new System.Windows.Point(40, 20));
                    points.Add(new System.Windows.Point(220, 20));
                    points.Add(new System.Windows.Point(220, 130));
                    points.Add(new System.Windows.Point(40, 130));
                    return points;
                }

                // 1. 收集所有線段的端點並計算邊界範圍 (Bounding Box)
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                var segments = new List<Tuple<XYZ, XYZ>>();

                foreach (var item in ElevationItems)
                {
                    var geom = item.GeometryData;
                    if (geom == null) continue;
                    segments.Add(Tuple.Create(geom.StartPoint, geom.EndPoint));
                    minX = Math.Min(minX, Math.Min(geom.StartPoint.X, geom.EndPoint.X));
                    maxX = Math.Max(maxX, Math.Max(geom.StartPoint.X, geom.EndPoint.X));
                    minY = Math.Min(minY, Math.Min(geom.StartPoint.Y, geom.EndPoint.Y));
                    maxY = Math.Max(maxY, Math.Max(geom.StartPoint.Y, geom.EndPoint.Y));
                }

                double w = maxX - minX;
                double h = maxY - minY;
                if (w < 0.001) w = 1.0;
                if (h < 0.001) h = 1.0;

                // 2. 計算等比例縮放以適應 240 x 140 的 Canvas 顯示區
                double canvasW = 220;
                double canvasH = 120;
                double padX = 25;
                double padY = 20;

                double scale = GetCanvasGeometryScale();
                // 居中對齊偏移
                double offsetX = padX + (canvasW - w * scale) / 2.0;
                double offsetY = padY + (canvasH - h * scale) / 2.0;

                // 3. 按照線段連接順序提取多邊形頂點
                // 為了避免線段亂序，我們從第一個線段的 StartPoint 開始，逐步尋找首尾相連的點
                var orderedPoints = new List<XYZ>();
                if (segments.Count > 0)
                {
                    var remaining = new List<Tuple<XYZ, XYZ>>(segments);
                    var currentSeg = remaining[0];
                    orderedPoints.Add(currentSeg.Item1);
                    XYZ currentPoint = currentSeg.Item2;
                    remaining.RemoveAt(0);

                    while (remaining.Count > 0)
                    {
                        orderedPoints.Add(currentPoint);
                        int nextIdx = -1;
                        bool reverse = false;

                        for (int i = 0; i < remaining.Count; i++)
                        {
                            if (remaining[i].Item1.DistanceTo(currentPoint) < 0.1)
                            {
                                nextIdx = i;
                                reverse = false;
                                break;
                            }
                            if (remaining[i].Item2.DistanceTo(currentPoint) < 0.1)
                            {
                                nextIdx = i;
                                reverse = true;
                                break;
                            }
                        }

                        if (nextIdx != -1)
                        {
                            currentPoint = reverse ? remaining[nextIdx].Item1 : remaining[nextIdx].Item2;
                            remaining.RemoveAt(nextIdx);
                        }
                        else
                        {
                            // 找不到相連線段 (可能是不連續邊界，直接加對面端點並跳出)
                            break;
                        }
                    }
                }

                // 如果拼合順序頂點太少，退回到使用最單純的頂點收集
                if (orderedPoints.Count < 3)
                {
                    orderedPoints.Clear();
                    foreach (var seg in segments)
                    {
                        if (!orderedPoints.Any(p => p.DistanceTo(seg.Item1) < 0.01)) orderedPoints.Add(seg.Item1);
                        if (!orderedPoints.Any(p => p.DistanceTo(seg.Item2) < 0.01)) orderedPoints.Add(seg.Item2);
                    }
                }

                // 4. 將 Revit 座標轉換至 Canvas 繪圖座標
                foreach (var pt in orderedPoints)
                {
                    double x = offsetX + (pt.X - minX) * scale;
                    // Revit 的 Y 軸朝上，Canvas 的 Y 軸朝下，需以 minY 鏡射
                    double y = offsetY + canvasH - (pt.Y - minY) * scale;
                    points.Add(new System.Windows.Point(x, y));
                }

                return points;
            }
        }

        // 當前選取之剖切刀線的起點與終點 (在 Canvas 座標系下)
        public double SelectedCutLineX1
        {
            get
            {
                if (SelectedElevationItem?.GeometryData == null) return 30;
                var geom = SelectedElevationItem.GeometryData;
                double scale = GetCanvasGeometryScale();
                double minX = GetCanvasMinX();
                double offsetX = GetCanvasOffsetX();

                // 剖刀線位置：朝著房間中心內進（順著法線方向 +）
                double offsetFeet = SelectedElevationItem.WallOffset / 304.8;
                if (SelectedElevationItem.FlipDirection) offsetFeet = -offsetFeet;
                double targetX = geom.StartPoint.X + geom.WallNormal.X * offsetFeet;
                return offsetX + (targetX - minX) * scale;
            }
        }

        public double SelectedCutLineY1
        {
            get
            {
                if (SelectedElevationItem?.GeometryData == null) return 70;
                var geom = SelectedElevationItem.GeometryData;
                double scale = GetCanvasGeometryScale();
                double minY = GetCanvasMinY();
                double offsetY = GetCanvasOffsetY();
                double canvasH = 120;

                double offsetFeet = SelectedElevationItem.WallOffset / 304.8;
                if (SelectedElevationItem.FlipDirection) offsetFeet = -offsetFeet;
                // Revit Y 與 Canvas Y 鏡像反轉，因此 Revit 中 +WallNormal.Y 對應到 Canvas 應為減去 WallNormal.Y
                double targetY = geom.StartPoint.Y + geom.WallNormal.Y * offsetFeet;
                return offsetY + canvasH - (targetY - minY) * scale;
            }
        }

        public double SelectedCutLineX2
        {
            get
            {
                if (SelectedElevationItem?.GeometryData == null) return 210;
                var geom = SelectedElevationItem.GeometryData;
                double scale = GetCanvasGeometryScale();
                double minX = GetCanvasMinX();
                double offsetX = GetCanvasOffsetX();

                double offsetFeet = SelectedElevationItem.WallOffset / 304.8;
                if (SelectedElevationItem.FlipDirection) offsetFeet = -offsetFeet;
                double targetX = geom.EndPoint.X + geom.WallNormal.X * offsetFeet;
                return offsetX + (targetX - minX) * scale;
            }
        }

        public double SelectedCutLineY2
        {
            get
            {
                if (SelectedElevationItem?.GeometryData == null) return 70;
                var geom = SelectedElevationItem.GeometryData;
                double scale = GetCanvasGeometryScale();
                double minY = GetCanvasMinY();
                double offsetY = GetCanvasOffsetY();
                double canvasH = 120;

                double offsetFeet = SelectedElevationItem.WallOffset / 304.8;
                if (SelectedElevationItem.FlipDirection) offsetFeet = -offsetFeet;
                double targetY = geom.EndPoint.Y + geom.WallNormal.Y * offsetFeet;
                return offsetY + canvasH - (targetY - minY) * scale;
            }
        }

        // 剖面視圖深度範圍的 4 個多邊形端點 (呈現一個半透明區域，從剖刀線往牆體方向看 depth 深度)
        // 剖刀方向朝牆面（即法線相反方向： -geom.WallNormal），所以深度的延伸是往牆體前進（-geom.WallNormal 方向的相反，即朝著 -geom.WallNormal 延伸，Revit 剖面視圖深度是向後看，BasisZ 是 -WallNormal，所以深度是往 -WallNormal 方向延伸）
        public System.Windows.Media.PointCollection SelectedDepthRangePoints
        {
            get
            {
                var points = new System.Windows.Media.PointCollection();
                if (SelectedElevationItem?.GeometryData == null) return points;

                var geom = SelectedElevationItem.GeometryData;
                double scale = GetCanvasGeometryScale();
                double minX = GetCanvasMinX();
                double minY = GetCanvasMinY();
                double offsetX = GetCanvasOffsetX();
                double offsetY = GetCanvasOffsetY();
                double canvasH = 120;

                double offsetFeet = SelectedElevationItem.WallOffset / 304.8;
                double depthFeet = SelectedElevationItem.ViewDepth / 304.8;
                double sign = SelectedElevationItem.FlipDirection ? -1.0 : 1.0;

                // 剖刀線端點 1 與 2 (Revit 坐報)
                XYZ cutStart = geom.StartPoint + geom.WallNormal * (offsetFeet * sign);
                XYZ cutEnd = geom.EndPoint + geom.WallNormal * (offsetFeet * sign);

                // 剖切深度終點 (往 -geom.WallNormal 延伸 depthFeet)
                XYZ depthStart = cutStart - geom.WallNormal * (depthFeet * sign);
                XYZ depthEnd = cutEnd - geom.WallNormal * (depthFeet * sign);

                // 轉為 Canvas 坐標
                System.Windows.Point pCutStart = new System.Windows.Point(
                    offsetX + (cutStart.X - minX) * scale,
                    offsetY + canvasH - (cutStart.Y - minY) * scale
                );
                System.Windows.Point pCutEnd = new System.Windows.Point(
                    offsetX + (cutEnd.X - minX) * scale,
                    offsetY + canvasH - (cutEnd.Y - minY) * scale
                );
                System.Windows.Point pDepthEnd = new System.Windows.Point(
                    offsetX + (depthEnd.X - minX) * scale,
                    offsetY + canvasH - (depthEnd.Y - minY) * scale
                );
                System.Windows.Point pDepthStart = new System.Windows.Point(
                    offsetX + (depthStart.X - minX) * scale,
                    offsetY + canvasH - (depthStart.Y - minY) * scale
                );

                points.Add(pCutStart);
                points.Add(pCutEnd);
                points.Add(pDepthEnd);
                points.Add(pDepthStart);

                return points;
            }
        }

        private double GetCanvasMinX()
        {
            double minX = double.MaxValue;
            foreach (var item in ElevationItems)
            {
                var geom = item.GeometryData;
                if (geom == null) continue;
                minX = Math.Min(minX, Math.Min(geom.StartPoint.X, geom.EndPoint.X));
            }
            return minX == double.MaxValue ? 0.0 : minX;
        }

        private double GetCanvasMinY()
        {
            double minY = double.MaxValue;
            foreach (var item in ElevationItems)
            {
                var geom = item.GeometryData;
                if (geom == null) continue;
                minY = Math.Min(minY, Math.Min(geom.StartPoint.Y, geom.EndPoint.Y));
            }
            return minY == double.MaxValue ? 0.0 : minY;
        }

        private double GetCanvasOffsetX()
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            foreach (var item in ElevationItems)
            {
                var geom = item.GeometryData;
                if (geom == null) continue;
                minX = Math.Min(minX, Math.Min(geom.StartPoint.X, geom.EndPoint.X));
                maxX = Math.Max(maxX, Math.Max(geom.StartPoint.X, geom.EndPoint.X));
            }
            double w = maxX - minX;
            if (w < 0.001) w = 1.0;
            double scale = GetCanvasGeometryScale();
            return 25.0 + (220.0 - w * scale) / 2.0;
        }

        private double GetCanvasOffsetY()
        {
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var item in ElevationItems)
            {
                var geom = item.GeometryData;
                if (geom == null) continue;
                minY = Math.Min(minY, Math.Min(geom.StartPoint.Y, geom.EndPoint.Y));
                maxY = Math.Max(maxY, Math.Max(geom.StartPoint.Y, geom.EndPoint.Y));
            }
            double h = maxY - minY;
            if (h < 0.001) h = 1.0;
            double scale = GetCanvasGeometryScale();
            return 20.0 + (120.0 - h * scale) / 2.0;
        }

        // 取得當前選定剖切面編號 (以 1-based index 顯示在示意圖上)
        public string SelectedSectionIndexText
        {
            get
            {
                if (SelectedElevationItem == null) return "";
                int idx = ElevationItems.IndexOf(SelectedElevationItem);
                return $"當前選中剖面: {SelectedElevationItem.ViewName} (刀位置 #{idx + 1})";
            }
        }

        private double _zoomFactor = 1.0;
        public double ZoomFactor
        {
            get => _zoomFactor;
            set
            {
                _zoomFactor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ZoomPercentText));
            }
        }

        public string ZoomPercentText => $"縮放: {Math.Round(_zoomFactor * 100)}%";

        private double GetCanvasGeometryScale()
        {
            if (ElevationItems.Count == 0) return 1.0;
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var item in ElevationItems)
            {
                var geom = item.GeometryData;
                if (geom == null) continue;
                minX = Math.Min(minX, Math.Min(geom.StartPoint.X, geom.EndPoint.X));
                maxX = Math.Max(maxX, Math.Max(geom.StartPoint.X, geom.EndPoint.X));
                minY = Math.Min(minY, Math.Min(geom.StartPoint.Y, geom.EndPoint.Y));
                maxY = Math.Max(maxY, Math.Max(geom.StartPoint.Y, geom.EndPoint.Y));
            }
            double w = maxX - minX;
            double h = maxY - minY;
            if (w < 0.001) w = 1.0;
            if (h < 0.001) h = 1.0;
            return Math.Min(220.0 / w, 120.0 / h);
        }

        // 剖刀線 Y 坐標 (在牆面下方，亦即房間內)
        public double DrawCutLineY
        {
            get
            {
                double offset = SelectedElevationItem != null ? SelectedElevationItem.WallOffset : WallOffset;
                return BaseWallY + (offset * ScalePx);
            }
        }

        // 剖面左右延伸後的 X 與 Width
        public double DrawCropX
        {
            get
            {
                double sideExt = SelectedElevationItem != null ? SelectedElevationItem.SideExtension : SideExtension;
                return BaseWallX - (sideExt * ScalePx);
            }
        }

        public double DrawCropWidth
        {
            get
            {
                double sideExt = SelectedElevationItem != null ? SelectedElevationItem.SideExtension : SideExtension;
                return BaseWallWidth + (sideExt * 2.0 * ScalePx);
            }
        }

        // 剖切框 (CropBox) 的 Y 與 Height
        // 因為是朝向牆面看，所以裁剪框是從剖刀線(DrawCutLineY)往牆面(減 Y 方向)延伸 ViewDepth 深度
        public double DrawCropY => DrawCutLineY - DrawCropHeight;
        public double DrawCropHeight
        {
            get
            {
                double depth = SelectedElevationItem != null ? SelectedElevationItem.ViewDepth : ViewDepth;
                return depth * ScalePx;
            }
        }

        // 箭頭點座標 (朝上的觀看方向箭頭)
        public string DrawArrowPoints
        {
            get
            {
                double centerX = BaseWallX + (BaseWallWidth / 2.0);
                double arrowStartY = DrawCutLineY;
                double arrowEndY = arrowStartY - 12; // 往上指 12 像素
                return $"{centerX-6},{arrowStartY} {centerX},{arrowEndY} {centerX+6},{arrowStartY}";
            }
        }

        private void RaiseDrawingPropertiesChanged()
        {
            OnPropertyChanged(nameof(FloorPolygonPoints));
            OnPropertyChanged(nameof(SelectedCutLineX1));
            OnPropertyChanged(nameof(SelectedCutLineY1));
            OnPropertyChanged(nameof(SelectedCutLineX2));
            OnPropertyChanged(nameof(SelectedCutLineY2));
            OnPropertyChanged(nameof(SelectedDepthRangePoints));
            OnPropertyChanged(nameof(SelectedSectionIndexText));
            OnPropertyChanged(nameof(DrawCutLineY));
            OnPropertyChanged(nameof(DrawCropX));
            OnPropertyChanged(nameof(DrawCropWidth));
            OnPropertyChanged(nameof(DrawCropY));
            OnPropertyChanged(nameof(DrawCropHeight));
            OnPropertyChanged(nameof(DrawArrowPoints));
            OnPropertyChanged(nameof(DrawFloorY));
            OnPropertyChanged(nameof(DrawCeilingY));
            OnPropertyChanged(nameof(DrawCeilingTextTop));
            OnPropertyChanged(nameof(DrawSectionCropTop));
            OnPropertyChanged(nameof(DrawSectionCropHeight));
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
        private System.Collections.Generic.Dictionary<ElementId, GeneratorSettings> _viewToSettingsMap = new System.Collections.Generic.Dictionary<ElementId, GeneratorSettings>();

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

            // 載入專案中所有的 Level 並排序
            var levelCollector = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .WhereElementIsNotElementType()
                .Cast<Level>()
                .ToList();
            levelCollector.Sort((a, b) => a.Elevation.CompareTo(b.Elevation));
            ProjectLevels = levelCollector;

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
            SetAsDefaultCommand = new RelayCommand(OnSetAsDefault);
            MoveUpCommand = new RelayCommand(OnMoveUp);
            MoveDownCommand = new RelayCommand(OnMoveDown);
            
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

                        AnalyzeSourceAndFillItems();
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

                        AnalyzeSourceAndFillItems();
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

        private void AnalyzeSourceAndFillItems()
        {
            try
            {
                ElevationItems.Clear();
                _tempWallDataList.Clear();
                _isStep1Ok = false;
                RefreshStepButtons();

                List<WallElevationData> dataList;
                if (IsFloorMode)
                {
                    if (SelectedFloors.Count == 0) return;
                    dataList = WallElevationDataBuilder.BuildDataFromFloorsAndSolids(_doc, SelectedFloors, Settings);
                }
                else
                {
                    if (SelectedWalls.Count == 0) return;
                    XYZ referenceCenter = GetWallsAverageCenter(SelectedWalls);
                    dataList = WallElevationDataBuilder.BuildData(_doc, SelectedWalls, Settings, referenceCenter);
                }

                _tempWallDataList = dataList;

                for (int i = 0; i < dataList.Count; i++)
                {
                    var data = dataList[i];
                    string defaultName = ElevationNamingService.GenerateViewName(_doc, NamePrefix, i);
                    
                    var item = new WallElevationDataItem
                    {
                        IsSelected = true,
                        ViewName = defaultName,
                        ViewDepth = Settings.ViewDepth,
                        WallOffset = Settings.WallOffset,
                        SideExtension = Settings.SideExtension,
                        TopOffset = Settings.TopOffset,
                        BottomOffset = Settings.BottomOffset,
                        GeometryData = data
                    };
                    ElevationItems.Add(item);
                }

                if (ElevationItems.Count > 0)
                {
                    SelectedElevationItem = ElevationItems[0];
                    _isStep1Ok = true;
                    StatusText = $"[分析成功] 已識別出 {ElevationItems.Count} 個剖面幾何定位，請在下方列表中確認與微調參數。";
                    
                    // 預設自動選定樓層
                    if (IsFloorMode)
                    {
                        SetDefaultLevels(SelectedFloors);
                    }
                    else
                    {
                        SetDefaultLevels(SelectedWalls);
                    }
                }
                else
                {
                    StatusText = "未偵測到任何有效邊界！";
                }

                RefreshStepButtons();
            }
            catch (Exception ex)
            {
                StatusText = "分析幾何失敗：" + ex.Message;
            }
        }

        private void SetDefaultLevels(System.Collections.IEnumerable elements)
        {
            if (elements == null || ProjectLevels == null || ProjectLevels.Count == 0) return;
            
            ElementId levelId = ElementId.InvalidElementId;
            double approxMinZ = 0.0;
            double approxHeight = 3000.0 / 304.8;
            
            object firstObj = null;
            foreach (var obj in elements)
            {
                firstObj = obj;
                break;
            }
            
            if (firstObj is Floor floor)
            {
                levelId = floor.LevelId;
                var bbox = floor.get_BoundingBox(null);
                if (bbox != null) approxMinZ = bbox.Min.Z;
            }
            else if (firstObj is Wall wall)
            {
                levelId = wall.LevelId;
                var bbox = wall.get_BoundingBox(null);
                if (bbox != null)
                {
                    approxMinZ = bbox.Min.Z;
                    approxHeight = bbox.Max.Z - bbox.Min.Z;
                }
            }
            
            Level baseLvl = null;
            if (levelId != null && levelId != ElementId.InvalidElementId)
            {
                baseLvl = ProjectLevels.FirstOrDefault(l => l.Id == levelId);
            }
            
            if (baseLvl == null)
            {
                baseLvl = ProjectLevels.OrderBy(l => Math.Abs(l.Elevation - approxMinZ)).FirstOrDefault();
            }
            
            if (baseLvl != null)
            {
                _selectedBaseLevel = baseLvl;
                
                int idx = ProjectLevels.IndexOf(baseLvl);
                if (idx >= 0 && idx < ProjectLevels.Count - 1)
                {
                    _selectedTopLevel = ProjectLevels[idx + 1];
                }
                else
                {
                    _selectedTopLevel = baseLvl;
                }
                OnPropertyChanged(nameof(SelectedBaseLevel));
                OnPropertyChanged(nameof(SelectedTopLevel));
                
                // 確實更新清單中所有項目的高度與高程！
                UpdateElevationItemsGeometry();
            }
        }

        private void OnGenerate()
        {
            _externalEventHandler.SetAction(() =>
            {
                try
                {
                    StatusText = "開始一鍵自動生成流程...";

                    _tempCreatedViews.Clear();
                    _isStep2Ok = false;
                    _isStep3Ok = false;
                    _isStep4Ok = false;

                    var selectedItems = ElevationItems.Where(x => x.IsSelected).ToList();
                    if (selectedItems.Count == 0)
                    {
                        MessageBox.Show("目前清單中沒有被勾選的剖面項目！請勾選至少一個剖面再執行生成。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 1. 建立視圖
                    using (var tx = new Transaction(_doc, "DT: Create Section Views"))
                    {
                        tx.Start();
                        _viewToDataMap.Clear();
                        _viewToSettingsMap.Clear();
                        foreach (var item in selectedItems)
                        {
                            var tempSettings = new GeneratorSettings
                            {
                                ViewDepth = item.ViewDepth,
                                WallOffset = item.WallOffset,
                                SideExtension = item.SideExtension,
                                TopOffset = item.TopOffset,
                                BottomOffset = item.BottomOffset,
                                FlipDirection = item.FlipDirection,
                                SelectedViewTemplateId = Settings.SelectedViewTemplateId
                            };

                            var view = WallElevationViewCreator.CreateElevationView(_doc, item.GeometryData, tempSettings, item.ViewName);
                            if (view != null)
                            {
                                _tempCreatedViews.Add(view);
                                _viewToDataMap[view.Id] = item.GeometryData;
                                _viewToSettingsMap[view.Id] = tempSettings;
                            }
                        }
                        tx.Commit();
                        _isStep2Ok = true;

                        // 彙整剖面高度與寬度報告
                        var reportLines = new List<string>();
                        foreach (var view in _tempCreatedViews)
                        {
                            try
                            {
                                BoundingBoxXYZ box = view.CropBox;
                                WallElevationData data = null;
                                GeneratorSettings tempSettings = null;
                                _viewToDataMap.TryGetValue(view.Id, out data);
                                _viewToSettingsMap.TryGetValue(view.Id, out tempSettings);
                                double originZ = (data != null && tempSettings != null) ? 
                                    (data.LevelElevation + (data.WallHeight + tempSettings.TopOffset / 304.8 - tempSettings.BottomOffset / 304.8) / 2.0) : 
                                    view.Origin.Z;
                                double minZ = (originZ + box.Min.Y) * 304.8;
                                double maxZ = (originZ + box.Max.Y) * 304.8;
                                double width = (box.Max.X - box.Min.X) * 304.8;

                                reportLines.Add($"• {view.Name}:\n" +
                                                $"  高程範圍: {minZ:F0} ~ {maxZ:F0} mm (高度: {maxZ - minZ:F0} mm)\n" +
                                                $"  視圖寬度: {width:F0} mm");
                            }
                            catch {}
                        }
                        if (reportLines.Count > 0)
                        {
                            string summary = "已成功建立以下剖面視圖：\n\n" + string.Join("\n\n", reportLines) + 
                                             "\n\n※ 請確認高程與寬度數值是否完全正確。";
                            TaskDialog.Show("剖面建立成功報告", summary);
                        }
                    }

                    // 2. 套用樣板
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
                            _doc.Regenerate(); // 必須在套用樣板後，強迫更新幾何狀態，防止 CropBox 被延遲更新覆蓋

                            foreach (var view in _tempCreatedViews)
                            {
                                WallElevationData data = null;
                                GeneratorSettings tempSettings = null;
                                _viewToDataMap.TryGetValue(view.Id, out data);
                                _viewToSettingsMap.TryGetValue(view.Id, out tempSettings);
                                if (data != null && tempSettings != null)
                                {
                                    WallElevationViewCreator.ReApplyCropBox(view, data, tempSettings);
                                }
                            }
                            tx.Commit();
                            _isStep3Ok = true;
                        }
                    }
                    else
                    {
                        _isStep3Ok = true;
                    }

                    // 3. 視圖名稱已在建立時直接給定，故無需重新命名，直接標記為完成
                    _isStep4Ok = true;

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
                                    BoundingBoxXYZ box = view.CropBox;
                                    double originZ = box.Transform.Origin.Z;
                                    double basisYZ = box.Transform.BasisY.Z;
                                    if (Math.Abs(basisYZ) < 0.001) basisYZ = 1.0;
                                    double currentCenterZ = originZ + ((box.Min.Y + box.Max.Y) / 2.0) * basisYZ;
                                    yFloorOffset = (data.LevelElevation - currentCenterZ) / scale;
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
            AnalyzeSourceAndFillItems();
        }

        private void OnStep2Create()
        {
            var selectedItems = ElevationItems.Where(x => x.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("目前清單中沒有被勾選的剖面項目！請勾選至少一個剖面再執行建立。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _externalEventHandler.SetAction(() =>
            {
                using (var tx = new Transaction(_doc, "DT: Create Section Views"))
                {
                    try
                    {
                        tx.Start();
                        _tempCreatedViews.Clear();
                        _viewToDataMap.Clear();
                        _viewToSettingsMap.Clear();

                        foreach (var item in selectedItems)
                        {
                            var tempSettings = new GeneratorSettings
                            {
                                ViewDepth = item.ViewDepth,
                                WallOffset = item.WallOffset,
                                SideExtension = item.SideExtension,
                                TopOffset = item.TopOffset,
                                BottomOffset = item.BottomOffset,
                                FlipDirection = item.FlipDirection,
                                SelectedViewTemplateId = Settings.SelectedViewTemplateId
                            };

                            var view = WallElevationViewCreator.CreateElevationView(_doc, item.GeometryData, tempSettings, item.ViewName);
                            if (view != null)
                            {
                                _tempCreatedViews.Add(view);
                                _viewToDataMap[view.Id] = item.GeometryData;
                                _viewToSettingsMap[view.Id] = tempSettings;
                            }
                        }

                        tx.Commit();
                        _isStep2Ok = true;

                        // 彙整剖面高度與寬度報告
                        var reportLines = new List<string>();
                        foreach (var view in _tempCreatedViews)
                        {
                            try
                            {
                                BoundingBoxXYZ box = view.CropBox;
                                WallElevationData data = null;
                                GeneratorSettings tempSettings = null;
                                _viewToDataMap.TryGetValue(view.Id, out data);
                                _viewToSettingsMap.TryGetValue(view.Id, out tempSettings);
                                double originZ = box.Transform.Origin.Z;
                                double basisYZ = box.Transform.BasisY.Z;
                                if (Math.Abs(basisYZ) < 0.001) basisYZ = 1.0;
                                double minZ = (originZ + box.Min.Y * basisYZ) * 304.8;
                                double maxZ = (originZ + box.Max.Y * basisYZ) * 304.8;
                                double width = (box.Max.X - box.Min.X) * 304.8;

                                reportLines.Add($"• {view.Name}:\n" +
                                                $"  高程範圍: {minZ:F0} ~ {maxZ:F0} mm (高度: {maxZ - minZ:F0} mm)\n" +
                                                $"  視圖寬度: {width:F0} mm");
                            }
                            catch {}
                        }
                        if (reportLines.Count > 0)
                        {
                            string summary = "已成功建立以下剖面視圖：\n\n" + string.Join("\n\n", reportLines) + 
                                             "\n\n※ 請確認高程與寬度數值是否完全正確。";
                            TaskDialog.Show("剖面建立成功報告", summary);
                        }

                        StatusText = $"[步驟 2 成功] 成功建立 {_tempCreatedViews.Count} 個剖面視圖。請執行 [步驟 3] 或 [步驟 5] 建立圖紙。";
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
                        }
                        _doc.Regenerate(); // 必須在套用樣板後，強迫更新幾何狀態，防止 CropBox 被延遲更新覆蓋

                        foreach (var view in _tempCreatedViews)
                        {
                            WallElevationData data = null;
                            GeneratorSettings tempSettings = null;
                            _viewToDataMap.TryGetValue(view.Id, out data);
                            _viewToSettingsMap.TryGetValue(view.Id, out tempSettings);
                            if (data != null && tempSettings != null)
                            {
                                WallElevationViewCreator.ReApplyCropBox(view, data, tempSettings);
                            }

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
                                    BoundingBoxXYZ box = view.CropBox;
                                    double originZ = box.Transform.Origin.Z;
                                    double basisYZ = box.Transform.BasisY.Z;
                                    if (Math.Abs(basisYZ) < 0.001) basisYZ = 1.0;
                                    double currentCenterZ = originZ + ((box.Min.Y + box.Max.Y) / 2.0) * basisYZ;
                                    yFloorOffset = (data.LevelElevation - currentCenterZ) / scale;
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
            string title = DevelopmentTools.Core.LanguageManager.Instance["Tut_TileElev_Title"];
            string content = DevelopmentTools.Core.LanguageManager.Instance["Tut_TileElev_Content"];
            Autodesk.Revit.UI.TaskDialog td = new Autodesk.Revit.UI.TaskDialog(title)
            {
                TitleAutoPrefix = false,
                MainInstruction = title,
                MainContent = content,
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Close
            };
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
