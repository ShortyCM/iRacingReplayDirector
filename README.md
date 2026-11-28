iRacingReplayDirector
=====================

This program converts your iRacing replays into video files complete with leaderboards, fastest laps, and more. You can upload these to YouTube and/or use them however you choose.

Replay Director scans the replay's incident markers, captures a track introduction when the replay includes a usable qualifying segment, and records the race. It then uses FFmpeg to encode the video with race overlays.

Here is an example of a full length video created by Replay Director:
https://youtu.be/L8Kd0OqsEVg

This is a highlight video:
https://youtu.be/a1WaRx0R9KI 

Requirements
============
* Video capture software such as OBS or NVIDIA App.
* Use the same start/stop recording hotkey in your capture software and in **Configure General Settings**. The default is Alt+F9.
* Set your video capture software to only record the iRacing replay window.
* Put `ffmpeg.exe` and `ffprobe.exe` on `PATH` or beside Replay Director.
* Save recordings as MP4, MKV, AVI, MOV, WebM, or MPEG-TS.
* Replays should capture all competitors. Before the session, under the graphics options in iRacing, set the Max Cars box to a number greater than number of competitors.

Output settings
===============

In the **Video Encoding** tab, open **Output settings** to choose the container
and select video and audio encoders from the installed FFmpeg build. Set a bitrate
or choose CRF, CQ, QP, or VBR quality when the encoder supports it. You can also
set encoder-specific options such as the x264 or x265 preset, adjust pixel format,
colour matrix and range, and choose the audio sample rate and channel count. The
default is Matroska (.mkv) with x265 at CRF 17 and AAC at 224 kb/s.

Choose a container that supports both encoders. Hardware encoders also need
compatible hardware and drivers.

F1-style overlay
================

Select **F1-style** under **Configure General Settings → Overlay**. The overlay
uses a left leaderboard with up to 20 drivers and a two- or three-driver gap
bar at the bottom based on the camera car. If the camera car is outside the
top 20, it takes the last leaderboard row. The Fastest Lap card shows a
class's record holder and time. The overlay also shows pit entry and exit, and
TeamRadio when the replay supplies a transmitting driver. Qualifying and final
results have their own cards.

Qualifying shows 10 drivers per page for 10 seconds. A final page with up to
five drivers gets 5 seconds when it follows another page.
Final race results also show 10 drivers per page for 10 seconds, with 5 seconds
for a last page of up to five drivers.

Use **F1-style class labels** to supply display names for class IDs. Enter
comma-separated assignments such as `2523=LMP2,4011=GT3`. New captures
save the timing and event history used by these panels. Captures made before
F1-style need a fresh recording to add gaps and speeds.

Building
========

Build `iRacingReplayDirectorApps.sln` for x64 in Visual Studio 2026 with the .NET Framework
4.7.2 targeting pack. The solution builds the SDK from a sibling checkout of the
[iRacingSDK.Net fork](https://github.com/ShortyCM/iRacingSDK.Net) at
`../iRacingSDK.Net/iRacingSDK.csproj`.

First-time setup
================

1. Start your capture software and open an iRacing replay.
2. On the **Race Capture** tab, open **Verify Video Capture**. Select the folder where your capture software writes recordings, then click **Verify Video Capture** in the dialog to record a sample.
3. On the **Video Encoding** tab, open **Verify Video Conversion**. Select the sample recording and click **Verify Conversion**. The test writes a preview of up to 10 seconds beside the sample using the selected output settings.

Creating a video
================

1. Open the replay in iRacing, hide iRacing's replay overlay with the space bar, and start your capture software.
2. In Replay Director, use **Configure Track Cameras** to set the camera percentages for a track you haven't configured yet.
3. Click **Begin Capture**. Replay Director locates the race start, scans incident markers, and records the race at normal playback speed. For a shorter test run, select **Short Test Only** before starting.
4. **Encode Video After Capture** is checked by default. To encode manually, select the capture's `.replayscript` file on the **Video Encoding** tab and click **Transcode Video**.
5. Encoding produces full-race and highlight videos by default. Select **Highlight Video Only** to export just the highlights. Set the highlight target length in **Configure General Settings**.

Troubleshooting
===============

If encoding reports that no highlight cuts could be calculated, reduce **Duration of Highlight video** in **Configure General Settings** and try again.

History
==============
iRacingReplayDirector is the successor to iRacingReplayOverlay.NET, developed by Vipoo. Merlin Cooper continued development in the `MerlinCooper/iRacingReplayDirector` fork, which this fork builds on.
