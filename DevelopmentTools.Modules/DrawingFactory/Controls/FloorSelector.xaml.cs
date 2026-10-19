using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using BIMDrawingFactory.UI.Models;

namespace BIMDrawingFactory.UI.Controls
{
    public partial class FloorSelector : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(FloorSelector),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        public event EventHandler<FloorChipDto> FloorSelectionToggled;

        public FloorSelector()
        {
            BIMDrawingFactory.UI.Services.ResourceHelper.LoadVersionedComponent(this);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var selector = d as FloorSelector;
            if (selector != null)
            {
                selector.FloorItemsControl.ItemsSource = e.NewValue as IEnumerable;
            }
        }

        private void OnFloorChipClicked(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var chip = btn.DataContext as FloorChipDto;
                if (chip != null && FloorSelectionToggled != null)
                {
                    FloorSelectionToggled(this, chip);
                }
            }
        }
    }
}
