// This file is part of iRacingReplayDirector.
//
// Copyright 2014 Dean Netherton
// https://github.com/vipoo/iRacingReplayDirector.net
//
// iRacingReplayDirector is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// iRacingReplayDirector is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License333
// along with iRacingReplayDirector.  If not, see <http://www.gnu.org/licenses/>.

using iRacingReplayDirector.Support;
using iRacingSDK;
using iRacingSDK.Support;
using System;
using System.Linq;

namespace iRacingReplayDirector.Phases.Capturing.LeaderBoard
{
    public class CaptureLeaderBoardLastLap
    {
        readonly OverlayData overlayData;
        readonly CaptureLeaderBoard captureLeaderBoard;
        readonly CommentaryMessages commentaryMessages;

        bool[] haveNotedCheckerdFlag = new bool[64];

        public CaptureLeaderBoardLastLap(CaptureLeaderBoard captureLeaderBoard, OverlayData overlayData, CommentaryMessages commentaryMessages)
        {
            this.captureLeaderBoard = captureLeaderBoard;
            this.overlayData = overlayData;
            this.commentaryMessages = commentaryMessages;
        }

        public void Process(DataSample data, TimeSpan relativeTime, ref OverlayData.LeaderBoard leaderBoard)
        {
            leaderBoard = captureLeaderBoard.CreateLeaderBoard(data, relativeTime, leaderBoard.Drivers);

            foreach (var car in data.Telemetry.RaceCars)
            {
                if (!car.HasSeenCheckeredFlag || haveNotedCheckerdFlag[car.CarIdx])
                    continue;

                var result = car.ResultPosition;
                if (result == null || result.Position <= 0)
                    continue;

                var position = (int)result.Position;
                AnnounceFinish(data, relativeTime, car, position, ref leaderBoard);
                MarkResultFlashCardStart(data, relativeTime, position);
            }

            overlayData.LeaderBoards.Add(leaderBoard);
        }

        void MarkResultFlashCardStart(DataSample data, TimeSpan relativeTime, int position)
        {
            if (!overlayData.TimeForOutroOverlay.HasValue && position == Settings.Default.ResultsFlashCardPosition)
            {
                overlayData.TimeForOutroOverlay = relativeTime.TotalSeconds;
                TraceInfo.WriteLine("{0} Mark show results flash card.", data.Telemetry.SessionTimeSpan);
            }
        }

        void AnnounceFinish(DataSample data, TimeSpan relativeTime, Car car, int position, ref OverlayData.LeaderBoard leaderBoard)
        {
            haveNotedCheckerdFlag[car.CarIdx] = true;

            var drivers = leaderBoard.Drivers.Where(d => d.CarIdx != car.CarIdx)
                .Select(d => d.Clone())
                .ToList();

            drivers.Insert(Math.Min(position - 1, drivers.Count), new OverlayData.Driver
            {
                CarNumber = car.Details.CarNumberDisplay,
                UserName = car.Details.UserName,
                Position = position,
                CarIdx = car.CarIdx,
                PitStopCount = car.PitStopCount

            });

            var p = 1;
            foreach (var d in drivers)
                d.Position = p++;

            leaderBoard = captureLeaderBoard.CreateLeaderBoard(data, relativeTime, drivers.ToArray());

            var msg = string.Format("{0} finished in {1}{2}", car.Details.UserName, position, position.Ordinal());
            TraceInfo.WriteLine("{0} {1}", data.Telemetry.SessionTimeSpan, msg);
            commentaryMessages.Add(msg, relativeTime.TotalSeconds);
        }
    }
}
