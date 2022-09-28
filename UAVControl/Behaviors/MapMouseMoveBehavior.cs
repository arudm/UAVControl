using MapControl;
using Microsoft.Xaml.Behaviors;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UAVControl.Models;

namespace UAVControl.Behaviors
{
    public class MapMouseMoveBehavior : Behavior<Map>
    {
        Map? _map;
        OutlinedText? _mouseLocation;
        protected override void OnAttached()
        {
            AssociatedObject.MouseMove += MouseMove;
            AssociatedObject.MouseLeave += MouseLeave;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.MouseMove -= MouseMove;
            AssociatedObject.MouseLeave -= MouseLeave;
        }

        private void MouseMove(object sender, MouseEventArgs e)
        {
            _map = AssociatedObject;
            _mouseLocation = FindVisualRootforOutlinedText(AssociatedObject) as OutlinedText;

            var location = _map.ViewToLocation((Point)e.GetPosition(_map));

            if (location != null)
            {
                var latitude = (int)Math.Round(location.Latitude * 60000d);
                var longitude = (int)Math.Round(Location.NormalizeLongitude(location.Longitude) * 60000d);
                var latHemisphere = 'N';
                var lonHemisphere = 'E';

                if (latitude < 0)
                {
                    latitude = -latitude;
                    latHemisphere = 'S';
                }

                if (longitude < 0)
                {
                    longitude = -longitude;
                    lonHemisphere = 'W';
                }

                _mouseLocation.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0}  {1:00} {2:00.000}\n{3} {4:000} {5:00.000}",
                    latHemisphere, latitude / 60000, (latitude % 60000) / 1000d,
                    lonHemisphere, longitude / 60000, (longitude % 60000) / 1000d);
            }
            else
            {
                _mouseLocation.Text = string.Empty;
            }
        }

        private void MouseLeave(object sender, MouseEventArgs e)
        {
            _mouseLocation = FindVisualRootforOutlinedText(AssociatedObject) as OutlinedText;
            _mouseLocation.Text = string.Empty;
        }

        private static DependencyObject FindVisualRootforOutlinedText(DependencyObject obj)
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
                        if (child is OutlinedText)
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
