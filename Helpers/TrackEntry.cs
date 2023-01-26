using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RadioStreamer_Client.Helpers
{
    public class TrackEntry : BaseViewModel
    {
        private int _index;
        private string _title;
        private TimeSpan _time;
        private TimeSpan _duration;

        public string Title { get => _title;
            set
            {
                _title = value;
                OnPropertyChanged();
            }
        }
        public TimeSpan Time { get => _time;
            set
            {
                _time = value;
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

        public int Index { get => _index;
            set
            {
                _index = value;
                OnPropertyChanged();
            }
        }
    }
}
