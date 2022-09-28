using MapControl;
using Microsoft.Xaml.Behaviors;
using System.Windows.Input;

namespace UAVControl.Behaviors
{
    public class MapItemTouchDownBehavior : Behavior<MapItem>
    {

        protected override void OnAttached()
        {
            AssociatedObject.TouchDown += TouchDownBehavior;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.TouchDown -= TouchDownBehavior;
        }
        private void TouchDownBehavior(object? sender, TouchEventArgs e)
        {
            var mapItem = sender as MapItem;
            if (mapItem != null)
            {
                mapItem.IsSelected = !mapItem.IsSelected;
                e.Handled = true;
            }
        }
    }
}
