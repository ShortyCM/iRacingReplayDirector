using iRacingSDK;
using System;
using System.Collections.Generic;
using System.Linq;
using Result = iRacingSDK.SessionData._SessionInfo._Sessions._ResultsPositions;

namespace iRacingDirector.Plugin
{
    public class EventData
    {
        private SessionData sessionData;
        private Result[] qualifyingClassification;
        private Result[] finalClassification;

        public EventData(SessionData sessionData)
        {
            this.sessionData = sessionData;
        }

        public SessionData._WeekendInfo WeekendInfo
        { get { return sessionData.WeekendInfo; } }

        public SessionData._SessionInfo._Sessions._ResultsPositions[] QualifyingResults
        { get { return sessionData.SessionInfo.Sessions.Qualifying().ResultsPositions; } }

        public Result[] QualifyingClassification
        {
            get { return qualifyingClassification ?? (qualifyingClassification = ReadClassification("qualif")); }
        }

        public Result[] FinalClassification
        {
            get { return finalClassification ?? (finalClassification = ReadClassification("race")); }
        }

        private Result[] ReadClassification(string sessionType)
        {
            var session = sessionData.SessionInfo.Sessions.FirstOrDefault(entry => entry.SessionType != null
                && entry.SessionType.IndexOf(sessionType, StringComparison.OrdinalIgnoreCase) >= 0);
            if (session == null || session.ResultsPositions == null)
                return new Result[0];

            var drivers = new HashSet<long>(CompetingDrivers.Where(driver => !driver.IsPaceCar && driver.IsSpectator == 0)
                .Select(driver => driver.CarIdx));
            return session.ResultsPositions.Where(result => result.Position > 0 && drivers.Contains(result.CarIdx))
                .OrderBy(result => result.Position).ToArray();
        }

        public SessionData._DriverInfo._Drivers[] CompetingDrivers
        { get { return sessionData.DriverInfo.Drivers; } }

        public SessionData._DriverInfo._Drivers GetCompetingDriverByIndex(long carIdx)
        {
            return carIdx < CompetingDrivers.Length ? CompetingDrivers[carIdx] : new SessionData._DriverInfo._Drivers { UserName = "---", CarNumber = "---" };
        }

        public SessionData._SessionInfo._Sessions Race
        { get { return sessionData.SessionInfo.Sessions.First(s => s.SessionType.ToLower().Contains("race")); } }

        public SessionData._SessionInfo._Sessions._ResultsPositions[] Results
        { get { return Race.ResultsPositions ?? new SessionData._SessionInfo._Sessions._ResultsPositions[0]; } }

        public SessionData Raw { get { return sessionData; } }
    }
}
