using Newtonsoft.Json;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace iRacingReplayDirector
{
    internal static class WindowPlacement
    {
        internal static void Remember(Form window)
        {
            var key = window.GetType().FullName;
            Point position;
            var hasPosition = Load().TryGetValue(key, out position);
            if (!hasPosition && window is Main)
            {
                var previousPosition = Settings.Default.MainWindowPosition;
                hasPosition = previousPosition.HasValue;
                position = previousPosition.GetValueOrDefault();
            }

            if (hasPosition && Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(position)))
            {
                window.StartPosition = FormStartPosition.Manual;
                window.Location = position;
            }

            window.FormClosing += (sender, args) => Save(window);
        }

        private static Dictionary<string, Point> Load()
        {
            var saved = Settings.Default.WindowPositions;
            if (!string.IsNullOrWhiteSpace(saved))
            {
                try
                {
                    return JsonConvert.DeserializeObject<Dictionary<string, Point>>(saved) ?? new Dictionary<string, Point>();
                }
                catch (JsonException) { }
            }
            return new Dictionary<string, Point>();
        }

        internal static void Save(Form window)
        {
            var bounds = window.WindowState == FormWindowState.Normal ? window.Bounds : window.RestoreBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var key = window.GetType().FullName;
            var positions = Load();
            Point previousPosition;
            if (positions.TryGetValue(key, out previousPosition) && previousPosition == bounds.Location)
                return;

            positions[key] = bounds.Location;
            Settings.Default.WindowPositions = JsonConvert.SerializeObject(positions);
            Settings.Default.Save();
        }
    }
}
