using iRacingDirector.Plugin;
using iRacingSDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace iRacingReplayDirector.Phases.Capturing
{
    internal sealed class RecordBroadcastData
    {
        private const int CheckpointsPerLap = 200;
        private const double TimingPeriod = 0.5;

        private sealed class CarTiming
        {
            public double Distance = -1;
            public double Time;
            public double SnapshotDistance = -1;
            public int SpeedKph = -1;
            public int LapStart = -1;
            public double LapStartTime;
            public double BestLap = double.PositiveInfinity;
            public readonly int[] CheckpointNumbers = new int[CheckpointsPerLap];
            public readonly double[] CheckpointTimes = new double[CheckpointsPerLap];
            public bool? OnPitRoad;
            public double? StallEntry;
            public double StopSeconds;
        }

        private readonly BroadcastData broadcast;
        private readonly CarTiming[] cars = Enumerable.Range(0, 64).Select(_ => new CarTiming()).ToArray();
        private readonly Dictionary<long, double> bestLapByClass = new Dictionary<long, double>();
        private int radioCar = -1;
        private double nextSnapshot;
        private double lastSnapshotTime;
        private double trackKm;

        public RecordBroadcastData(BroadcastData broadcast)
        {
            this.broadcast = broadcast;
        }

        public void Process(DataSample data, TimeSpan relativeTime)
        {
            var telemetry = data.Telemetry;
            var time = relativeTime.TotalSeconds;
            var sessionTime = ReplayTime(data);
            var distances = telemetry.CarIdxDistance;
            var takeSnapshot = time >= nextSnapshot;
            if (trackKm <= 0 && data.SessionData.WeekendInfo != null)
            {
                var length = data.SessionData.WeekendInfo.TrackLength ?? "";
                double.TryParse(length.Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out trackKm);
            }

            RecordRadio(telemetry, time);
            foreach (var car in telemetry.RaceCars)
            {
                var state = cars[car.CarIdx];
                var distance = distances[car.CarIdx];
                if (!car.HasData || !(distance > 0))
                {
                    state.Distance = state.SnapshotDistance = -1;
                    state.LapStart = -1;
                    state.OnPitRoad = null;
                    state.StallEntry = null;
                    continue;
                }

                RecordMotion(car, state, distance, sessionTime, time, telemetry.SessionState);
                RecordPitTransition(car, state, sessionTime, time);

                if (takeSnapshot)
                {
                    var elapsed = sessionTime - lastSnapshotTime;
                    var speed = (distance - state.SnapshotDistance) * trackKm * 3600 / elapsed;
                    state.SpeedKph = trackKm > 0 && state.SnapshotDistance >= 0 && speed >= 0 && speed <= 500
                        ? (int)Math.Round(speed) : -1;
                    state.SnapshotDistance = distance;
                }
            }

            if (takeSnapshot)
            {
                RecordTiming(telemetry, time);
                nextSnapshot = time + TimingPeriod;
                lastSnapshotTime = sessionTime;
            }
        }

        private static double ReplayTime(DataSample data)
        {
            var telemetry = data.Telemetry;
            var weekend = data.SessionData.WeekendInfo;
            object value;
            if (weekend != null && string.Equals(weekend.SimMode, "replay", StringComparison.OrdinalIgnoreCase)
                && telemetry.TryGetValue("ReplaySessionTime", out value) && value is double)
            {
                var replayTime = (double)value;
                if (replayTime > 0 && !double.IsInfinity(replayTime))
                    return replayTime;
            }

            return telemetry.SessionTime;
        }

        private void RecordRadio(Telemetry telemetry, double time)
        {
            object value;
            var speaker = telemetry.TryGetValue("RadioTransmitCarIdx", out value) && value is int ? (int)value : -1;
            if (speaker == radioCar)
                return;

            broadcast.Radio.Add(new RadioTransmission { Time = time, CarIdx = speaker });
            radioCar = speaker;
        }

        private void RecordMotion(Car car, CarTiming state, double distance, double sessionTime, double time, SessionState raceState)
        {
            var progress = distance - state.Distance;
            if (state.Distance >= 0 && progress > 0 && progress < 0.1 && sessionTime > state.Time)
            {
                var lastCheckpoint = (int)(state.Distance * CheckpointsPerLap);
                var checkpoint = (int)(distance * CheckpointsPerLap);
                for (var number = lastCheckpoint + 1; number <= checkpoint; number++)
                {
                    var fraction = (number / (double)CheckpointsPerLap - state.Distance) / progress;
                    var at = state.Time + fraction * (sessionTime - state.Time);
                    var index = number % CheckpointsPerLap;
                    state.CheckpointNumbers[index] = number;
                    state.CheckpointTimes[index] = at;
                    if (index == 0 && (raceState == SessionState.Racing || raceState == SessionState.Checkered))
                        RecordLapTime(car, state, number / CheckpointsPerLap, at, time);
                }
            }
            else if (progress < -0.05 || progress >= 0.1 || sessionTime < state.Time)
            {
                state.LapStart = -1;
                Array.Clear(state.CheckpointNumbers, 0, state.CheckpointNumbers.Length);
            }

            state.Distance = distance;
            state.Time = sessionTime;
        }

        private void RecordLapTime(Car car, CarTiming state, int lap, double at, double time)
        {
            var lapSeconds = at - state.LapStartTime;
            if (state.LapStart >= 1 && state.LapStart == lap - 1 && lapSeconds > 0 && lapSeconds < state.BestLap)
            {
                var classId = car.Details.Driver.CarClassID;
                double classBest;
                var isClassBest = !bestLapByClass.TryGetValue(classId, out classBest) || lapSeconds < classBest;
                if (isClassBest)
                    bestLapByClass[classId] = lapSeconds;

                state.BestLap = lapSeconds;
                broadcast.BestLaps.Add(new LapTimeUpdate
                {
                    Time = time, CarIdx = car.CarIdx, LapSeconds = lapSeconds, ClassBest = isClassBest
                });
            }

            state.LapStart = lap;
            state.LapStartTime = at;
        }

        private void RecordPitTransition(Car car, CarTiming state, double sessionTime, double time)
        {
            var inPits = car.Details.IsOnPitRoad || car.IsInPits;
            if (inPits && !state.OnPitRoad.GetValueOrDefault())
                state.StopSeconds = 0;

            if (car.IsInPits)
                state.StallEntry = state.StallEntry ?? sessionTime;
            else if (state.StallEntry.HasValue)
            {
                state.StopSeconds += Math.Max(0, sessionTime - state.StallEntry.Value);
                state.StallEntry = null;
            }

            if (state.OnPitRoad.HasValue && state.OnPitRoad.Value != inPits)
                broadcast.Pits.Add(new PitTransition
                {
                    Time = time, CarIdx = car.CarIdx, Entering = inPits,
                    Stops = car.PitStopCount, StopSeconds = state.StopSeconds
                });

            state.OnPitRoad = inPits;
        }

        private void RecordTiming(Telemetry telemetry, double time)
        {
            var runningOrder = telemetry.RaceCars
                .Where(car => cars[car.CarIdx].Distance > 0)
                .OrderBy(car => car.Position)
                .ToArray();
            var focusIndex = Array.FindIndex(runningOrder, car => car.CarIdx == telemetry.CamCarIdx);
            var snapshot = new TimingSnapshot { Time = time };

            if (focusIndex >= 0)
            {
                var classPositions = new Dictionary<long, int>();
                for (var index = 0; index <= Math.Min(focusIndex + 1, runningOrder.Length - 1); index++)
                {
                    var car = runningOrder[index];
                    var classId = car.Details.Driver.CarClassID;
                    int classPosition;
                    classPositions.TryGetValue(classId, out classPosition);
                    classPositions[classId] = ++classPosition;
                    if (index < focusIndex - 1)
                        continue;

                    var driver = new TimingDriver
                    {
                        CarIdx = car.CarIdx, Position = car.Position, ClassPosition = classPosition,
                        SpeedKph = cars[car.CarIdx].SpeedKph
                    };
                    if (index < focusIndex)
                        snapshot.Ahead = driver;
                    else if (index == focusIndex)
                        snapshot.Focus = driver;
                    else
                        snapshot.Behind = driver;
                }

                if (snapshot.Ahead != null)
                    snapshot.GapAhead = Gap(snapshot.Ahead.CarIdx, snapshot.Focus.CarIdx);
                if (snapshot.Behind != null)
                    snapshot.GapBehind = Gap(snapshot.Focus.CarIdx, snapshot.Behind.CarIdx);
            }

            broadcast.Timing.Add(snapshot);
        }

        private string Gap(int aheadCarIdx, int behindCarIdx)
        {
            var ahead = cars[aheadCarIdx];
            var behind = cars[behindCarIdx];
            var lapGap = (int)Math.Floor(ahead.Distance - behind.Distance);
            if (lapGap > 0)
                return "+" + lapGap.ToString(CultureInfo.InvariantCulture) + "L";

            // Compare when the cars crossed the same point. The checkpoint number also identifies the lap.
            var number = (int)(behind.Distance * CheckpointsPerLap);
            var index = number % CheckpointsPerLap;
            if (number == 0 || ahead.CheckpointNumbers[index] != number || behind.CheckpointNumbers[index] != number)
                return null;

            var seconds = behind.CheckpointTimes[index] - ahead.CheckpointTimes[index];
            return seconds >= 0 ? "+" + seconds.ToString("0.000", CultureInfo.InvariantCulture) : null;
        }
    }
}
