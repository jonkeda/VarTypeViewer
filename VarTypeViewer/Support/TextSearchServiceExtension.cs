using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Operations;

namespace VarTypeViewer.Support
{
    public static class TextSearchServiceExtension
    {
        public static IEnumerable<SnapshotSpan> Find(this ITextSearchService textSearchService, FindData findData, ITextViewLine line)
        {
            return textSearchService.Find(findData, line.Start.Position, line.End.Position);
        }

        public static IEnumerable<SnapshotSpan> Find(this ITextSearchService textSearchService, FindData findData, int searchStart, int searchEnd)
        {
            SnapshotSpan? next;
            for (int startIndex = searchStart; startIndex < searchEnd; startIndex = next.Value.End.Position)
            {
                next = textSearchService.FindNext(startIndex, false, findData);
                if (next.HasValue && next.Value.Start.Position <= searchEnd)
                {
                    yield return next.Value;
                }
                else
                    break;
            }
        }

        public static IEnumerable<SnapshotSpan> Find(this ITextSearchService textSearchService, FindData findData, ITextSnapshotLine line)
        {
            return textSearchService.Find(findData, line.Start.Position, line.End.Position);
        }
    }
}
