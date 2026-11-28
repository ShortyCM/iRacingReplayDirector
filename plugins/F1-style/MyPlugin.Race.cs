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
        private sealed class Standing
        {
            public Driver Driver;
            public int ClassPosition;
        }

        private readonly Dictionary<int, LapTimeUpdate> lapTimes = new Dictionary<int, LapTimeUpdate>();
        private readonly Dictionary<long, LapTimeUpdate> classRecords = new Dictionary<long, LapTimeUpdate>();
        private int nextLapTime;
        private double lapTimesThrough;

        public void RaceOverlay(long timestamp)
        {
            ReadSession();
            var seconds = TimeSpan.FromTicks(timestamp).TotalSeconds;
            OnCanvas(() =>
            {
                var standings = GetStandings();
                var focusId = CamDriver != null && CamDriver.DriverDetails != null
                    ? CamDriver.DriverDetails.CarIdx : -1;
                DrawLeaderboard(standings, focusId);
                DrawPositionBar(standings, seconds, focusId);
                DrawGaps(standings, seconds);
                DrawRadio(seconds);
                DrawPitTransitions(seconds);
                DrawFastestLaps(seconds);
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

            Block(Black, new RectangleF(95, 67, 205, 25));
            Text(LeaderBoard.RacePosition ?? LeaderBoard.LapCounter, Bold, Color.White,
                new RectangleF(103, 67, 191, 25));

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
                var row = new RectangleF(95, 93 + i * 25, 205, 24);
                var background = standing == focused ? Color.FromArgb(242, 64, 64, 64) : i % 2 == 0 ? Dark : Grey;
                DriverRow(row, position, Accent(driver.DriverDetails, driver.Position.Value), background, 42);

                var label = multiclass ? ClassLabel(driver.DriverDetails) : "";
                var hasStops = driver.PitStopCount > 0;
                if (label.Length > 0)
                    Text(label, Small, Muted, new RectangleF(143, row.Y, 49, row.Height));
                var nameX = label.Length > 0 ? 195 : 143;
                Text(ShortName(driver.UserName), standing == focused ? Bold : Regular, Color.White,
                    new RectangleF(nameX, row.Y, (hasStops ? 272 : 294) - nameX, row.Height));
                if (hasStops)
                    Text(driver.PitStopCount.ToString(CultureInfo.InvariantCulture) + "P", Small, Muted,
                        new RectangleF(273, row.Y, 22, row.Height), RightText);
            }
        }

        private void DrawPositionBar(Standing[] standings, double seconds, long focusId)
        {
            if (standings.Length == 0)
                return;

            const int driversPerPage = 5;
            var pages = (standings.Length + driversPerPage - 1) / driversPerPage;
            var page = (int)(Math.Max(0, seconds) / 5) % pages;
            Block(Black, new RectangleF(0, 658, 1280, 27));
            var items = standings.Skip(page * driversPerPage).Take(driversPerPage).ToArray();
            for (var i = 0; i < items.Length; i++)
            {
                var standing = items[i];
                var driver = standing.Driver;
                var area = new RectangleF(95 + i * 218, 658, 217, 27);
                var focused = driver.DriverDetails != null && driver.DriverDetails.CarIdx == focusId;
                DriverRow(area, multiclass ? standing.ClassPosition : driver.Position.Value,
                    Accent(driver.DriverDetails, driver.Position.Value), focused ? Grey : Black);
                var label = multiclass ? ClassLabel(driver.DriverDetails) : "";
                if (label.Length > 0)
                    Text(label, Small, Muted, new RectangleF(area.X + 52, area.Y, 50, area.Height));
                var left = label.Length > 0 ? 106 : 54;
                Text(ShortName(driver.UserName), Bold, Color.White,
                    new RectangleF(area.X + left, area.Y, area.Width - left - 5, area.Height), CentreText);
            }
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
            var row = new RectangleF(x, 614, 245, 27);
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
                    new RectangleF(x + 211, 614, 32, 15), CentreText);
                Text("km/h", Small, Muted, new RectangleF(x + 211, 627, 32, 13), CentreText);
            }
        }

        private void DrawInterval(string gap, float x, int width)
        {
            var area = new RectangleF(x, 614, width, 27);
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

        private void ResetLapTimes()
        {
            lapTimes.Clear();
            classRecords.Clear();
            nextLapTime = 0;
            lapTimesThrough = 0;
        }

        private void DrawFastestLaps(double seconds)
        {
            if (Broadcast == null || Broadcast.BestLaps.Count == 0)
            {
                if (FastLap != null && FastLap.Driver != null)
                {
                    Block(Black, new RectangleF(1007, 67, 233, 48));
                    Text("Fastest Lap", Bold, Purple, new RectangleF(1013, 67, 221, 23));
                    Text(ShortName(FastLap.Driver.UserName), Regular, Color.White, new RectangleF(1013, 91, 141, 23));
                    Text(SecondsText(FastLap.Time), Bold, Color.White, new RectangleF(1154, 91, 80, 23), RightText);
                }
                return;
            }

            if (seconds < lapTimesThrough)
                ResetLapTimes();
            lapTimesThrough = seconds;
            while (nextLapTime < Broadcast.BestLaps.Count && Broadcast.BestLaps[nextLapTime].Time <= seconds)
            {
                var lap = Broadcast.BestLaps[nextLapTime++];
                lapTimes[lap.CarIdx] = lap;
                var driver = DriverAt(lap.CarIdx);
                if (lap.ClassBest && driver != null)
                    classRecords[driver.CarClassID] = lap;
            }

            var active = classRecords.Where(record => seconds - record.Value.Time < 15).OrderBy(record => record.Key).ToArray();
            if (active.Length == 0)
                return;

            var recordClass = active[(int)(Math.Max(0, seconds) / 5) % active.Length];
            var leader = DriverAt(recordClass.Value.CarIdx);
            var laps = lapTimes.Values.Where(lap =>
            {
                var driver = DriverAt(lap.CarIdx);
                return driver != null && driver.CarClassID == recordClass.Key;
            }).OrderBy(lap => lap.LapSeconds).Take(10).ToArray();
            Block(Black, new RectangleF(1007, 67, 233, 25));
            var title = multiclass ? "Fastest Laps  ·  " + ClassLabel(leader) : "Fastest Laps";
            Text(title, Bold, Purple, new RectangleF(1013, 67, 221, 25));
            for (var i = 0; i < laps.Length; i++)
            {
                var lap = laps[i];
                var driver = DriverAt(lap.CarIdx);
                var row = new RectangleF(1007, 93 + i * 25, 233, 24);
                DriverRow(row, i + 1, Accent(driver, i + 1), i % 2 == 0 ? Dark : Grey, 42);
                Text(ShortName(driver.UserName), Regular, Color.White, new RectangleF(1055, row.Y, 106, row.Height));
                Text(SecondsText(lap.LapSeconds), Bold, Color.White, new RectangleF(1161, row.Y, 72, row.Height), RightText);
            }
        }
    }
}
