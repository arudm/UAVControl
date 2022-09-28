using MapControl;
using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Input;

namespace UAVControl.Behaviors.Properties
{
    public class MapItemTouchDownProperty
    {

        // Using a DependencyProperty as the backing store for MyProperty.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TouchDownBehaviorProperty =
            DependencyProperty.RegisterAttached("TouchDownBehavior", typeof(Behavior<MapItem>), typeof(MapItemTouchDownProperty), new PropertyMetadata(null));

        public static Behavior<MapItem> GetTouchDownBehavior(DependencyObject obj)
        {
            return (Behavior<MapItem>)obj.GetValue(TouchDownBehaviorProperty);
        }

        public static void SetTouchDownBehavior(DependencyObject obj, Behavior<MapItem> value)
        {
            obj.SetValue(TouchDownBehaviorProperty, value);
        }



    }
}
