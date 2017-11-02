using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Tagging;
using VarTypeViewer.Support;

namespace VarTypeViewer.Viewer
{
    internal sealed class VarTagger : ITagger<VarTag>
    {
        private readonly ITextSearchService _textSearchService;
        private readonly string _textToFind;
        private Document _document;
        private SemanticModel _semanticModel;

        public VarTagger(ITextBuffer buffer, ITextSearchService textSearchService)
        {
            _textSearchService = textSearchService;
            _textToFind = "var";

            buffer.Changed += (sender, args) => HandleBufferChanged(args);
        }

        #region ITagger implementation

        public IEnumerable<ITagSpan<VarTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            // Here we grab whole lines so that matches that only partially fall inside the spans argument are detected.
            // Note that the spans argument can contain spans that are sub-spans of lines or intersect multiple lines.
            foreach (ITextSnapshotLine line in GetIntersectingLines(spans))
            {
                FindData findData = new FindData(_textToFind, line.Snapshot)
                {
                    FindOptions = FindOptions.MatchCase | FindOptions.WholeWord
                };
                foreach (SnapshotSpan foundspan in _textSearchService.Find(findData, line))
                {
                    SnapshotPoint point = new SnapshotPoint(line.Snapshot, foundspan.Start.Position);

                    if (_document == null)
                    {
                        _document = point.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
                        _semanticModel = _document.GetSemanticModelAsync().Result;
                    }

                    SyntaxNode node =
                        _document.GetSyntaxRootAsync().Result.
                            FindToken(point).Parent.AncestorsAndSelf()
                            //OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().
                            .FirstOrDefault();
                    string typeName = null;
                    if (node is IdentifierNameSyntax)
                    {
                        if (node.Parent is VariableDeclarationSyntax variable &&
                            variable.Type != null &&
                            variable.Type.IsVar &&
                            variable.Variables.Count == 1)
                        {
                            ExpressionSyntax expression = variable.Variables[0].Initializer?.Value;
                            if (expression != null)
                            {
                                TypeInfo expressionType = ModelExtensions.GetTypeInfo(_semanticModel, expression);
                                typeName = GetVarTypeName(_semanticModel, expressionType.Type, variable.Type.GetLocation());
                            }
                        }
                        else if (node.Parent is ForEachStatementSyntax forEach
                            && forEach.Type != null
                            && forEach.Type.IsVar
                            && forEach.Expression != null)
                        {
                            ForEachStatementInfo info = _semanticModel.GetForEachStatementInfo(forEach);
                            typeName = GetVarTypeName(_semanticModel, info.ElementType, forEach.Type.GetLocation());
                        }
                        else if (node.Parent is UsingStatementSyntax usingStatement
                                 && usingStatement.Declaration.Type != null
                                 && usingStatement.Declaration.Type.IsVar
                                 && usingStatement.Declaration.Variables.Count == 1)
                        {
                            ExpressionSyntax expression = usingStatement.Declaration.Variables[0].Initializer?.Value;
                            if (expression != null)
                            {
                                TypeInfo expressionType = ModelExtensions.GetTypeInfo(_semanticModel, expression);
                                typeName = GetVarTypeName(_semanticModel, expressionType.Type, usingStatement.Declaration.Type.GetLocation());
                            }
                        }

                    }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        yield return new TagSpan<VarTag>(foundspan, new VarTag(typeName));
                    }
                }
            }
        }

        private static string GetVarTypeName(SemanticModel semanticModel, ITypeSymbol realType, Location varLocation)
        {
            //if (realType != null && !HasAnonymousType(realType))
            {
                return realType.ToMinimalDisplayString(semanticModel, varLocation.SourceSpan.Start);
            }
            //return null;
        }

        private static bool HasAnonymousType(ITypeSymbol realType)
        {
            if (realType == null)
            {
                return false;
            }
            if (realType.IsAnonymousType)
            {
                return true;
            }

            switch (realType)
            {
                case IArrayTypeSymbol arrayType
                when HasAnonymousType(arrayType.ElementType):
                    {
                        return true;

                    }
                case INamedTypeSymbol namedType
                when namedType.IsGenericType:
                    {
                        foreach (ITypeSymbol argument in namedType.TypeArguments)
                        {
                            if (HasAnonymousType(argument as INamedTypeSymbol))
                            {
                                return true;
                            }
                        }
                        break;
                    }

            }

            return false;
        }
        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        #endregion

        private static IEnumerable<ITextSnapshotLine> GetIntersectingLines(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;
            int lastVisitedLineNumber = -1;
            ITextSnapshot snapshot = spans[0].Snapshot;
            foreach (SnapshotSpan span in spans)
            {
                int firstLine = snapshot.GetLineNumberFromPosition(span.Start);
                int lastLine = snapshot.GetLineNumberFromPosition(span.End);

                for (int i = Math.Max(lastVisitedLineNumber, firstLine); i <= lastLine; i++)
                {
                    yield return snapshot.GetLineFromLineNumber(i);
                }

                lastVisitedLineNumber = lastLine;
            }
        }

        /// <summary>
        /// Handle buffer changes. The default implementation expands changes to full lines and sends out
        /// a <see cref="TagsChanged"/> event for these lines.
        /// </summary>
        /// <param name="args">The buffer change arguments.</param>
        private void HandleBufferChanged(TextContentChangedEventArgs args)
        {
            if (args.Changes.Count == 0)
                return;

            var temp = TagsChanged;
            if (temp == null)
                return;

            // Combine all changes into a single span so that
            // the ITagger<>.TagsChanged event can be raised just once for a compound edit
            // with many parts.

            ITextSnapshot snapshot = args.After;

            int start = args.Changes[0].NewPosition;
            int end = args.Changes[args.Changes.Count - 1].NewEnd;

            SnapshotSpan totalAffectedSpan = new SnapshotSpan(
                snapshot.GetLineFromPosition(start).Start,
                snapshot.GetLineFromPosition(end).End);

            temp(this, new SnapshotSpanEventArgs(totalAffectedSpan));
        }
    }
}
