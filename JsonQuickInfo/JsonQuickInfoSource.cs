using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json;

namespace JsonQuickInfo
{
    internal class JsonQuickInfoSource : IQuickInfoSource
    {
        private JsonQuickInfoSourceProvider m_provider;
        private bool m_isDisposed;
        private ITextBuffer m_subjectBuffer;

        public JsonQuickInfoSource(JsonQuickInfoSourceProvider provider, ITextBuffer subjectBuffer)
        {
            m_provider = provider;
            m_subjectBuffer = subjectBuffer;
        }

        private const long InitialJavaScriptTicks = 621355968000000000;

        public void AugmentQuickInfoSession(IQuickInfoSession session, IList<object> qiContent, out ITrackingSpan applicableToSpan)
        {
            // Map the trigger point down to our buffer.
            SnapshotPoint? subjectTriggerPoint = session.GetTriggerPoint(m_subjectBuffer.CurrentSnapshot);
            if (!subjectTriggerPoint.HasValue)
            {
                applicableToSpan = null;
                return;
            }

            ITextSnapshot currentSnapshot = subjectTriggerPoint.Value.Snapshot;
            SnapshotSpan querySpan = new SnapshotSpan(subjectTriggerPoint.Value, 0);

            //look for occurrences of our QuickInfo words in the span
            ITextStructureNavigator navigator = m_provider.NavigatorService.GetTextStructureNavigator(m_subjectBuffer);

            var bracketsSpan = navigator.GetSpanOfEnclosing(querySpan);
            var dateLabelSpan = navigator.GetSpanOfPreviousSibling(querySpan);

            //TextExtent extent = navigator.GetExtentOfWord(subjectTriggerPoint.Value);
            string dateLabelText = dateLabelSpan.GetText();

            //foreach (string key in m_dictionary.Keys)
            //{
            ////int foundIndex = searchText.IndexOf("Date", StringComparison.CurrentCultureIgnoreCase);
            ////if (foundIndex > -1)
            if (dateLabelText == "Date")
            {
                applicableToSpan = currentSnapshot.CreateTrackingSpan
                (
                    //querySpan.Start.Add(foundIndex).Position, 9, SpanTrackingMode.EdgeInclusive
                    //extent.Span.Start + foundIndex, brackets.Length, SpanTrackingMode.EdgeInclusive
                    bracketsSpan, SpanTrackingMode.EdgeInclusive
                );

                var bracketedDate = bracketsSpan.GetText();
                var regex = new Regex(@"(?<date>-?\d+)((?<direction>[+-])(?<hour>\d\d)(?<minute>\d\d))?");
                var match = regex.Match(bracketedDate);
                if (match.Success)
                {

                    long.TryParse(match.Groups["date"].Value, out var javascriptTicks);
                    var date = new DateTime((javascriptTicks * 10_000) + InitialJavaScriptTicks, DateTimeKind.Utc);

                    int.TryParse(match.Groups["hour"].Value, out var timezoneHours);
                    int.TryParse(match.Groups["minute"].Value, out var timezoneMinutes);
                    var offset = TimeSpan.FromHours(timezoneHours) + TimeSpan.FromMinutes( timezoneMinutes);
                    if (match.Groups["direction"].Value == "-")
                    {
                        offset = offset.Negate();
                    }
                    var tzDate = new DateTimeOffset(date.Ticks, offset);

                    qiContent.Add(bracketsSpan.GetText());
                    qiContent.Add(tzDate.ToString());
                }
                return;
            }
            //}        
            applicableToSpan = null;
        }

        public void Dispose()
        {
            if (!m_isDisposed)
            {
                GC.SuppressFinalize(this);
                m_isDisposed = true;
            }
        }

    }

    [Export(typeof(IQuickInfoSourceProvider))]
    [Name("ToolTip QuickInfo Source")]
    [Order(Before = "Default Quick Info Presenter")]
    [ContentType("text")]
    internal class JsonQuickInfoSourceProvider : IQuickInfoSourceProvider
    {
        [Import]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        [Import]
        internal ITextBufferFactoryService TextBufferFactoryService { get; set; }

        public IQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return new JsonQuickInfoSource(this, textBuffer);
        }
    }

    internal class JsonQuickInfoController : IIntellisenseController
    {
        private ITextView m_textView;
        private IList<ITextBuffer> m_subjectBuffers;
        private JsonQuickInfoControllerProvider m_provider;
        private IQuickInfoSession m_session;

        internal JsonQuickInfoController(ITextView textView, IList<ITextBuffer> subjectBuffers, JsonQuickInfoControllerProvider provider)
        {
            m_textView = textView;
            m_subjectBuffers = subjectBuffers;
            m_provider = provider;

            m_textView.MouseHover += this.OnTextViewMouseHover;
        }

        private void OnTextViewMouseHover(object sender, MouseHoverEventArgs e)
        {
            //find the mouse position by mapping down to the subject buffer
            SnapshotPoint? point = m_textView.BufferGraph.MapDownToFirstMatch
                 (new SnapshotPoint(m_textView.TextSnapshot, e.Position),
                PointTrackingMode.Positive,
                snapshot => m_subjectBuffers.Contains(snapshot.TextBuffer),
                PositionAffinity.Predecessor);

            if (point != null)
            {
                ITrackingPoint triggerPoint = point.Value.Snapshot.CreateTrackingPoint(point.Value.Position,
                PointTrackingMode.Positive);

                if (!m_provider.QuickInfoBroker.IsQuickInfoActive(m_textView))
                {
                    m_session = m_provider.QuickInfoBroker.TriggerQuickInfo(m_textView, triggerPoint, true);
                }
            }
        }

        public void Detach(ITextView textView)
        {
            if (m_textView == textView)
            {
                m_textView.MouseHover -= this.OnTextViewMouseHover;
                m_textView = null;
            }
        }

        public void ConnectSubjectBuffer(ITextBuffer subjectBuffer)
        {
        }

        public void DisconnectSubjectBuffer(ITextBuffer subjectBuffer)
        {
        }
    }

    [Export(typeof(IIntellisenseControllerProvider))]
    [Name("ToolTip QuickInfo Controller")]
    [ContentType("text")]
    internal class JsonQuickInfoControllerProvider : IIntellisenseControllerProvider
    {
        [Import]
        internal IQuickInfoBroker QuickInfoBroker { get; set; }

        public IIntellisenseController TryCreateIntellisenseController(ITextView textView, IList<ITextBuffer> subjectBuffers)
        {
            return new JsonQuickInfoController(textView, subjectBuffers, this);
        }
    }
}
