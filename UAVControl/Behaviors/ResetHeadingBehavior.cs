using MapControl;
using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UAVControl.Models;

namespace UAVControl.Behaviors
{
    public class ResetHeadingBehavior : Behavior<Button>
    {
        Map? _map;
        protected override void OnAttached()
        {
            AssociatedObject.Click += ResetHeadingClickBehavior;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.MouseMove -= ResetHeadingClickBehavior;
        }
        private void ResetHeadingClickBehavior(object sender, RoutedEventArgs e)
        {
            var _map = FindVisualRootFromResetHeadingButton(AssociatedObject) as Map;
                
            if (_map != null)
            {
                _map.TargetHeading = 0d;
            }
        }

        private object FindVisualRootFromResetHeadingButton(DependencyObject obj)
        {
            do
            {
                var parent = VisualTreeHelper.GetParent(obj);
                if (parent is Grid)
                {
                    var count = VisualTreeHelper.GetChildrenCount(parent);
                    for (int i = 0; i < count; i++)
                    {
                        var child = VisualTreeHelper.GetChild(parent, i);
                        if (child is Map)
                        {
                            return child;
                        }
                    }
                }
                obj = parent;
            } while (true);
        }
    }
}
