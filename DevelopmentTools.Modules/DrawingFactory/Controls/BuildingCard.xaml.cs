using System;
using System.Windows;
using System.Windows.Controls;
using BIMDrawingFactory.UI.Models;

namespace BIMDrawingFactory.UI.Controls
{
    public partial class BuildingCard : UserControl
    {
        public static readonly DependencyProperty BuildingDataProperty =
            DependencyProperty.Register("BuildingData", typeof(BuildingCardDto), typeof(BuildingCard),
                new PropertyMetadata(null));

        public BuildingCardDto BuildingData
        {
            get { return (BuildingCardDto)GetValue(BuildingDataProperty); }
            set { SetValue(BuildingDataProperty, value); }
        }

        public event EventHandler<BuildingCardDto> CardSelected;

        public BuildingCard()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }

        private void OnCardClicked(object sender, RoutedEventArgs e)
        {
            if (BuildingData != null && CardSelected != null)
            {
                CardSelected(this, BuildingData);
            }
        }
    }
}
