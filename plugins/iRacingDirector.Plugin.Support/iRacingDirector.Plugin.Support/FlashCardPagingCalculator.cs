using System;

namespace iRacingDirector.Plugin
{
    public class FlashCardPagingCalculator
    {
        public const int QualifyingDriversPerPage = 10;
        private const int QualifyingSecondsPerPage = 10;

        public static int GetQualifyingPageCount(int resultCount)
        {
            return resultCount > 0 ? 1 + (resultCount - 1) / QualifyingDriversPerPage : 0;
        }

        public static TimeSpan GetQualifyingDuration(int resultCount)
        {
            var pages = GetQualifyingPageCount(resultCount);
            var lastPageSize = resultCount - (pages - 1) * QualifyingDriversPerPage;
            var lastPageReduction = pages > 1 && lastPageSize <= 5 ? 5 : 0;
            return TimeSpan.FromSeconds(pages * QualifyingSecondsPerPage - lastPageReduction);
        }

        public static int GetQualifyingPageNumber(int resultCount, long timestamp)
        {
            var page = Math.Max(0, timestamp) / (QualifyingSecondsPerPage * TimeSpan.TicksPerSecond);
            return (int)Math.Min(page, GetQualifyingPageCount(resultCount) - 1);
        }

        static int GetNumberOfPages(EventData eventData, int driversPerPage)
        {
            var numberOfDrivers = eventData.CompetingDrivers.Length - 1;
            var numberOfPages = Math.Min(numberOfDrivers / driversPerPage, 3);
            if (((float)numberOfDrivers % driversPerPage) != 0)
                numberOfPages++;

            return numberOfPages;
        }

        public static int GetPageNumber(EventData eventData, int driversPerPage, long duration, long timestamp)
        {
            var pagePeriod = (float)timestamp / duration;

            var numberOfPages = GetNumberOfPages(eventData, driversPerPage);
            var page = (int)Math.Floor(pagePeriod * numberOfPages);
            return Math.Min(page, numberOfPages - 1);
        }
    }
}
