using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TextRecognitionOfCalls.Mvvm;
using System.Collections.ObjectModel;
using TextRecognitionOfCalls.Transcribers;
using Google.Apis.Auth.OAuth2;
using System.IO;
using Google.Apis.Util.Store;
using Google.Apis.Gmail.v1;
using System.Threading;
using Google.Apis.Services;
using Google.Apis.Auth;
using System.Threading.Tasks;
using System.Text;
using System.Net.Mail;
using Google.Apis.Gmail.v1.Data;

namespace TextRecognitionOfCalls
{
    public class MainViewModel : ReactiveObject
    {
        string fileName = @"C:\Temp\TranscribedCall.txt";
        MicrophoneTranscriber _microphoneTranscriber = new MicrophoneTranscriber();
        SystemAudioTranscriber _systemAudioTranscriber = new SystemAudioTranscriber();

        //using gmail scope
        static string[] Scopes = { GmailService.Scope.GmailSend };

        public ObservableCollection<TranscribedTextModel> TranscribedTexts { get; set; } = new ObservableCollection<TranscribedTextModel>();

        [Reactive] public string YourName { get; set; } = "Влад";
        [Reactive] public string CallerName { get; set; } = "Віталік";
        [Reactive] public string Email { get; set; }
        [Reactive] public bool IsRecording { get; set; }

        public ICommand StartCommand
        {
            get
            {
                return new DelegateCommand((o) => StartTranscribing());
            }
        }

        public ICommand StopCommand
        {
            get
            {
                return new DelegateCommand((o) => StopTranscribing());
            }
        }

        public ICommand SendCommand
        {
            get
            {
                return new DelegateCommand((o) => Send());
            }
        }

        public MainViewModel()
        {
            _microphoneTranscriber.TextTranscribed += MicrophoneTranscriber_TextTranscribed;
            _systemAudioTranscriber.TextTranscribed += SystemAudioTranscriber_TextTranscribed;

        }

        void StartTranscribing()
        {
            try
            {
                _microphoneTranscriber.StartTranscribing();

                _systemAudioTranscriber.StartTranscribing();

                IsRecording = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        void StopTranscribing()
        {
            try
            {
                _microphoneTranscriber.StopTranscribing();

                _systemAudioTranscriber.StopTranscribing();

                IsRecording = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private void SystemAudioTranscriber_TextTranscribed(object sender, TranscribedTextArgs e)
        {
            var transcribedText = TranscribedTexts.FirstOrDefault(t => t.Id == e.Id);
            if (transcribedText == null)
            {
                transcribedText = new TranscribedTextModel(e.Id, CallerName, e.Text);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TranscribedTexts.Add(transcribedText);
                });
            }
            else
            {
                transcribedText.Text = e.Text;
            }
        }

        void MicrophoneTranscriber_TextTranscribed(object sender, TranscribedTextArgs e)
        {
            var transcribedText = TranscribedTexts.FirstOrDefault(t => t.Id == e.Id);
            if (transcribedText == null)
            {
                transcribedText = new TranscribedTextModel(e.Id, YourName, e.Text);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TranscribedTexts.Add(transcribedText);
                });
            }
            else
            {
                transcribedText.Text = e.Text;
            }
        }

        async void Send()
        {
            CreateFile();
            await SendMail();
            MessageBox.Show("Успішно надіслано!", "Message");
        }

        public static string Base64UrlEncode(string input)
        {
            var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            return Convert.ToBase64String(inputBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        void CreateFile()
        {
            try
            {
                var textsList = TranscribedTexts.ToList();
                // Check if file already exists. If yes, delete it.     
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }

                // Create a new file     
                using (FileStream fs = File.Create(fileName))
                {
                    foreach (var transcribedText in textsList)
                    {
                        var str = $"{transcribedText.AuthorName}: {transcribedText.Text}\n";
                        byte[] array = new UTF8Encoding(true).GetBytes(str);
                        fs.Write(array, 0, array.Length);
                    }
                }
            }
            catch (Exception Ex)
            {
                Console.WriteLine(Ex.ToString());
            }
        }

        async Task SendMail()
        {
            UserCredential credential;
            //read credentials file
            using (FileStream stream = new FileStream("C:\\Users\\Doss\\source\\repos\\TextRecognitionOfCalls\\TextRecognitionOfCalls\\client_secret_600003400910-cnajif3betj7shlurmie9kc3itc1sv0p.apps.googleusercontent.com.json", FileMode.Open, FileAccess.Read))
            {
                string credPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                credPath = Path.Combine(credPath, ".credentials/gmail-dotnet-quickstart.json");
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(GoogleClientSecrets.FromStream(stream).Secrets, Scopes, "user", CancellationToken.None, new FileDataStore(credPath, true));
            }

            //Create Message
            MailMessage mail = new MailMessage();
            mail.Subject = "Call recognizer";
            mail.Body = $"Transcribed call at {DateTime.Now.ToShortTimeString()}";
            mail.IsBodyHtml = true;
            string attFile = fileName;
            mail.Attachments.Add(new Attachment(attFile));
            mail.To.Add(new MailAddress(Email));
            MimeKit.MimeMessage mimeMessage = MimeKit.MimeMessage.CreateFromMailMessage(mail);

            Message message = new Message();
            message.Raw = Base64UrlEncode(mimeMessage.ToString());

            //call gmail service
            var service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Call Recognizer",
            });

            service.Users.Messages.Send(message, "me").Execute();
        }
    }
}
