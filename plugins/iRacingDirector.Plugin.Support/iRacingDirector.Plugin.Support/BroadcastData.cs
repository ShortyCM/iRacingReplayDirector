using System.Collections.Generic;
using System.Xml.Serialization;

namespace iRacingDirector.Plugin
{
    public sealed class BroadcastData
    {
        public List<TimingSnapshot> Timing = new List<TimingSnapshot>();
        public List<RadioTransmission> Radio = new List<RadioTransmission>();
        public List<PitTransition> Pits = new List<PitTransition>();
        public List<LapTimeUpdate> BestLaps = new List<LapTimeUpdate>();
    }

    public sealed class TimingSnapshot
    {
        [XmlAttribute] public double Time;
        public TimingDriver Ahead;
        public TimingDriver Focus;
        public TimingDriver Behind;
        public string GapAhead;
        public string GapBehind;
    }

    public sealed class TimingDriver
    {
        [XmlAttribute] public int CarIdx;
        [XmlAttribute] public int Position;
        [XmlAttribute] public int ClassPosition;
        [XmlAttribute] public int SpeedKph = -1;
    }

    public sealed class RadioTransmission
    {
        [XmlAttribute] public double Time;
        [XmlAttribute] public int CarIdx;
    }

    public sealed class PitTransition
    {
        [XmlAttribute] public double Time;
        [XmlAttribute] public int CarIdx;
        [XmlAttribute] public bool Entering;
        [XmlAttribute] public int Stops;
        [XmlAttribute] public double StopSeconds;
    }

    public sealed class LapTimeUpdate
    {
        [XmlAttribute] public double Time;
        [XmlAttribute] public int CarIdx;
        [XmlAttribute] public double LapSeconds;
        [XmlAttribute] public bool ClassBest;
    }
}
