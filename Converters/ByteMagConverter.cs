using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace RadioStreamer_Client.Converters
{
    public class ByteMagConverter : IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            double bytes = (long)value;
            string mag = "Bytes";
            if (bytes > 1000)
            {
                bytes /= 1000.0;
                mag = "kB";
            }
            if (bytes > 1000)
            {
                bytes /= 1000.0;
                mag = "MB";
            }
            if (bytes > 1000)
            {
                bytes /= 1000.0;
                mag = "GB";
            }

            return string.Join(" ", new object[] { Math.Round(bytes, 2), mag });
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
