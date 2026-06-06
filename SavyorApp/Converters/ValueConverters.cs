using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SavyorApp.Converters
{
    public class ExtensionToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string ext = (value as string ?? "").Trim().ToLowerInvariant();
            if (!ext.StartsWith(".") && !string.IsNullOrEmpty(ext))
            {
                ext = "." + ext;
            }

            string hexColor = ext switch
            {
                ".pdf" => "#ef4444",   // Red for PDF
                ".docx" => "#2563eb",  // Classic blue for Word
                ".doc" => "#2563eb",
                ".pptx" => "#ea580c",  // Dark orange for PowerPoint
                ".ppt" => "#ea580c",
                ".png" => "#059669",   // Dark green for images
                ".jpg" => "#059669",
                ".jpeg" => "#059669",
                ".gif" => "#059669",
                ".txt" => "#7c3aed",   // Rich violet for plain text
                ".md" => "#7c3aed",
                ".json" => "#0d9488",  // Teal for json data
                _ => "#4b5563"         // Slate for others
            };

            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class SizeFormatterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                if (bytes >= 1024 * 1024)
                {
                    return $"{(double)bytes / (1024 * 1024):F1} MB";
                }
                if (bytes >= 1024)
                {
                    return $"{(double)bytes / 1024:F0} KB";
                }
                return $"{bytes} B";
            }
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
