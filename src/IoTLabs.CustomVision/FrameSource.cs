using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

using EdgeModuleSamples.Common;
using static EdgeModuleSamples.Common.AsyncHelper;
using System.Security.Cryptography.X509Certificates;
using Windows.Globalization.DateTimeFormatting;
using Windows.Media;

//
// This sample directly implements the following:
//
// https://docs.microsoft.com/en-us/windows/uwp/audio-video-camera/process-media-frames-with-mediaframereader
//

namespace SampleModule
{
    class FrameSource :  IDisposable
    {
        private const int MinimumVideoWidth = 1080;
        private MediaCapture mediaCapture = null;
        private MediaFrameReader mediaFrameReader = null;
        private EventWaitHandle evtFrame = null;
        public static async Task<IEnumerable<string>> GetSourceNamesAsync()
        {
            var frameSourceGroups = await AsAsync(MediaFrameSourceGroup.FindAllAsync());
            return frameSourceGroups.Select(x => x.DisplayName);
        }

        public async Task StartAsync(string Name, bool UseGpu = false)
        {
            var frameSourceGroups = await AsAsync(MediaFrameSourceGroup.FindAllAsync());

            Log.WriteLine($"Found the following devices: {(frameSourceGroups.Any() ? string.Join(", ", frameSourceGroups.Select(x => x.DisplayName)) : "no imaging devices found")}");

            // Only select colour cameras to filter out IR ones
            var selectedGroup = frameSourceGroups
                .Where(x => x.DisplayName.Contains(Name) && x.SourceInfos.FirstOrDefault().SourceKind == MediaFrameSourceKind.Color)
                .OrderBy(x => x.DisplayName)
                .FirstOrDefault();

            if (selectedGroup == null)
            {
                if ((Name == "*"))
                {
                    //get first camera
                    selectedGroup = frameSourceGroups.FirstOrDefault();
                }
                else if (("0123456789").Contains(Name))
                {
                    //get camera by index
                    try
                    {
                        int vCameraId = int.Parse(Name);
                        if (vCameraId < frameSourceGroups.Count)
                        {
                            selectedGroup = frameSourceGroups[vCameraId];
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
            }

            if (selectedGroup == null)
            {
                throw new ApplicationException($"Unable to find frame source from parameter '{Name}'");
            }
            else
            {
                try
                {
                    Log.WriteLine($"Selected device named '{selectedGroup.DisplayName}', based on '{Name}' filter");
                }
                catch (Exception)
                {
                }
            }



            var colorSourceInfo = selectedGroup.SourceInfos
                .Where(x => x.MediaStreamType == MediaStreamType.VideoRecord && x.SourceKind == MediaFrameSourceKind.Color)
                .FirstOrDefault();

            if (null == colorSourceInfo)
                throw new ApplicationException($"Unable to find color video recording source on '{selectedGroup.DisplayName}' device");

            mediaCapture = new MediaCapture();

            if (null == mediaCapture)
                throw new ApplicationException($"Unable to create new mediacapture");

            var settings = new MediaCaptureInitializationSettings()
            {
                SourceGroup = selectedGroup,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = UseGpu ? MediaCaptureMemoryPreference.Auto : MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            };

            try
            {
                Log.WriteLine($"Before async call to initialise {nameof(mediaCapture)} object...");
                await AsAsync(mediaCapture.InitializeAsync(settings));
                Log.WriteLine($"After async call to initialise {nameof(mediaCapture)} object...");
            }
            catch (Exception ex)
            {
                throw new ApplicationException("MediaCapture initialization failed: " + ex.Message, ex);
            }

            // TODO: Use Console.Write to output this
            Console.WriteLine($"{nameof(colorSourceInfo)} is {(colorSourceInfo == null ? "is null" : "is not null")}");
            Console.WriteLine($"colorSourceInfo.Id ({nameof(colorSourceInfo.Id)}) is {colorSourceInfo.Id}");

            Console.WriteLine($"{nameof(mediaCapture)} is {(mediaCapture == null ? "is null" : "is not null")}");
            Console.WriteLine($"mediaCapture.FrameSources ({nameof(mediaCapture.FrameSources)}) has {mediaCapture.FrameSources.Count} items");

            foreach (var source in mediaCapture.FrameSources)
            {
                Console.WriteLine($"{source.Key}: {source.Value}");
            }

            var colorFrameSource = mediaCapture.FrameSources[colorSourceInfo.Id];

            List<MediaFrameFormat> orderedVideoResolutions = colorFrameSource.SupportedFormats.OrderByDescending(x => x.VideoFormat.Width).ToList();

            Log.WriteLine($"Found the following supportedFormats(video resolutions): {string.Join(", ", orderedVideoResolutions.Select(x => $"{x.VideoFormat.Width}x{x.VideoFormat.Height}"))}");

            var preferredFormat = orderedVideoResolutions.Where(format => format.VideoFormat.Width >= MinimumVideoWidth).FirstOrDefault();

            if (null == preferredFormat)
                throw new ApplicationException($"Our desired minimum video width ({MinimumVideoWidth}) is not supported by the imaging devices found on this machine");

            Log.WriteLine($"Selected: {preferredFormat.VideoFormat.Width}x{preferredFormat.VideoFormat.Height}, based on minimum video width requirement of >= '{MinimumVideoWidth}'");

            await AsAsync(colorFrameSource.SetFormatAsync(preferredFormat));

            mediaFrameReader = await AsAsync(mediaCapture.CreateFrameReaderAsync(colorFrameSource, MediaEncodingSubtypes.Argb32));

            if (null == mediaFrameReader)
                throw new ApplicationException($"Unable to create new mediaframereader");

            evtFrame = new EventWaitHandle(false, EventResetMode.ManualReset);
            mediaFrameReader.FrameArrived += (s, a) => evtFrame.Set();
            await AsAsync(mediaFrameReader.StartAsync());

            Log.WriteLineVerbose("FrameReader Started");
        }

        public async Task<MediaFrameReference> GetFrameAsync()
        {

            MediaFrameReference result = null;
            do
            {

                var frameReceived = evtFrame.WaitOne(4000);
                if (!frameReceived)
                {
                    throw new Exception("Unable to get exclusive lock for camera device, are other camera applications open?");
                }
                evtFrame.Reset();

                result = mediaFrameReader.TryAcquireLatestFrame();

                if (null == result)
                    await Task.Delay(10);
            }
            while (null == result);

            return result;
        }

        public async Task StopAsync()
        {
            await AsAsync(mediaFrameReader.StopAsync());

            Log.WriteLineVerbose("FrameReader Stopped");
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        public virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects).
                    mediaFrameReader?.Dispose();
                    mediaFrameReader = null;

                    mediaCapture?.Dispose();
                    mediaCapture = null;

                    evtFrame?.Dispose();
                    evtFrame = null;
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~FrameSource() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
