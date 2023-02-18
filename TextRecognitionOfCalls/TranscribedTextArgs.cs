using System;

namespace TextRecognitionOfCalls
{
    public class TranscribedTextArgs : EventArgs
    {
        public string Id { get; }
        public string Text { get; }

        public TranscribedTextArgs(string id, string text)
        {
            Id = id;
            Text = text;
        }
    }
}
