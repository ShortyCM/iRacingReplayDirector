using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using iRacingDirector.Plugin;
using SessionDriver = iRacingSDK.SessionData._DriverInfo._Drivers;

namespace iRacingDirector.Plugin.F1Style
{
    public partial class MyPlugin
    {
        public Graphics Graphics;
        public EventData EventData;
        public LeaderBoard LeaderBoard;
        public Driver CamDriver;
        public FastLap FastLap;
        public BroadcastData Broadcast;
        public string ClassLabels;

        private static readonly Color Black = Color.FromArgb(242, 8, 8, 8);
        private static readonly Color Dark = Color.FromArgb(238, 26, 26, 26);
        private static readonly Color Grey = Color.FromArgb(238, 49, 49, 49);
        private static readonly Color PositionGrey = Color.FromArgb(126, 126, 126);
        private static readonly Color Muted = Color.FromArgb(208, 208, 208);
        private static readonly Color Purple = Color.FromArgb(220, 104, 221);
        private static readonly Color[] Accents =
        {
            Color.FromArgb(62, 216, 204), Color.FromArgb(217, 30, 35), Color.FromArgb(51, 58, 202),
            Color.FromArgb(255, 87, 45), Color.FromArgb(250, 210, 19)
        };
        private static readonly Font Small = new Font("Arial", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font Regular = new Font("Arial", 15, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font Bold = new Font("Arial", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        private static readonly Font Title = new Font("Arial", 30, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly StringFormat LeftText = TextFormat(StringAlignment.Near);
        private static readonly StringFormat CentreText = TextFormat(StringAlignment.Center);
        private static readonly StringFormat RightText = TextFormat(StringAlignment.Far);

        private EventData currentEvent;
        private Dictionary<long, SessionDriver> drivers;
        private Dictionary<long, ClassStyle> styles;
        private bool multiclass;

        private sealed class ClassStyle
        {
            public string Label;
            public Color Colour;
        }

        private void ReadSession()
        {
            if (ReferenceEquals(currentEvent, EventData) && drivers != null)
                return;

            currentEvent = EventData;
            var entries = EventData.CompetingDrivers.Where(d => !d.IsPaceCar && d.IsSpectator == 0);
            drivers = entries.GroupBy(d => d.CarIdx).ToDictionary(d => d.Key, d => d.First());
            var groups = drivers.Values.GroupBy(d => d.CarClassID).ToArray();
            multiclass = groups.Length > 1 || EventData.WeekendInfo.NumCarClasses > 1;
            var overrides = new Dictionary<long, string>();
            foreach (var item in (ClassLabels ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = item.Split(new[] { '=' }, 2);
                long id;
                if (parts.Length == 2 && long.TryParse(parts[0], out id) && !string.IsNullOrWhiteSpace(parts[1]))
                    overrides[id] = parts[1].Trim();
            }

            styles = new Dictionary<long, ClassStyle>();
            foreach (var group in groups)
            {
                var colour = group.Select(d => d.CarClassColor).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var name = group.Select(d => d.CarClassShortName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                // These car-model labels come from the supplied Kamel GT iRTVO setup.
                if (name == null && group.All(d => d.CarID == 76))
                    name = "GTO";
                if (name == null && group.All(d => d.CarID == 77))
                    name = "GTP";

                string label;
                styles[group.Key] = new ClassStyle
                {
                    Label = overrides.TryGetValue(group.Key, out label) ? label :
                        name ?? group.Key.ToString(CultureInfo.InvariantCulture),
                    Colour = ParseColour(colour, Accents[styles.Count % Accents.Length])
                };
            }
            ResetLapTimes();
        }

        private static Color ParseColour(string value, Color fallback)
        {
            int rgb;
            if (value != null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb) && rgb != 0)
                return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

            return fallback;
        }

        private SessionDriver DriverAt(long carIdx)
        {
            SessionDriver driver;
            return drivers.TryGetValue(carIdx, out driver) ? driver : null;
        }

        private string ClassLabel(SessionDriver driver)
        {
            ClassStyle style;
            return driver != null && styles.TryGetValue(driver.CarClassID, out style) ? style.Label : "";
        }

        private Color Accent(SessionDriver driver, int position)
        {
            ClassStyle style;
            return multiclass && driver != null && styles.TryGetValue(driver.CarClassID, out style)
                ? style.Colour : Accents[Math.Max(0, position - 1) % Accents.Length];
        }

        private void OnCanvas(Action draw)
        {
            var bounds = Graphics.VisibleClipBounds;
            var scale = Math.Min(bounds.Width / 1280f, bounds.Height / 720f);
            if (scale <= 0)
                return;

            var state = Graphics.Save();
            try
            {
                Graphics.TranslateTransform(bounds.Left + (bounds.Width - 1280 * scale) / 2,
                    bounds.Top + (bounds.Height - 720 * scale) / 2);
                Graphics.ScaleTransform(scale, scale);
                Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                draw();
            }
            finally
            {
                Graphics.Restore(state);
            }
        }

        private static StringFormat TextFormat(StringAlignment alignment)
        {
            return new StringFormat
            {
                Alignment = alignment,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            };
        }

        private void Text(string value, Font font, Color colour, RectangleF area, StringFormat format = null)
        {
            if (string.IsNullOrEmpty(value) || area.Width <= 0)
                return;
            using (var brush = new SolidBrush(colour))
                Graphics.DrawString(value, font, brush, area, format ?? LeftText);
        }

        private void Block(Color colour, RectangleF area)
        {
            using (var brush = new SolidBrush(colour))
                Graphics.FillRectangle(brush, area);
        }

        private void Stripe(RectangleF area, Color colour, int width)
        {
            const int slope = 13;
            var edge = area.Left + width;
            using (var brush = new SolidBrush(PositionGrey))
                Graphics.FillPolygon(brush, new[] { new PointF(area.Left, area.Top), new PointF(edge, area.Top),
                    new PointF(edge - slope, area.Bottom), new PointF(area.Left, area.Bottom) });
            using (var brush = new SolidBrush(colour))
                Graphics.FillPolygon(brush, new[] { new PointF(edge, area.Top), new PointF(edge + 5, area.Top),
                    new PointF(edge - slope + 5, area.Bottom), new PointF(edge - slope, area.Bottom) });
        }

        private void DriverRow(RectangleF area, int position, Color colour, Color background, int stripeWidth = 47)
        {
            Block(background, area);
            Stripe(area, colour, stripeWidth);
            Text(position > 0 ? position.ToString(CultureInfo.InvariantCulture) : "–", Bold, Color.White,
                new RectangleF(area.X, area.Y, stripeWidth - 12, area.Height), CentreText);
        }

        private static int LatestAt<T>(IList<T> items, double seconds, Func<T, double> startTime)
        {
            var lo = 0;
            var hi = items.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (startTime(items[mid]) <= seconds)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo - 1;
        }

        private static string SecondsText(double seconds)
        {
            return seconds >= 60
                ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture)
                : seconds.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";
            var separator = name.LastIndexOf(' ');
            return separator > 0 ? name[0] + ". " + name.Substring(separator + 1) : name;
        }
    }
}
