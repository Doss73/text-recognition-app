using Google.Cloud.Speech.V1;
using Google.Protobuf;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TextRecognitionOfCalls.Transcribers
{
    public class MicrophoneTranscriber
    {
        List<string> recordingDevices = new List<string>();
        AudioRecorder audioRecorder = new AudioRecorder();

        bool monitoring = false;

        BufferedWaveProvider waveBuffer;

        // Read from the microphone and stream to API.
        WaveInEvent waveIn = new WaveInEvent();

        System.Timers.Timer timer1 = new System.Timers.Timer();

        DateTime lastTranscribedTime = DateTime.Now;
        string lastTrascribedId = Guid.NewGuid().ToString();
        string lastTranscribedText = string.Empty;

        public event EventHandler<TranscribedTextArgs> TextTranscribed;

        public MicrophoneTranscriber()
        {
            if (NAudio.Wave.WaveIn.DeviceCount < 1)
            {
                MessageBox.Show("No microphone! ... exiting");
                return;
            }

            //Mixer
            //Hook Up Audio Mic for sound peak detection
            audioRecorder.SampleAggregator.MaximumCalculated += OnRecorderMaximumCalculated;

            for (int n = 0; n < WaveIn.DeviceCount; n++)
            {
                recordingDevices.Add(WaveIn.GetCapabilities(n).ProductName);
            }

            //Set up NAudio waveIn object and events
            waveIn.DeviceNumber = 0;
            waveIn.WaveFormat = new WaveFormat(16000, 1);
            //Need to catch this event to fill our audio beffer up
            waveIn.DataAvailable += WaveIn_DataAvailable;
            //the actuall wave buffer we will be sending to googles for voice to text conversion
            waveBuffer = new BufferedWaveProvider(waveIn.WaveFormat);
            waveBuffer.DiscardOnBufferOverflow = true;

            //We are using a timer object to fire a one second record interval
            //this gets enabled and disabled based on when we get a peak detection from NAudio
            timer1.Enabled = false;
            //One second record window
            timer1.Interval = 2500;
            //Hook up to timer tick event
            timer1.Elapsed += Timer1_Tick;
        }

        /// <summary>
        /// Fires when audio peak detected. If we get a peak audio signal 
        /// above a certain threshold, start recording audio, set a timer to call us back after one second
        /// so we can stop recording and send what audio we have to googles
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void OnRecorderMaximumCalculated(object sender, MaxSampleEventArgs e)
        {
            if (!monitoring)
                return;

            float peak = Math.Max(e.MaxSample, Math.Abs(e.MinSample));

            // multiply by 100 because the Progress bar's default maximum value is 100
            peak *= 100;
            //progressBar1.Value = (int)peak;

            //Console.WriteLine("Recording Level " + peak);
            if (peak > 5)
            {
                //Timer should not be enabled, meaning, we are not already recording
                if (timer1.Enabled == false)
                {
                    timer1.Start();
                    waveIn.StartRecording();
                }

            }
        }

        /// <summary>
        /// When we get data from microphone or audio srouce, add to internal wave buffer for later use
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            waveBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        }

        /// <summary>
        /// fires after one second recording interval
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer1_Tick(object sender, EventArgs e)
        {
            //Turn off events, will get re-enabled once another audio peak gets detected
            timer1.Enabled = false;
            //Stop recording
            waveIn.StopRecording();

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
                            LanguageCode = "uk-UA",
                        },
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
            //timer1.Start();
            if (recordingDevices.Count > 0)
            {
                if (monitoring == false)
                {
                    monitoring = true;
                    //Begin
                    audioRecorder.BeginMonitoring(0);
                }
                else
                {
                    monitoring = false;
                    audioRecorder.Stop();
                }
            }
        }

        public void StopTranscribing()
        {
            monitoring = false;
            audioRecorder.Stop();

            timer1.Enabled = false;
            waveIn.StopRecording();
        }
    }
}
