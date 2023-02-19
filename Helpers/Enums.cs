using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RadioStreamer_Client
{
    public enum Command
    {
        NONE = 0,
        PLAY = 1337,
        STOP,
        NEXT,
        PREVIOUS,
        SHUTDOWN,

        BEGIN_UPLOAD,
        TRANSFER,
        END_UPLOAD,

        REQUEST_NEXT_TRACK,

        INFO_SYNC,
        INFO_CURRENT_TRACK,
        INFO_PLAYLIST_TRACK,
    }
}
