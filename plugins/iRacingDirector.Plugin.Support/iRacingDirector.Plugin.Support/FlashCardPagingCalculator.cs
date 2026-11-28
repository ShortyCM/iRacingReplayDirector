using System;

namespace iRacingDirector.Plugin
{
    public class FlashCardPagingCalculator
    {
        public const int ResultsDriversPerPage = 10;
        public const int QualifyingDriversPerPage = ResultsDriversPerPage;
        private const int SecondsPerResultsPage = 10;

        public static int GetQualifyingPageCount(int resultCount)
        {
            return GetResultsPageCount(resultCount);
        }

        public static int GetResultsPageCount(int resultCount)
        {
            return resultCount > 0 ? 1 + (resultCount - 1) / ResultsDriversPerPage : 0;
        }

        public static TimeSpan GetQualifyingDuration(int resultCount)
        {
            return GetResultsDuration(resultCount, shortenSinglePage: false);
        }

        public static TimeSpan GetFinalResultsDuration(int resultCount)
        {
            return GetResultsDuration(resultCount, shortenSinglePage: true);
        }

        private static TimeSpan GetResultsDuration(int resultCount, bool shortenSinglePage)
        {
            var pages = GetResultsPageCount(resultCount);
            if (pages == 0)
                return TimeSpan.Zero;

            var lastPageSize = resultCount - (pages - 1) * ResultsDriversPerPage;
            var lastPageReduction = (shortenSinglePage || pages > 1) && lastPageSize <= 5 ? 5 : 0;
            return TimeSpan.FromSeconds(pages * (double)SecondsPerResultsPage - lastPageReduction);
        }

        public static int GetQualifyingPageNumber(int resultCount, long timestamp)
        {
            return GetResultsPageNumber(resultCount, timestamp);
        }

        public static int GetResultsPageNumber(int resultCount, long timestamp)
        {
            var page = Math.Max(0, timestamp) / (SecondsPerResultsPage * TimeSpan.TicksPerSecond);
            return (int)Math.Min(page, GetResultsPageCount(resultCount) - 1);
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
