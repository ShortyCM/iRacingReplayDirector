using iRacingSDK.Support;
using System.Drawing;

namespace iRacingDirector.Plugin.StandardOverlays
{
    public partial class MyPlugin
    {
        public Graphics Graphics;
        public EventData EventData;

        const int FlashCardWidth = 900;
        const int FlashCardLeft = (1920 / 2) - FlashCardWidth / 2;
        public void IntroFlashCard(long duration, long timestamp)
        {
            var page = FlashCardPagingCalculator.GetQualifyingPageNumber(EventData.QualifyingClassification.Length, timestamp);
            if (page >= 0)
                DrawIntroFlashCard(page);
        }

        public void RaceOverlay(long timestamp)
        {
            var timeInSeconds = timestamp.FromNanoToSeconds();

            DrawLeaderboard(timeInSeconds.Seconds());
            DrawCurrentDriverRow();
            DrawRaceMessages(timeInSeconds);
            DrawFastestLap();
        }

        public void OutroFlashCard(long duration, long timestamp)
        {
            var page = FlashCardPagingCalculator.GetResultsPageNumber(EventData.FinalClassification.Length, timestamp);
            if (page >= 0)
                DrawOutroFlashCard(page);
        }
    }
}
