using Microsoft.Win32;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RadioStreamer_Client
{
    public class UploadVM : BaseViewModel
    {
        private string _filePath;
        private string _title;
        private long _fileSize;
        private TimeSpan _duration;

        public string FilePath { 
            get => _filePath;
            set
            {
                _filePath = value;
                OnPropertyChanged();
            }
        }
        public string Title {
            get => _title; 
            set
            {
                _title = value;
                OnPropertyChanged();
            }
        }
        public long FileSize { get => _fileSize;
            set
            {
                _fileSize = value;
                OnPropertyChanged();
            }
        }
        public TimeSpan Duration { get => _duration;
            set
            {
                _duration = value;
                OnPropertyChanged();
            }
        }

        public RelayCommand BrowseTrackCommand { get; set; }
        private void browseTrack(object obj)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.DefaultExt = ".wav";
            openFileDialog.Filter = "Wave files (*.wav)|*.wav|MPEG file (*.mp3)|*.mp3";
            if (openFileDialog.ShowDialog() == true)
            {
                FilePath = openFileDialog.FileName;
                var reader = new AudioFileReader(_filePath);
                var fileInfo = new FileInfo(_filePath);
                Duration = reader.TotalTime;
                reader.Close();
                FileSize = fileInfo.Length;
                Title = fileInfo.Name.Replace(fileInfo.Extension, string.Empty);
            }
        }

        public RelayCommand UploadCommand { get; set; }
        private async void upload(object obj)
        {
            IsBusy = true;
            try
            {
                var values = new Dictionary<string, object>
                {
                    { "FileName", Path.GetFileName(_filePath) },
                    { "FileSize", _fileSize },
                    { "Title", _title },
                    { "Duration", Convert.ToInt32(_duration.TotalSeconds) }
                };
                MainVM.Instance().SendCommand(Command.BEGIN_UPLOAD, values);


                using (var fileReader = File.OpenRead(_filePath))
                {
                    fileReader.Position = 0;
                    int readBytes = 0;
                    byte[] buff = new byte[1024 * 10];
                    var sent = 0;
                    while ((readBytes = fileReader.Read(buff, 0, 1024 * 10)) > 0)
                    {
                        await MainVM.Instance().SendTransfer(buff);
                        sent += readBytes;
                    }
                    fileReader.Close();
                }

                MainVM.Instance().SendCommand(Command.END_UPLOAD);
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
                MessageBox.Show("Something went wrong!");
            }
            IsBusy = false;
        }

        public UploadVM()
        {
            BrowseTrackCommand = new RelayCommand(browseTrack);
            UploadCommand = new RelayCommand(upload);
            _duration = new TimeSpan();
        }
    }
}
