using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using VarTypeViewer.Services;

namespace VarTypeViewer.Viewer
{
    internal sealed class VarAdornmentTagger
        : ITagger<IntraTextAdornmentTag>
    {
        internal static ITagger<IntraTextAdornmentTag> GetTagger(IWpfTextView view, Lazy<ITagAggregator<VarTag>> varTagger)
        {
            return view.Properties.GetOrCreateSingletonProperty(() => new VarAdornmentTagger(view, varTagger.Value));
        }

        private readonly ITagAggregator<VarTag> _varTagger;
        private readonly IWpfTextView _view;
        private Dictionary<SnapshotSpan, VarAdornment> _adornmentCache = new Dictionary<SnapshotSpan, VarAdornment>();
        private ITextSnapshot _snapshot;
        private readonly List<SnapshotSpan> _invalidatedSpans = new List<SnapshotSpan>();

        private VarAdornmentTagger(IWpfTextView view, ITagAggregator<VarTag> varTagger)
        {
            _view = view;
            _snapshot = view.TextBuffer.CurrentSnapshot;

            _view.LayoutChanged += HandleLayoutChanged;
            _view.TextBuffer.Changed += HandleBufferChanged;

            _varTagger = varTagger;
        }

        public void Dispose()
        {
            _varTagger.Dispose();

            _view.Properties.RemoveProperty(typeof(VarAdornmentTagger));
        }

        /// <param name="spans">Spans to provide adornment data for. These spans do not necessarily correspond to text lines.</param>
        /// <remarks>
        /// If adornments need to be updated, call <see cref="RaiseTagsChanged"/> or <see cref="InvalidateSpans"/>.
        /// This will, indirectly, cause <see cref="GetAdornmentData"/> to be called.
        /// </remarks>
        /// <returns>
        /// A sequence of:
        ///  * adornment data for each adornment to be displayed
        ///  * the span of text that should be elided for that adornment (zero length spans are acceptable)
        ///  * and affinity of the adornment (this should be null if and only if the elided span has a length greater than zero)
        /// </returns>

        // To produce adornments that don't obscure the text, the adornment tags
        // should have zero length spans. Overriding this method allows control
        // over the tag spans.
        private IEnumerable<Tuple<SnapshotSpan, PositionAffinity?, VarTag>> GetAdornmentData(NormalizedSnapshotSpanCollection spans)
        {
            if (!VarServices.ShowVarType)
            {
                yield break;
            }

            if (spans.Count == 0)
            {
                yield break;
            }

            ITextSnapshot snapshot = spans[0].Snapshot;

            var varTags = _varTagger.GetTags(spans);

            foreach (IMappingTagSpan<VarTag> dataTagSpan in varTags)
            {
                NormalizedSnapshotSpanCollection varTagSpans = dataTagSpan.Span.GetSpans(snapshot);

                // Ignore data tags that are split by projection.
                // This is theoretically possible but unlikely in current scenarios.
                if (varTagSpans.Count != 1)
                    continue;

                SnapshotSpan adornmentSpan = new SnapshotSpan(varTagSpans[0].Start + varTagSpans[0].Length, 0);

                yield return Tuple.Create(adornmentSpan, (PositionAffinity?)PositionAffinity.Successor, dataTagSpan.Tag);
            }
        }

        /// <param name="data"></param>
        /// <param name="span">The span of text that this adornment will elide.</param>
        /// <returns>Adornment corresponding to given data. May be null.</returns>
        private VarAdornment CreateAdornment(VarTag data, SnapshotSpan span)
        {
            return new VarAdornment(data);
        }

        /// <returns>True if the adornment was updated and should be kept. False to have the adornment removed from the view.</returns>
        private bool UpdateAdornment(VarAdornment adornment, VarTag dataTag)
        {
            adornment.Update(dataTag);
            return true;
        }


        private void HandleBufferChanged(object sender, TextContentChangedEventArgs args)
        {
            var editedSpans = args.Changes.Select(change => new SnapshotSpan(args.After, change.NewSpan)).ToList();
            InvalidateSpans(editedSpans);
        }

        /// <summary>
        /// Causes intra-text adornments to be updated asynchronously.
        /// </summary>
        private void InvalidateSpans(IList<SnapshotSpan> spans)
        {
            lock (_invalidatedSpans)
            {
                bool wasEmpty = _invalidatedSpans.Count == 0;
                _invalidatedSpans.AddRange(spans);

                if (wasEmpty && _invalidatedSpans.Count > 0)
                {
                    _view.VisualElement.Dispatcher.BeginInvoke(new Action(AsyncUpdate));
                }
            }
        }

        private void AsyncUpdate()
        {
            // Store the snapshot that we're now current with and send an event
            // for the text that has changed.
            if (_snapshot != _view.TextBuffer.CurrentSnapshot)
            {
                _snapshot = _view.TextBuffer.CurrentSnapshot;

                Dictionary<SnapshotSpan, VarAdornment> translatedAdornmentCache = new Dictionary<SnapshotSpan, VarAdornment>();

                foreach (var keyValuePair in _adornmentCache)
                {
                    translatedAdornmentCache.Add(keyValuePair.Key.TranslateTo(_snapshot, SpanTrackingMode.EdgeExclusive), keyValuePair.Value);
                }

                _adornmentCache = translatedAdornmentCache;
            }

            List<SnapshotSpan> translatedSpans;
            lock (_invalidatedSpans)
            {
                translatedSpans = _invalidatedSpans.Select(s => s.TranslateTo(_snapshot, SpanTrackingMode.EdgeInclusive)).ToList();
                _invalidatedSpans.Clear();
            }

            if (translatedSpans.Count == 0)
            {
                return;
            }

            var start = translatedSpans.Select(span => span.Start).Min();
            var end = translatedSpans.Select(span => span.End).Max();

            RaiseTagsChanged(new SnapshotSpan(start, end));
        }

        /// <summary>
        /// Causes intra-text adornments to be updated synchronously.
        /// </summary>
        private void RaiseTagsChanged(SnapshotSpan span)
        {
            var handler = TagsChanged;
            handler?.Invoke(this, new SnapshotSpanEventArgs(span));
        }

        private void HandleLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            SnapshotSpan visibleSpan = _view.TextViewLines.FormattedSpan;

            // Filter out the adornments that are no longer visible.
            List<SnapshotSpan> toRemove = new List<SnapshotSpan>(
                from keyValuePair
                in _adornmentCache
                where !keyValuePair.Key.TranslateTo(visibleSpan.Snapshot, SpanTrackingMode.EdgeExclusive).IntersectsWith(visibleSpan)
                select keyValuePair.Key);

            foreach (var span in toRemove)
            {
                _adornmentCache.Remove(span);
            }
        }


        // Produces tags on the snapshot that the tag consumer asked for.
        public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans) //hier
        {
            if (spans == null || spans.Count == 0)
            {
                yield break;
            }

            if (!VarServices.ShowVarType)
            {
                _adornmentCache.Clear();
                yield break;
            }

            // Translate the request to the snapshot that this tagger is current with.

            ITextSnapshot requestedSnapshot = spans[0].Snapshot;

            var translatedSpans = new NormalizedSnapshotSpanCollection(spans.Select(span => span.TranslateTo(_snapshot, SpanTrackingMode.EdgeExclusive)));

            // Grab the adornments.
            foreach (var tagSpan in GetAdornmentTagsOnSnapshot(translatedSpans))
            {
                // Translate each adornment to the snapshot that the tagger was asked about.
                SnapshotSpan span = tagSpan.Span.TranslateTo(requestedSnapshot, SpanTrackingMode.EdgeExclusive);

                IntraTextAdornmentTag tag = new IntraTextAdornmentTag(tagSpan.Tag.Adornment, tagSpan.Tag.RemovalCallback, tagSpan.Tag.Affinity);
                yield return new TagSpan<IntraTextAdornmentTag>(span, tag);
            }
        }

        // Produces tags on the snapshot that this tagger is current with.
        private IEnumerable<TagSpan<IntraTextAdornmentTag>> GetAdornmentTagsOnSnapshot(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
            {
                yield break;
            }

            ITextSnapshot snapshot = spans[0].Snapshot;

            System.Diagnostics.Debug.Assert(snapshot == _snapshot);

            // Since WPF UI objects have state (like mouse hover or animation) and are relatively expensive to create and lay out,
            // this code tries to reuse controls as much as possible.
            // The controls are stored in this.adornmentCache between the calls.

            // Mark which adornments fall inside the requested spans with Keep=false
            // so that they can be removed from the cache if they no longer correspond to data tags.
            HashSet<SnapshotSpan> toRemove = new HashSet<SnapshotSpan>();
            foreach (var ar in _adornmentCache)
                if (spans.IntersectsWith(new NormalizedSnapshotSpanCollection(ar.Key)))
                {
                    toRemove.Add(ar.Key);
                }

            foreach (var spanDataPair in GetAdornmentData(spans).Distinct(new Comparer()))
            {
                // Look up the corresponding adornment or create one if it's new.
                SnapshotSpan snapshotSpan = spanDataPair.Item1;
                PositionAffinity? affinity = spanDataPair.Item2;
                VarTag adornmentData = spanDataPair.Item3;
                if (_adornmentCache.TryGetValue(snapshotSpan, out var adornment))
                {
                    if (UpdateAdornment(adornment, adornmentData))
                    {
                        toRemove.Remove(snapshotSpan);
                    }
                }
                else
                {
                    adornment = CreateAdornment(adornmentData, snapshotSpan);

                    if (adornment == null)
                    {
                        continue;
                    }

                    // Get the adornment to measure itself. Its DesiredSize property is used to determine
                    // how much space to leave between text for this adornment.
                    // Note: If the size of the adornment changes, the line will be reformatted to accommodate it.
                    // Note: Some adornments may change size when added to the view's visual tree due to inherited
                    // dependency properties that affect layout. Such options can include SnapsToDevicePixels,
                    // UseLayoutRounding, TextRenderingMode, TextHintingMode, and TextFormattingMode. Making sure
                    // that these properties on the adornment match the view's values before calling Measure here
                    // can help avoid the size change and the resulting unnecessary re-format.
                    adornment.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                    _adornmentCache.Add(snapshotSpan, adornment);
                }

                yield return new TagSpan<IntraTextAdornmentTag>(snapshotSpan, new IntraTextAdornmentTag(adornment, null, affinity));
            }

            foreach (var snapshotSpan in toRemove)
            {
                _adornmentCache.Remove(snapshotSpan);
            }
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        private class Comparer : IEqualityComparer<Tuple<SnapshotSpan, PositionAffinity?, VarTag>>
        {
            public bool Equals(Tuple<SnapshotSpan, PositionAffinity?, VarTag> x, Tuple<SnapshotSpan, PositionAffinity?, VarTag> y)
            {
                if (x == null && y == null)
                {
                    return true;
                }
                if (x == null || y == null)
                {
                    return false;
                }
                return x.Item1.Equals(y.Item1);
            }

            public int GetHashCode(Tuple<SnapshotSpan, PositionAffinity?, VarTag> obj)
            {
                return obj.Item1.GetHashCode();
            }
        }

    }
}
