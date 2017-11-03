using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace VarTypeViewer.Viewer
{
    // todo styling from TextViewer
    internal sealed class VarAdornment : TextBlock
    {
        static VarAdornment()
        {
            _color = new SolidColorBrush(Color.FromRgb(43, 145, 175));
            _color.Freeze();
        }

        private static Brush _color;
        private static readonly FontFamily Font = new FontFamily("Consolas");

        private readonly Run _spanText;

        internal VarAdornment(VarTag varTag)
        {
            FontFamily = Font;
            FontSize = 13;
            FontWeight = FontWeights.Medium;

            Inlines.Add(new Run
            {
                Text = "<"
            });

            _spanText = new Run
            {
                Foreground = _color
            };
            Inlines.Add(_spanText);
            Inlines.Add(new Run
            {
                Text = ">"
            });

            Update(varTag);

        }

        internal void Update(VarTag varTag)
        {
            _spanText.Text = varTag.TypeName;
        }
    }
}
