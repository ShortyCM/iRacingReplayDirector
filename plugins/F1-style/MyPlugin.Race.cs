using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using iRacingDirector.Plugin;

namespace iRacingDirector.Plugin.F1Style
{
    public partial class MyPlugin
    {
        private const int GapBarTop = 658;
        private const int GapBarHeight = 27;

        private sealed class Standing
        {
            public Driver Driver;
            public int ClassPosition;
        }

        private readonly Dictionary<long, LapTimeUpdate> classRecords = new Dictionary<long, LapTimeUpdate>();
        private int nextLapUpdate;
        private double classRecordsThrough;

        public void RaceOverlay(long timestamp)
        {
            ReadSession();
            var seconds = TimeSpan.FromTicks(timestamp).TotalSeconds;
            UpdateClassRecords(seconds);
            OnCanvas(() =>
            {
                var standings = GetStandings();
                var focusId = CamDriver != null && CamDriver.DriverDetails != null
                    ? CamDriver.DriverDetails.CarIdx : -1;
                DrawLeaderboard(standings, focusId);
                DrawGaps(standings, seconds);
                DrawRadio(seconds);
                DrawPitTransitions(seconds);
                DrawFastestLap(seconds);
            });
        }

        private Standing[] GetStandings()
        {
            if (LeaderBoard == null)
                return new Standing[0];

            var positionsByClass = new Dictionary<long, int>();
            return LeaderBoard.Drivers.Where(d => d.Position.GetValueOrDefault() > 0 &&
                    (d.DriverDetails == null || drivers.ContainsKey(d.DriverDetails.CarIdx)))
                .OrderBy(d => d.Position).Select(d =>
                {
                    var classId = d.DriverDetails == null ? 0 : d.DriverDetails.CarClassID;
                    int position;
                    positionsByClass.TryGetValue(classId, out position);
                    positionsByClass[classId] = ++position;
                    return new Standing { Driver = d, ClassPosition = position };
                }).ToArray();
        }

        private void DrawLeaderboard(Standing[] standings, long focusId)
        {
            if (standings.Length == 0)
                return;

            var racePosition = FormatRacePosition(LeaderBoard.RacePosition);
            var lapCounter = LeaderBoard.LapCounter;
            var showLapRow = !string.IsNullOrWhiteSpace(racePosition) && !string.IsNullOrWhiteSpace(lapCounter)
                && !string.Equals(racePosition, lapCounter, StringComparison.Ordinal);

            Block(Black, new RectangleF(95, 67, 205, 25));
            Text(string.IsNullOrWhiteSpace(racePosition) ? lapCounter : racePosition, Bold, Color.White,
                new RectangleF(103, 67, 191, 25));
            if (showLapRow)
            {
                Block(Grey, new RectangleF(95, 93, 205, 24));
                Text(lapCounter, Bold, Color.White, new RectangleF(103, 93, 191, 24));
            }

            var firstDriverRow = showLapRow ? 118 : 93;
            const int maxRows = 20;
            var visible = standings.Take(maxRows).ToArray();
            var focused = standings.FirstOrDefault(s => s.Driver.DriverDetails != null && s.Driver.DriverDetails.CarIdx == focusId);
            if (focused != null && standings.Length > maxRows && !visible.Contains(focused))
                visible[maxRows - 1] = focused;

            for (var i = 0; i < visible.Length; i++)
            {
                var standing = visible[i];
                var driver = standing.Driver;
                var position = multiclass ? standing.ClassPosition : driver.Position.Value;
                var row = new RectangleF(95, firstDriverRow + i * 25, 205, 24);
                var background = standing == focused ? Color.FromArgb(242, 64, 64, 64) : i % 2 == 0 ? Dark : Grey;
                DriverRow(row, position, Accent(driver.DriverDetails, driver.Position.Value), background, 42);

                var label = multiclass ? ClassLabel(driver.DriverDetails) : "";
                var hasStops = driver.PitStopCount > 0;
                var fastest = IsFastestLapHolder(driver);
                var textRight = row.Right - (fastest ? row.Height : 0) - 6;
                var classWidth = label.Length <= 4 ? 31 : 49;
                if (label.Length > 0)
                    Text(label, Small, Muted, new RectangleF(143, row.Y, classWidth, row.Height));
                var nameX = label.Length > 0 ? 146 + classWidth : 143;
                Text(ShortName(driver.UserName), standing == focused ? Bold : Regular, Color.White,
                    new RectangleF(nameX, row.Y, textRight - (hasStops ? 22 : 0) - nameX, row.Height));
                if (hasStops)
                    Text(driver.PitStopCount.ToString(CultureInfo.InvariantCulture) + "P", Small, Muted,
                        new RectangleF(textRight - 22, row.Y, 22, row.Height), RightText);
                if (fastest)
                    DrawArtwork(FastestLapStopwatch, new Rectangle((int)(row.Right - row.Height), (int)row.Top,
                        (int)row.Height, (int)row.Height));
            }
        }

        private static string FormatRacePosition(string position)
        {
            TimeSpan remaining;
            if (TimeSpan.TryParseExact(position, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out remaining)
                && remaining.Hours == 0)
                return remaining.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

            return position;
        }

        private void DrawGaps(Standing[] standings, double seconds)
        {
            TimingSnapshot timing;
            if (Broadcast != null && Broadcast.Timing.Count > 0)
            {
                var index = LatestAt(Broadcast.Timing, seconds, entry => entry.Time);
                timing = index >= 0 ? Broadcast.Timing[index] : null;
            }
            else
                timing = TimingFromStandings(standings);

            if (timing == null || timing.Focus == null)
                return;

            const int cell = 245;
            const int interval = 104;
            var hasAhead = timing.Ahead != null && timing.Focus.Position > 1;
            var hasBehind = timing.Behind != null;
            var width = cell + (hasAhead ? cell + interval : 0) + (hasBehind ? cell + interval : 0);
            var x = (1280 - width) / 2f;
            if (hasAhead)
            {
                DrawGapDriver(timing.Ahead, x, false);
                DrawInterval(timing.GapAhead, x + cell, interval);
                x += cell + interval;
            }
            DrawGapDriver(timing.Focus, x, true);
            if (hasBehind)
            {
                DrawInterval(timing.GapBehind, x + cell, interval);
                DrawGapDriver(timing.Behind, x + cell + interval, false);
            }
        }

        private TimingSnapshot TimingFromStandings(Standing[] standings)
        {
            if (CamDriver == null || CamDriver.DriverDetails == null)
                return null;

            var focus = Array.FindIndex(standings, s => s.Driver.DriverDetails != null &&
                s.Driver.DriverDetails.CarIdx == CamDriver.DriverDetails.CarIdx);
            if (focus < 0)
                return null;

            return new TimingSnapshot
            {
                Ahead = focus > 0 ? TimingFromStanding(standings[focus - 1]) : null,
                Focus = TimingFromStanding(standings[focus]),
                Behind = focus + 1 < standings.Length ? TimingFromStanding(standings[focus + 1]) : null
            };
        }

        private static TimingDriver TimingFromStanding(Standing standing)
        {
            var driver = standing.Driver;
            return driver.DriverDetails == null ? null : new TimingDriver
            {
                CarIdx = (int)driver.DriverDetails.CarIdx, Position = driver.Position.Value,
                ClassPosition = standing.ClassPosition
            };
        }

        private void DrawGapDriver(TimingDriver timing, float x, bool focused)
        {
            var driver = DriverAt(timing.CarIdx);
            var row = new RectangleF(x, GapBarTop, 245, GapBarHeight);
            DriverRow(row, multiclass ? timing.ClassPosition : timing.Position,
                Accent(driver, timing.Position), focused ? Dark : Black);
            if (driver == null)
                return;

            if (multiclass)
                Text(ClassLabel(driver), Small, Muted, new RectangleF(x + 53, row.Y, 48, row.Height));
            var nameX = x + (multiclass ? 104 : 53);
            var nameRight = timing.SpeedKph >= 0 ? x + 210 : x + 239;
            Text(driver.UserName, Bold, Color.White, new RectangleF(nameX, row.Y, nameRight - nameX, row.Height));
            if (timing.SpeedKph >= 0)
            {
                Text(timing.SpeedKph.ToString(CultureInfo.InvariantCulture), Small, Color.White,
                    new RectangleF(x + 211, row.Top, 32, 15), CentreText);
                Text("km/h", Small, Muted, new RectangleF(x + 211, row.Top + 13, 32, 13), CentreText);
            }
        }

        private void DrawInterval(string gap, float x, int width)
        {
            var area = new RectangleF(x, GapBarTop, width, GapBarHeight);
            Block(Grey, area);
            Text(gap ?? "–", Bold, Color.White, area, CentreText);
        }

        private void DrawRadio(double seconds)
        {
            if (Broadcast == null)
                return;
            var index = LatestAt(Broadcast.Radio, seconds, entry => entry.Time);
            if (index < 0)
                return;
            var carIdx = Broadcast.Radio[index].CarIdx;
            if (carIdx < 0)
                return;
            var driver = DriverAt(carIdx) ?? EventData.CompetingDrivers.FirstOrDefault(d => d.CarIdx == carIdx && !d.IsPaceCar);
            if (driver == null)
                return;

            Block(Dark, new RectangleF(1014, 346, 226, 27));
            Block(Grey, new RectangleF(1020, 347, 53, 25));
            for (var i = 0; i < 7; i++)
            {
                var height = 5 + 12 * (float)Math.Abs(Math.Sin(seconds * (7 + i) + i));
                Block(Accents[0], new RectangleF(1025 + i * 6, 359.5f - height / 2, 3, height));
            }
            Text(driver.UserName, Regular, Color.White, new RectangleF(1076, 346, 158, 27));
        }

        private void DrawPitTransitions(double seconds)
        {
            if (Broadcast == null)
                return;
            var index = LatestAt(Broadcast.Pits, seconds, entry => entry.Time);
            var shownEntry = false;
            var shownExit = false;
            for (; index >= 0; index--)
            {
                var pit = Broadcast.Pits[index];
                if (seconds - pit.Time >= 3 || (shownEntry && shownExit))
                    break;
                if (pit.Entering ? shownEntry : shownExit)
                    continue;
                var driver = DriverAt(pit.CarIdx);
                if (driver == null)
                    continue;

                var top = pit.Entering ? 413 : 480;
                Block(Black, new RectangleF(1020, top, 253, 50));
                Text(pit.Entering ? "Entering Pits" : "Exiting Pits", Bold, pit.Entering ? Accents[1] : Accents[0],
                    new RectangleF(1026, top, 143, 24));
                var value = pit.Entering ? "Stops " + pit.Stops : pit.StopSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                Text(value, Regular, Color.White, new RectangleF(1170, top, 96, 24), RightText);
                Text("#" + driver.CarNumber + " " + driver.UserName, Regular, Color.White, new RectangleF(1026, top + 24, 240, 24));
                if (pit.Entering)
                    shownEntry = true;
                else
                    shownExit = true;
            }
        }

        private void ResetClassRecords()
        {
            classRecords.Clear();
            nextLapUpdate = 0;
            classRecordsThrough = 0;
        }

        private bool IsFastestLapHolder(Driver driver)
        {
            var details = driver.DriverDetails;
            if (details == null)
                return false;

            if (Broadcast == null || Broadcast.BestLaps.Count == 0)
                return FastestLapHolder != null && FastestLapHolder.DriverDetails != null
                    && FastestLapHolder.DriverDetails.CarIdx == details.CarIdx;

            LapTimeUpdate record;
            return classRecords.TryGetValue(details.CarClassID, out record) && record.CarIdx == details.CarIdx;
        }

        private void UpdateClassRecords(double seconds)
        {
            if (Broadcast == null)
                return;
            if (seconds < classRecordsThrough)
                ResetClassRecords();
            classRecordsThrough = seconds;
            while (nextLapUpdate < Broadcast.BestLaps.Count && Broadcast.BestLaps[nextLapUpdate].Time <= seconds)
            {
                var lap = Broadcast.BestLaps[nextLapUpdate++];
                if (!lap.ClassBest)
                    continue;

                var driver = DriverAt(lap.CarIdx);
                if (driver != null)
                    classRecords[driver.CarClassID] = lap;
            }
        }

        private void DrawFastestLap(double seconds)
        {
            if (Broadcast == null || Broadcast.BestLaps.Count == 0)
            {
                if (FastLap != null && FastLap.Driver != null)
                    DrawFastestLapCard("", FastLap.Driver.UserName, FastLap.Time);
                return;
            }

            var active = classRecords.Where(entry => seconds - entry.Value.Time < 15).OrderBy(entry => entry.Key).ToArray();
            if (active.Length == 0)
                return;

            var record = active[(int)(Math.Max(0, seconds) / 5) % active.Length].Value;
            var leader = DriverAt(record.CarIdx);
            DrawFastestLapCard(multiclass ? ClassLabel(leader) : "", leader.UserName, record.LapSeconds);
        }

        private void DrawFastestLapCard(string classLabel, string driverName, double seconds)
        {
            const int right = 1240, top = 67, badgeHeight = 48, detailsHeight = 24;
            var width = (int)Math.Round((double)badgeHeight * FastestLapBadge.Width / FastestLapBadge.Height);
            var left = right - width;
            DrawArtwork(FastestLapBadge, new Rectangle(left, top, width, badgeHeight));

            var detailsTop = top + badgeHeight;
            Block(Black, new RectangleF(left, detailsTop, width, detailsHeight));
            var nameLeft = left + 6;
            if (!string.IsNullOrEmpty(classLabel))
            {
                Text(classLabel, Small, Muted, new RectangleF(nameLeft, detailsTop, 50, detailsHeight));
                nameLeft += 54;
            }
            Text(ShortName(driverName), Regular, Color.White,
                new RectangleF(nameLeft, detailsTop, right - 86 - nameLeft, detailsHeight));
            Text(SecondsText(seconds), Bold, Color.White, new RectangleF(right - 86, detailsTop, 80, detailsHeight), RightText);
        }
    }
}
