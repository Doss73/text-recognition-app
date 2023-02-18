using Google.Cloud.Speech.V1;
using Google.Protobuf;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TextRecognitionOfCalls
{
    public class SystemAudioTranscriber
    {
        SystemAudioRecorder audioRecorder = new SystemAudioRecorder();

        bool monitoring = false;

        BufferedWaveProvider waveBuffer;

        // Read from the microphone and stream to API.
        WasapiLoopbackCapture wasapiLoopbackCapture = new WasapiLoopbackCapture();

        System.Timers.Timer timer1 = new System.Timers.Timer();

        DateTime lastTranscribedTime = DateTime.Now;
        string lastTrascribedId = Guid.NewGuid().ToString();
        string lastTranscribedText = string.Empty;

        public event EventHandler<TranscribedTextArgs> TextTranscribed;

        public SystemAudioTranscriber()
        {
            wasapiLoopbackCapture.DataAvailable += WaveIn_DataAvailable;
            //the actuall wave buffer we will be sending to googles for voice to text conversion
            waveBuffer = new BufferedWaveProvider(new WaveFormat(16000, 1));
            waveBuffer.DiscardOnBufferOverflow = true;
            //We are using a timer object to fire a one second record interval
            //this gets enabled and disabled based on when we get a peak detection from NAudio
            //One second record window
            timer1.Interval = 1000;
            //Hook up to timer tick event
            timer1.Elapsed += Timer1_Tick;
        }

        /// <summary>
        /// When we get data from microphone or audio srouce, add to internal wave buffer for later use
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {

            // Create a WaveStream from the input buffer.
            var memStream = new MemoryStream(e.Buffer, 0, e.BytesRecorded);
            var inputStream = new RawSourceWaveStream(memStream, wasapiLoopbackCapture.WaveFormat);
            var sampleStream = new WaveToSampleProvider(inputStream);

            var monoStream = new StereoToMonoSampleProvider(sampleStream)
            {
                LeftVolume = 1f,
                RightVolume = 1f
            };

            var resamplingProvider = new WdlResamplingSampleProvider(monoStream, 16000);

            // Convert the input stream to a WaveProvider in 16bit PCM format with sample rate of 48000 Hz.
            var convertedPCM = new SampleToWaveProvider16(resamplingProvider);

            byte[] convertedBuffer = new byte[e.BytesRecorded];

            var stream = new MemoryStream();
            int read;

            // Read the converted WaveProvider into a buffer and turn it into a Stream.
            while ((read = convertedPCM.Read(convertedBuffer, 0, e.BytesRecorded)) > 0)
                stream.Write(convertedBuffer, 0, read);
            var result = stream.ToArray();

            waveBuffer.AddSamples(result, 0, result.Length);
        }

        /// <summary>
        /// fires after one second recording interval
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer1_Tick(object sender, EventArgs e)
        {
            //Call the async google voice stream method with our saved audio buffer
            Task me = StreamBufferToGooglesAsync();
        }

        /// <summary>
        /// Wave in recording task gets called when we think we have enough audio to send to googles
        /// </summary>
        /// <param name="seconds"></param>
        /// <returns></returns>
        private async Task<object> StreamBufferToGooglesAsync()
        {
            try
            {
                //I don't like having to re-create these everytime, but breaking the
                //code out is for another refactoring.
                var speech = SpeechClient.Create();
                var streamingCall = speech.StreamingRecognize();

                // Write the initial request with the config.
                //Again, this is googles code example, I tried unrolling this stuff
                //and the google api stopped working, so stays like this for now
                await streamingCall.WriteAsync(new StreamingRecognizeRequest()
                {
                    StreamingConfig = new StreamingRecognitionConfig()
                    {
                        Config = new RecognitionConfig()
                        {
                            Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                            SampleRateHertz = 16000,
                            LanguageCode = "uk-UA"
                        },

                        //Note: play with this value
                        // InterimResults = true,  // this needs to be true for real time
                        SingleUtterance = true,
                    }
                });

                //Get what ever data we have in our internal wave buffer and put into
                //byte array for googles
                byte[] buffer = new byte[waveBuffer.BufferLength];
                int offset = 0;
                int count = waveBuffer.BufferLength;

                //Gulp ... yummy bytes ....
                waveBuffer.Read(buffer, offset, count);

                try
                {
                    //Sending to Googles .... finally
                    streamingCall.WriteAsync(new StreamingRecognizeRequest()
                    {
                        AudioContent = ByteString.CopyFrom(buffer, 0, count)
                    }).Wait();
                }
                catch (Exception wtf)
                {
                    string wtfMessage = wtf.Message;
                }

                //Again, this is googles code example below, I tried unrolling this stuff
                //and the google api stopped working, so stays like this for now

                //Print responses as they arrive. Need to move this into a method for cleanslyness
                Task printResponses = Task.Run(async () =>
                {
                    while (await streamingCall.GetResponseStream().MoveNextAsync(default(CancellationToken)))
                    {
                        var results = streamingCall.GetResponseStream().Current.Results;
                        var result = streamingCall.GetResponseStream().Current.Results.LastOrDefault();
                        if (result != null && result.IsFinal)
                        {
                            var estimatedTime = DateTime.Now - lastTranscribedTime;
                            if (estimatedTime.TotalSeconds > 4)
                            {
                                lastTrascribedId = Guid.NewGuid().ToString();
                                lastTranscribedText = result.Alternatives.LastOrDefault()?.Transcript;
                            }
                            else
                            {

                                lastTranscribedText = lastTranscribedText + " " + result.Alternatives.LastOrDefault()?.Transcript;
                            }

                            TextTranscribed?.Invoke(this, new TranscribedTextArgs(lastTrascribedId, lastTranscribedText));
                            lastTranscribedTime = DateTime.Now;
                        }
                    }
                });

                //Clear our internal wave buffer
                waveBuffer.ClearBuffer();

                //Tell googles we are done for now
                await streamingCall.WriteCompleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return 0;
        }

        public void StartTranscribing()
        {
            if (monitoring == false)
            {
                monitoring = true;
                //Begin
                audioRecorder.BeginMonitoring(0);

                timer1.Enabled = true;
                wasapiLoopbackCapture.StartRecording();
            }
            else
            {
                monitoring = false;
                audioRecorder.Stop();
            }
        }

        public void StopTranscribing()
        {
            monitoring = false;
            audioRecorder.Stop();
            timer1.Enabled = false;
            wasapiLoopbackCapture.StopRecording();
        }
    }
}
