using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
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
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFloorMode));
            }
        }

        // 用於儲存 Revit 選定物件
        public Floor SelectedFloor { get; private set; }
        public List<Wall> SelectedWalls { get; private set; } = new List<Wall>();

        // 用於控制視窗顯示/隱藏的委派事件
        public Action RequestHide { get; set; }
        public Action RequestShow { get; set; }
        public Action RequestClose { get; set; }

        public ICommand SelectSourceCommand { get; }
        public ICommand GenerateCommand { get; }

        public DT_TileElevationGeneratorViewModel(ExternalCommandData commandData)
        {
            _commandData = commandData;
            _uidoc = commandData.Application.ActiveUIDocument;
            _doc = _uidoc.Document;

            // 1. 取得 Section View Templates
            ViewTemplates = ViewTemplateSelector.GetSectionViewTemplates(_doc);
            if (ViewTemplates.Count > 0)
            {
                _selectedTemplate = ViewTemplates[0];
                Settings.SelectedViewTemplateId = _selectedTemplate.Id;
            }

            // 2. 初始化 Commands
            SelectSourceCommand = new RelayCommand(OnSelectSource);
            GenerateCommand = new RelayCommand(OnGenerate);
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
            try
            {
                var service = new DT_TileElevationGeneratorService();
                TileElevationResult result;

                if (IsFloorMode)
                {
                    if (SelectedFloor == null)
                    {
                        MessageBox.Show("Please select a Floor first!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    StatusText = "Generating elevations (Floor Mode)...";
                    result = service.GenerateElevationsForFloor(_doc, SelectedFloor, Settings);
                }
                else
                {
                    if (SelectedWalls.Count == 0)
                    {
                        MessageBox.Show("Please select at least one Wall first!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    StatusText = "Generating elevations (Wall Mode)...";
                    result = service.GenerateElevationsForWalls(_doc, SelectedWalls, Settings);
                }

                if (result.Success)
                {
                    string msg = $"Successfully created {result.CreatedViewsCount} Elevation Section Views:\n" +
                                 string.Join("\n", result.CreatedViewNames);
                    if (result.SkippedWallsCount > 0)
                    {
                        msg += $"\n\nSkipped {result.SkippedWallsCount} walls (e.g. too short or duplicate name).";
                    }

                    MessageBox.Show(msg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    RequestClose?.Invoke();
                }
                else
                {
                    MessageBox.Show($"Failed to generate elevations:\n{result.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText = "Generation failed.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = "Error occurred.";
            }
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
