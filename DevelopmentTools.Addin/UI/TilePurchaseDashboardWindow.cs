using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DevelopmentTools.UI
{
	public class TilePurchaseDashboardItem
	{
		public string Room { get; set; }
		public string Surface { get; set; }
		public string Material { get; set; }
		public string Size { get; set; }
		public string SurfaceAreaText { get; set; }
		public string NetQtyText { get; set; }
		public string PurchaseQtyText { get; set; }
		public string WasteText { get; set; }
		public string CheckScheduleStatus { get; set; }
		public string PurchaseScheduleStatus { get; set; }
		public string CalculationMethod { get; set; }
		public int PartCount { get; set; }
	}

	public class TilePurchaseDashboardWindow : Window
	{
		public TilePurchaseDashboardWindow(IList<TilePurchaseDashboardItem> items)
		{
			List<TilePurchaseDashboardItem> rows = (items ?? new List<TilePurchaseDashboardItem>()).ToList();
			Title = "磁磚採購表 / 檢查";
			Width = 1180;
			Height = 680;
			MinWidth = 860;
			MinHeight = 480;
			WindowStartupLocation = WindowStartupLocation.CenterScreen;
			Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
			Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));

			Grid root = new Grid { Margin = new Thickness(14) };
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			int missingCheck = rows.Count(row => row.CheckScheduleStatus != "已建立");
			int missingPurchase = rows.Count(row => row.PurchaseScheduleStatus != "已建立");
			TextBlock summary = new TextBlock
			{
				Text = string.Format("介面彙總 {0} 筆；未建立檢核表 {1} 筆；未建立採購表 {2} 筆", rows.Count, missingCheck, missingPurchase),
				Margin = new Thickness(0, 0, 0, 10),
				FontSize = 14,
				FontWeight = FontWeights.SemiBold
			};
			root.Children.Add(summary);

			DataGrid grid = new DataGrid
			{
				AutoGenerateColumns = false,
				IsReadOnly = true,
				CanUserAddRows = false,
				CanUserDeleteRows = false,
				SelectionMode = DataGridSelectionMode.Single,
				GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
				HeadersVisibility = DataGridHeadersVisibility.Column,
				Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
				Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
				RowBackground = new SolidColorBrush(Color.FromRgb(37, 39, 52)),
				AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(45, 48, 63)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
				ItemsSource = rows
			};
			AddColumn(grid, "空間", "Room", 150);
			AddColumn(grid, "位置", "Surface", 80);
			AddColumn(grid, "磁磚材質", "Material", 150);
			AddColumn(grid, "規格", "Size", 90);
			AddColumn(grid, "鋪貼面積(m2)", "SurfaceAreaText", 100);
			AddColumn(grid, "淨面積片數", "NetQtyText", 100);
			AddColumn(grid, "採購片數", "PurchaseQtyText", 90);
			AddColumn(grid, "浪費片數/率", "WasteText", 110);
			AddColumn(grid, "檢核表", "CheckScheduleStatus", 80);
			AddColumn(grid, "採購表", "PurchaseScheduleStatus", 80);
			AddColumn(grid, "Parts", "PartCount", 64);
			AddColumn(grid, "計算方式", "CalculationMethod", 260);

			Grid.SetRow(grid, 1);
			root.Children.Add(grid);

			StackPanel footer = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				HorizontalAlignment = HorizontalAlignment.Right,
				Margin = new Thickness(0, 10, 0, 0)
			};
			Button closeButton = new Button
			{
				Content = "關閉",
				Width = 96,
				Height = 30,
				Margin = new Thickness(8, 0, 0, 0)
			};
			closeButton.Click += (sender, args) => Close();
			footer.Children.Add(closeButton);
			Grid.SetRow(footer, 2);
			root.Children.Add(footer);

			Content = root;
		}

		private static void AddColumn(DataGrid grid, string header, string bindingPath, double width)
		{
			grid.Columns.Add(new DataGridTextColumn
			{
				Header = header,
				Binding = new Binding(bindingPath),
				Width = width
			});
		}
	}
}
