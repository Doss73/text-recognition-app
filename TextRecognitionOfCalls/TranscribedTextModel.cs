using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TextRecognitionOfCalls
{
    public class TranscribedTextModel : ReactiveObject
    {
        public string Id { get; set; }
        [Reactive] public string AuthorName { get; set; }
        [Reactive] public string Text { get; set; }

        public TranscribedTextModel(string id, string authorName, string text)
        {
            Id = id;
            AuthorName = authorName;
            Text = text;
        }
    }
}
