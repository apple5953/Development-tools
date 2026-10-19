namespace BIMDrawingFactory.UI.Models
{
    public enum FloorStatus
    {
        NotCreated,
        Completed,
        Partial,
        Outdated,
        Warning,
        Error
    }

    public enum UiItemStatus
    {
        Success,      // 🟢 可以建立
        Warning,      // 🟡 需要確認
        Error,        // 🔴 無法建立
        Inactive,     // ⚪ 尚未建立 / 跳過
        Completed     // 🔵 已完成
    }

    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum LevelBindingOption
    {
        CurrentLevel,   // 本樓層
        PreviousLevel,  // 樓下一層作為基準 (見上：由樓下往上看)
        NextLevel,      // 樓上一層作為基準（保留供特殊規則使用）
        FixedLevel,     // 指定固定樓層
        Unbound,        // 不依附樓層
        AskBeforeRun    // 建立前詢問
    }

    public enum SheetCreationType
    {
        StandardFloorPackage, // 標準樓層整套圖紙
        CustomSelection       // 自訂圖紙套組
    }

    public enum ResourceMappingType
    {
        Level,
        ScopeBox,
        ViewTemplate,
        TitleBlock,
        LegendSchedule
    }

    public enum NavigationPageKey
    {
        QuickGenerate,
        DrawingProgress,
        StandardDrawing,
        DrawingRules,
        Mapping,
        Preflight,
        Preview,
        GenerationResult,
        History,
        Settings,
        SetupWizard
    }
}
