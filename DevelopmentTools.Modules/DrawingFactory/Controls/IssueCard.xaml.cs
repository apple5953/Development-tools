using System.Windows;
using System.Windows.Controls;
using BIMDrawingFactory.UI.Models;

namespace BIMDrawingFactory.UI.Controls
{
    public partial class IssueCard : UserControl
    {
        public static readonly DependencyProperty IssueDataProperty =
            DependencyProperty.Register("IssueData", typeof(IssueItemDto), typeof(IssueCard),
                new PropertyMetadata(null, OnIssueDataChanged));

        public IssueItemDto IssueData
        {
            get { return (IssueItemDto)GetValue(IssueDataProperty); }
            set { SetValue(IssueDataProperty, value); }
        }

        public IssueCard()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }

        private static void OnIssueDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var card = d as IssueCard;
            if (card != null)
            {
                card.UpdateData();
            }
        }

        private void UpdateData()
        {
            if (IssueData == null) return;

            TxtFloorName.Text = IssueData.FloorName;
            TxtSheetName.Text = IssueData.SheetOrViewName;
            TxtUserMessage.Text = IssueData.UserMessage;
            TxtSuggestedAction.Text = IssueData.SuggestedAction;
            TxtTechnicalDetails.Text = IssueData.TechnicalDetails;

            switch (IssueData.Severity)
            {
                case IssueSeverity.Error:
                    SeverityBadge.Status = UiItemStatus.Error;
                    SeverityBadge.CustomText = "無法建立";
                    break;
                case IssueSeverity.Warning:
                    SeverityBadge.Status = UiItemStatus.Warning;
                    SeverityBadge.CustomText = "需要確認";
                    break;
                default:
                    SeverityBadge.Status = UiItemStatus.Inactive;
                    SeverityBadge.CustomText = "資訊提醒";
                    break;
            }

            CmbResolutions.ItemsSource = IssueData.ResolutionOptions;
            if (!string.IsNullOrEmpty(IssueData.SelectedResolution))
            {
                CmbResolutions.SelectedItem = IssueData.SelectedResolution;
            }
            else if (IssueData.ResolutionOptions.Count > 0)
            {
                CmbResolutions.SelectedIndex = 0;
            }
        }

        private void OnResolutionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IssueData != null && CmbResolutions.SelectedItem is string)
            {
                IssueData.SelectedResolution = (string)CmbResolutions.SelectedItem;
            }
        }
    }
}
