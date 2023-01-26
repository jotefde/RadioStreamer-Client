using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Newtonsoft.Json.Linq;
using NSwag.Collections;
using RadioStreamer_Client.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace RadioStreamer_Client
{
    public class MainVM : BaseViewModel
    {
        private static readonly object _bufferSyncLock = new object();
        private static MainVM _instance = null;

        private WaveOut _player;
        private Socket _socket;
        private BufferedWaveProvider _bufferProvider;
        private MemoryStream _audioStream;
        private int _receivedBytes = 0;
        private int _currentTrackBytes = 0;
        private Thread T_receiveStream;
        private Thread T_processBuffer;
        private Thread T_player;
        private Thread T_serverStatus;
        private bool _isForceSync = false;
        private TrackEntry _nextTrack;
        private bool _isNewTrack = false;

        #region Properties
        private string _host;
        private int _port;
        private bool _isPlaying = false;
        private bool _isConnected = false;
        private TrackEntry _currentTrack;
        private ObservableCollection<TrackEntry> _trackQueue;

        public static Dispatcher WindowDispatcher;

        public string Host
        {
            get => _host;
            set
            {
                _host = value;
                OnPropertyChanged();
            }
        }

        public int Port
        {
            get => _port;
            set
            {
                _port = value;
                OnPropertyChanged();
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnected 
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
            }
        }

        public TrackEntry CurrentTrack 
        { 
            get => _currentTrack;
            set 
            {
                _currentTrack = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<TrackEntry> TrackQueue
        {
            get => _trackQueue;
            set
            {
                _trackQueue = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Commands
        public RelayCommand PlayCommand { get; set; }
        private void play(object obj)
        {
            if (IsPlaying)
                StopStreaming();
            else
                StartStreaming();
            IsPlaying = !_isPlaying;
        }

        public RelayCommand ConnectCommand { get; set; }
        private async void connect(object obj)
        {
            IsBusy = true;
            IPHostEntry ipHostInfo = Dns.GetHostEntry(Host);
            IPAddress ipAddress = ipHostInfo.AddressList[1];
            IPEndPoint ipEndPoint = new IPEndPoint(ipAddress, Port);
            if (_socket != null)
                _socket.Shutdown(SocketShutdown.Both);
            try
            {
                _socket = new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await _socket.ConnectAsync(ipEndPoint);
            }
            catch(Exception ex)
            {
                MessageBox.Show("Server is not available!");
            }
            IsBusy = false;
        }

        public RelayCommand OpenUploadWindowCommand { get; set; }
        private void openUploadWindow(object obj)
        {
            var uploadWindow = new UploadWindow();
            var uploadVM = new UploadVM();
            uploadWindow.Owner = Application.Current.MainWindow;
            uploadWindow.DataContext = uploadVM;
            uploadWindow.Show();

        }

        public RelayCommand NextTrackCommand { get; set; }
        private void nextTrack(object obj)
        {
            SendCommand(Command.NEXT);
        }

        public RelayCommand PrevTrackCommand { get; set; }
        private void prevTrack(object obj)
        {
            SendCommand(Command.PREVIOUS);
        }

        #endregion

        private void receiveStream()
        {
            var tempBuff = new byte[1_024];
            while (true)
            {
                if (!_socket.Connected)
                    break;
                int tempRecv = 0;
                try
                {
                    tempRecv = _socket.Receive(tempBuff);
                }
                catch(Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    continue;
                }

                var command = getCommand(tempBuff);
                if(command != Command.NONE)
                {
                    Console.WriteLine(command);
                    switch (command)
                    {
                        case Command.INFO_SYNC:
                            beginSyncInfo();
                            break;
                        case Command.INFO_CURRENT_TRACK:
                            syncCurrentTrack(tempBuff, tempRecv);
                            break;
                        case Command.INFO_PLAYLIST_TRACK:
                            syncPlaylist(tempBuff, tempRecv);
                            break;
                    }
                    continue;
                }

                /*
                 * if there is no command in received data
                 * it must be an audio package
                 */
                lock (_bufferSyncLock)
                {
                    _audioStream.Write(tempBuff, 0, tempRecv);
                    _receivedBytes += tempRecv;
                }
            }
        }

        private void beginSyncInfo()
        {
            _isForceSync= true;
            WindowDispatcher.Invoke(() =>
            {
                TrackQueue.Clear();
            });
        }

        private void syncCurrentTrack(byte[] data, int dataSize)
        {
            string message = getMessage(data, dataSize);
            var tempTrack = parseTrackData(message);
            Console.WriteLine(_isForceSync);
            if (_isForceSync)
            {
                CurrentTrack = parseTrackData(message);
                Console.WriteLine("Force sync, stop streaming");
                StopStreaming();
                Console.WriteLine("Force sync, start streaming");
                StartStreaming();
                _isForceSync = false;
            }
            else
            {
                _nextTrack = tempTrack;
                _isNewTrack = true;
                _currentTrackBytes = _receivedBytes;
            }
        }

        private void syncPlaylist(byte[] data, int dataSize)
        {
            string message = getMessage(data, dataSize);
            WindowDispatcher.Invoke(() =>
            {
                TrackQueue.Add(parseTrackData(message));
            });
        }

        private TrackEntry parseTrackData(string stringData)
        {
            Dictionary<string, string> values = stringData.Split(';')
                .Select(pair => pair.Split('='))
                .Where(pair => pair.Length == 2)
                .ToDictionary(sp => sp[0], sp => sp[1]);
            if (values.Count != 4)
                return new TrackEntry();

            return new TrackEntry
            {
                Index = Convert.ToInt32(values["Index"]),
                Title = values["Title"],
                Time = TimeSpan.FromSeconds(Convert.ToInt32(values["Time"])),
                Duration = TimeSpan.FromSeconds(Convert.ToInt32(values["Duration"]))
            };
        }

        private Command getCommand(byte[] package)
        {
            var cmdBytes = new byte[4];
            Buffer.BlockCopy(package, 0, cmdBytes, 0, 4);
            int cmdNumber = BitConverter.ToInt32(cmdBytes, 0);
            if (Enum.IsDefined(typeof(Command), cmdNumber))
                return (Command)cmdNumber;
            else
                return Command.NONE;
        }

        private string getMessage(byte[] data, int dataSize)
        {
            var messageBytes = new byte[dataSize - 4];
            Buffer.BlockCopy(data, 4, messageBytes, 0, dataSize - 4);
            return System.Text.Encoding.UTF8.GetString(messageBytes);
        }

        private void processBuffer()
        {
            while(true)
            {
                //Console.WriteLine(_receivedBytes);
                if (_receivedBytes == 0)
                {
                    continue;
                }

                //Console.WriteLine("ProcessBuffer: Lock");
                //if (_bufferProvider.BufferLength - _bufferProvider.BufferedBytes - _receivedBytes < _bufferProvider.WaveFormat.AverageBytesPerSecond / 4)
                //   continue;

                var availableBufferSpace = _bufferProvider.BufferLength - _bufferProvider.BufferedBytes;
                byte[] buff = new byte[availableBufferSpace];

                lock (_bufferSyncLock)
                {
                    _audioStream.Position = 0;
                    var written = _audioStream.Read(buff, 0, availableBufferSpace);
                    _bufferProvider.AddSamples(buff, 0, written);
                    _receivedBytes -= written;
                    _currentTrackBytes -= written;
                    if (_isNewTrack)
                    {
                        if (_currentTrackBytes <= 0)
                        {
                            Task.Factory.StartNew(async () =>
                            {
                                await Task.Delay(5000);
                                CurrentTrack = _nextTrack;
                            });
                            _isNewTrack = false;
                        }
                    }

                    var streamBuff = new byte[_receivedBytes];
                    Buffer.BlockCopy(_audioStream.ToArray(), written, streamBuff, 0, _receivedBytes);
                    _audioStream = new MemoryStream();
                    _audioStream.Write(streamBuff, 0, _receivedBytes);
                }
                //Console.WriteLine("ProcessBuffer: Unlock");
            }
        }
        private void serverStatus()
        {
            while(true)
            {
                IsConnected = _socket != null && _socket.Connected;
                if(IsPlaying)
                {
                    /*Console.WriteLine($"Buffered bytes: {_bufferProvider.BufferedBytes}");
                    Console.WriteLine($"Buffered duration: {_bufferProvider.BufferedDuration}");
                    Console.WriteLine($"Buffer length: {_bufferProvider.BufferLength}");*/
                }
                Thread.Sleep(5000);
            }
        }

        public void SendCommand(Command cmd, Dictionary<string, object> values = null)
        {
            byte[] cmdBytes = BitConverter.GetBytes((int)cmd);

            string valueStr = "";
            if(values != null)
            {
                var joinedArr = values.Select(kv => kv.Key + "=" + Convert.ToString(kv.Value)).ToArray();
                valueStr = String.Join(";", joinedArr) + ";";
            }

            var valuesBytes = Encoding.UTF8.GetBytes(valueStr);
            var messageBytes = new byte[cmdBytes.Length + valuesBytes.Length];
            Buffer.BlockCopy(cmdBytes, 0, messageBytes, 0, cmdBytes.Length);
            Buffer.BlockCopy(valuesBytes, 0, messageBytes, cmdBytes.Length, valuesBytes.Length);
            _ = _socket.Send(messageBytes);
        }

        public async Task SendTransfer(byte[] buff)
        {
            _ = _socket.Send(buff);
            await Task.Delay(1);
        }

        public void StartStreaming()
        {
            T_receiveStream = new Thread(new ThreadStart(receiveStream));
            T_processBuffer = new Thread(new ThreadStart(processBuffer));
            T_receiveStream.Start();
            SendCommand(Command.PLAY);
            T_processBuffer.Start();
            T_player = new Thread(new ThreadStart(_player.Play));
            T_player.Start();
        }

        public void StopStreaming()
        {
            _player.Stop();
            lock (_bufferSyncLock)
            {
                T_processBuffer.Abort();
                SendCommand(Command.STOP);
                T_receiveStream.Abort();
                T_player.Abort();
                _receivedBytes = _currentTrackBytes = 0;
                _isNewTrack = false;
                _audioStream = new MemoryStream();
                _bufferProvider.ClearBuffer();
            }
        }

        public static MainVM Instance()
        {
            if (_instance == null)
                _instance = new MainVM();
            return _instance;
        }

        public MainVM()
        {
            Host = "localhost";
            Port = 1337;
            //_audioStream = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(32, 2));
            PlayCommand = new RelayCommand(play);
            ConnectCommand = new RelayCommand(connect);
            OpenUploadWindowCommand = new RelayCommand(openUploadWindow);
            NextTrackCommand = new RelayCommand(nextTrack);
            PrevTrackCommand = new RelayCommand(prevTrack);

            _player = new WaveOut();
            _bufferProvider = new BufferedWaveProvider(new WaveFormat());
            _player.Init(_bufferProvider);
            _audioStream = new MemoryStream();

            T_receiveStream = new Thread(new ThreadStart(receiveStream));
            T_processBuffer = new Thread(new ThreadStart(processBuffer));
            T_serverStatus = new Thread(new ThreadStart(serverStatus));
            T_serverStatus.Start();
            CurrentTrack = new TrackEntry
            {
                Duration = TimeSpan.FromSeconds(142),
                Time = TimeSpan.FromSeconds(79),
                Title = "Some track title"
            };
            TrackQueue = new ObservableCollection<TrackEntry> { 
                new TrackEntry {Title = "Track #1"},
                new TrackEntry {Title = "Track #2"},
                new TrackEntry {Title = "Track #3"},
            };
        }

        public void Close()
        {
            _audioStream.Close();
            _audioStream.Dispose();
            _bufferProvider.ClearBuffer();
            _player.Stop();
            _player.Dispose();
            if (_socket != null)
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
                _socket.Dispose();
            }
            T_serverStatus.Abort();
        }
    }
}
