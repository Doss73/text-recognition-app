using NAudio.Mixer;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TextRecognitionOfCalls
{
    public class SystemAudioRecorder
    {
        WasapiLoopbackCapture wasapiLoopbackCapture;
        private SampleAggregator sampleAggregator;
        RecordingState recordingState;
        WaveFileWriter writer;
        WaveFormat recordingFormat;

        public event EventHandler Stopped = delegate { };

        public SystemAudioRecorder()
        {
            sampleAggregator = new SampleAggregator();
            RecordingFormat = new WaveFormat(44100, 1);
        }

        public WaveFormat RecordingFormat
        {
            get
            {
                return recordingFormat;
            }
            set
            {
                recordingFormat = value;
                sampleAggregator.NotificationCount = value.SampleRate / 10;
            }
        }

        public void BeginMonitoring(int recordingDevice)
        {
            if (recordingState != RecordingState.Stopped)
            {
                throw new InvalidOperationException("Can't begin monitoring while we are in this state: " + recordingState.ToString());
            }

            wasapiLoopbackCapture = new WasapiLoopbackCapture();
            wasapiLoopbackCapture.DataAvailable += OnDataAvailable;
            wasapiLoopbackCapture.RecordingStopped += OnRecordingStopped;
            //wasapiLoopbackCapture.WaveFormat = recordingFormat;
            wasapiLoopbackCapture.StartRecording();
            
            recordingState = RecordingState.Monitoring;
        }

        void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            recordingState = RecordingState.Stopped;
            Stopped(this, EventArgs.Empty);
        }

        public void BeginRecording(string waveFileName)
        {
            if (recordingState != RecordingState.Monitoring)
            {
                throw new InvalidOperationException("Can't begin recording while we are in this state: " + recordingState.ToString());
            }

            writer = new WaveFileWriter(waveFileName, wasapiLoopbackCapture.WaveFormat);
            recordingState = RecordingState.Recording;
        }

        public void Stop()
        {

            recordingState = RecordingState.RequestedStop;
            wasapiLoopbackCapture.StopRecording();

        }

        public SampleAggregator SampleAggregator
        {
            get
            {
                return sampleAggregator;
            }
        }

        public RecordingState RecordingState
        {
            get
            {
                return recordingState;
            }
        }

        public TimeSpan RecordedTime
        {
            get
            {
                if (writer == null)
                {
                    return TimeSpan.Zero;
                }

                return TimeSpan.FromSeconds((double)writer.Length / writer.WaveFormat.AverageBytesPerSecond);
            }
        }

        void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            byte[] buffer = e.Buffer;
            int bytesRecorded = e.BytesRecorded;
            //WriteToFile(buffer, bytesRecorded);

            for (int index = 0; index < e.BytesRecorded; index += 2)
            {
                short sample = (short)((buffer[index + 1] << 8) |
                                        buffer[index + 0]);
                float sample32 = sample / 32768f;
                sampleAggregator.Add(sample32);
            }
        }

        private void WriteToFile(byte[] buffer, int bytesRecorded)
        {
            long maxFileLength = this.recordingFormat.AverageBytesPerSecond * 60;

            if (recordingState == RecordingState.Recording || recordingState == RecordingState.RequestedStop)
            {
                var toWrite = (int)Math.Min(maxFileLength - writer.Length, bytesRecorded);
                if (toWrite > 0)
                {
                    writer.Write(buffer, 0, bytesRecorded);
                }
                else
                {
                    Stop();
                }
            }
        }
    }
}
