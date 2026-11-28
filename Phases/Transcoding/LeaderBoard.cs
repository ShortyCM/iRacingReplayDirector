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
// You should have received a copy of the GNU General Public License
// along with iRacingReplayDirector.  If not, see <http://www.gnu.org/licenses/>.

using iRacingReplayDirector.Phases.Capturing;
using System;
using System.Drawing;

namespace iRacingReplayDirector.Phases.Transcoding
{
    public class LeaderBoard
    {
        public OverlayData OverlayData;
        public const int DriversPerPage = 10;

        PluginProxy plugin;
        internal bool DisplaysResults { get; private set; }

        internal string PluginName
        {
            set
            {
                DisplaysResults = !string.Equals(value, "NoOverlay", StringComparison.OrdinalIgnoreCase);
                this.plugin = new PluginProxy(value);
            }
        }

        public LeaderBoard()
        {
        }

        public void Intro(Graphics graphics, long duration, long timestamp)
        {
            plugin.SetReplayConfig(OverlayData);
            plugin.SetGraphics(graphics);
            plugin.InjectFields(timestamp, Settings.Default.PreferredDriverNames.Split(new char[] { ',', ';' }));
            plugin.DrawIntroFlashCard(duration);
        }

        public void Overlay(Graphics graphics, long timestamp)
        {
            Overlay(graphics, timestamp, timestamp);
        }

        internal void Overlay(Graphics graphics, long timestamp, long elapsed)
        {
            plugin.SetReplayConfig(OverlayData);
            plugin.SetGraphics(graphics);
            plugin.InjectFields(timestamp, Settings.Default.PreferredDriverNames.Split(new char[] { ',', ';' }));
            plugin.RaceOverlay(elapsed);
        }

        public void Outro(Graphics graphics, long duration, long timestamp, long period)
        {
            plugin.SetReplayConfig(OverlayData);
            plugin.SetGraphics(graphics);
            plugin.InjectFields(timestamp, Settings.Default.PreferredDriverNames.Split(new char[] { ',', ';' }));
            plugin.DrawOutroFlashCard(duration, period);
        }
    }
}
