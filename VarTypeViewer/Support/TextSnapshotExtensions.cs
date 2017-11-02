using Microsoft.VisualStudio.Text;

namespace VarTypeViewer.Support
{
    internal static class TextSnapshotExtensions
    {
        public static SnapshotPoint GetPoint(this ITextSnapshot snapshot, int position)
        {
            return new SnapshotPoint(snapshot, position);
        }
    }
}