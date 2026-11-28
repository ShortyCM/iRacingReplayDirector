using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Result = iRacingSDK.SessionData._SessionInfo._Sessions._ResultsPositions;

namespace iRacingDirector.Plugin.F1Style
{
    public partial class MyPlugin
    {
        private const int ResultsPageSize = 11;

        public void IntroFlashCard(long duration, long timestamp)
        {
            DrawClassification(true, duration, timestamp);
        }

        public void OutroFlashCard(long duration, long timestamp)
        {
            DrawClassification(false, duration, timestamp);
        }

        private void DrawClassification(bool qualifying, long duration, long timestamp)
        {
            ReadSession();
            var session = EventData.Raw.SessionInfo.Sessions.FirstOrDefault(s => s.SessionType != null &&
                s.SessionType.IndexOf(qualifying ? "qualif" : "race", StringComparison.OrdinalIgnoreCase) >= 0);
            if (session == null || session.ResultsPositions == null)
                return;

            var results = session.ResultsPositions.Where(row => row.Position > 0 && DriverAt(row.CarIdx) != null)
                .OrderBy(row => row.Position).ToArray();
            if (results.Length == 0)
                return;

            var pageCount = (results.Length + ResultsPageSize - 1) / ResultsPageSize;
            var progress = duration > 0 ? Math.Min(1, Math.Max(0, (double)timestamp / duration)) : 0;
            var page = Math.Min(pageCount - 1, (int)(progress * pageCount));
            OnCanvas(() =>
            {
                Block(Black, new RectangleF(278, 121, 724, 47));
                Text(EventData.WeekendInfo.TrackDisplayName, Title, Color.White, new RectangleF(294, 121, 690, 47));
                Block(Black, new RectangleF(278, 174, 724, 32));
                Text(qualifying ? "Qualifying Classification" : "Race Classification", Bold, Color.White,
                    new RectangleF(294, 174, 565, 32));
                if (pageCount > 1)
                    Text((page + 1) + " / " + pageCount, Regular, Muted, new RectangleF(866, 174, 119, 32), RightText);

                var entries = results.Skip(page * ResultsPageSize).Take(ResultsPageSize).ToArray();
                for (var i = 0; i < entries.Length; i++)
                {
                    var result = entries[i];
                    var driver = DriverAt(result.CarIdx);
                    var position = (int)result.Position;
                    var row = new RectangleF(278, 212 + 32 * i, 724, 27);
                    DriverRow(row, position, Accent(driver, position), i % 2 == 0 ? Dark : Grey);
                    Text("#" + driver.CarNumber, Regular, Muted, new RectangleF(335, row.Y, 59, row.Height));
                    Text(driver.UserName, Regular, Color.White, new RectangleF(394, row.Y, multiclass ? 287 : 423, row.Height));
                    if (multiclass)
                    {
                        var label = ClassLabel(driver);
                        if (result.ClassPosition >= 0)
                            label += "  " + (result.ClassPosition + 1).ToString(CultureInfo.InvariantCulture);
                        Text(label, Regular, Muted, new RectangleF(687, row.Y, 139, row.Height));
                    }
                    Text(ClassificationTime(result, results[0], qualifying), Regular, Color.White,
                        new RectangleF(829, row.Y, 157, row.Height), RightText);
                }

                var footerTop = 212 + entries.Length * 32 - 1;
                Block(Black, new RectangleF(278, footerTop, 724, 30));
                Text(qualifying ? QualifyingFooter() : RaceFooter(results), Regular, Color.White,
                    new RectangleF(294, footerTop, 691, 30));
            });
        }

        private string QualifyingFooter()
        {
            var track = EventData.WeekendInfo;
            var location = string.Join(", ", new[] { track.TrackCity, track.TrackCountry }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.Join("   ·   ", new[] { track.TrackLength, location }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private string RaceFooter(Result[] results)
        {
            var fastest = results.Where(result => result.FastestTime > 0)
                .OrderBy(result => result.FastestTime).FirstOrDefault();
            if (fastest == null)
                return "";
            return "Fastest Lap   " + DriverAt(fastest.CarIdx).UserName + "   " + SecondsText(fastest.FastestTime);
        }

        private static string ClassificationTime(Result row, Result leader, bool qualifying)
        {
            if (qualifying)
            {
                if (row.FastestTime <= 0)
                    return "–";
                return row == leader || leader.FastestTime <= 0
                    ? SecondsText(row.FastestTime) : "+" + SecondsText(Math.Max(0, row.FastestTime - leader.FastestTime));
            }

            if (row.ReasonOutId != 0)
                return string.IsNullOrWhiteSpace(row.ReasonOutStr) ? "DNF" : row.ReasonOutStr;
            if (row == leader)
                return row.Time > 0 ? TimeSpan.FromSeconds(row.Time).ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture) : "–";
            if (leader.LapsComplete > row.LapsComplete)
                return "+" + (leader.LapsComplete - row.LapsComplete) + "L";
            return row.Time >= leader.Time && row.Time > 0 ? "+" + SecondsText(row.Time - leader.Time) : "–";
        }
    }
}
