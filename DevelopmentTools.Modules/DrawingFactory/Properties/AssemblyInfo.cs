using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("BIM Drawing Factory UI")]
[assembly: AssemblyDescription("Revit 2024 drawing and view generation UI")]
[assembly: AssemblyCompany("BIM_FACTORY")]
[assembly: AssemblyProduct("BIM Drawing Factory")]
[assembly: ComVisible(false)]

// 每次建置產生不同 Build/Revision，讓同一 Revit AppDomain 中的熱載入組件
// 具有不同 CLR/WPF 資源身分，避免 pack URI 取到上一版 BAML。
[assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyFileVersion("1.0.0.0")]
