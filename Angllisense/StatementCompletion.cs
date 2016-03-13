//------------------------------------------------------------------------------
// <copyright file="StatementCompletion.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.InteropServices;

using Angllisense.Services;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace Angllisense {
    internal class TestCompletionCommandHandler : IOleCommandTarget {
        private readonly IOleCommandTarget nextCommandHandler;
        private ITextView textView;
        private TestCompletionHandlerProvider provider;
        private ICompletionSession m_session;



        internal TestCompletionCommandHandler(IVsTextView textViewAdapter, ITextView textView, TestCompletionHandlerProvider provider) {
            this.textView = textView;
            this.provider = provider;

            //add the command to the command chain
            textViewAdapter.AddCommandFilter(this, out this.nextCommandHandler);
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText) {
            return this.nextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
            if (VsShellUtilities.IsInAutomationFunction(this.provider.ServiceProvider)) {
                return this.nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }
            //make a copy of this so we can look at it after forwarding some commands
            uint commandID = nCmdID;
            char typedChar = char.MinValue;
            //make sure the input is a char before getting it
            if (pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR) {
                typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
            }

            //check for a commit character
            if (nCmdID == (uint)VSConstants.VSStd2KCmdID.RETURN
                || nCmdID == (uint)VSConstants.VSStd2KCmdID.TAB
                || (char.IsWhiteSpace(typedChar) || char.IsPunctuation(typedChar))) {
                //check for a a selection
                if (this.m_session != null && !this.m_session.IsDismissed) {
                    //if the selection is fully selected, commit the current session
                    if (this.m_session.SelectedCompletionSet.SelectionStatus.IsSelected) {
                        this.m_session.Commit();
                        //also, don't add the character to the buffer
                        return VSConstants.S_OK;
                    } else {
                        //if there is no selection, dismiss the session
                        this.m_session.Dismiss();
                    }
                }
            }

            //pass along the command so the char is added to the buffer
            int retVal = this.nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            bool handled = false;
            if (!typedChar.Equals(char.MinValue) && char.IsLetterOrDigit(typedChar)) {
                if (this.m_session == null || this.m_session.IsDismissed) // If there is no active session, bring up completion
                {
                    this.TriggerCompletion();
                    this.m_session.Filter();
                } else    //the completion session is already active, so just filter
                  {
                    this.m_session.Filter();
                }
                handled = true;
            } else if (commandID == (uint)VSConstants.VSStd2KCmdID.BACKSPACE   //redo the filter if there is a deletion
                  || commandID == (uint)VSConstants.VSStd2KCmdID.DELETE) {
                if (this.m_session != null && !this.m_session.IsDismissed) this.m_session.Filter();
                handled = true;
            }
            if (handled) return VSConstants.S_OK;
            return retVal;
        }

        private bool TriggerCompletion() {
            //the caret must be in a non-projection location 
            SnapshotPoint? caretPoint = this.textView.Caret.Position.Point.GetPoint(
            textBuffer => (!textBuffer.ContentType.IsOfType("projection")), PositionAffinity.Predecessor);
            if (!caretPoint.HasValue) {
                return false;
            }

            this.m_session = this.provider.CompletionBroker.CreateCompletionSession
         (this.textView,
                caretPoint.Value.Snapshot.CreateTrackingPoint(caretPoint.Value.Position, PointTrackingMode.Positive),
                true);

            //subscribe to the Dismissed event on the session 
            this.m_session.Dismissed += this.OnSessionDismissed;
            this.m_session.Start();

            return true;
        }

        private void OnSessionDismissed(object sender, EventArgs e) {
            this.m_session.Dismissed -= this.OnSessionDismissed;
            this.m_session = null;
        }
    }
    [Export(typeof (IVsTextViewCreationListener))]
    [Name("token completion handler")]
    [ContentType("htmlx")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class TestCompletionHandlerProvider : IVsTextViewCreationListener {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterService = null;
        [Import]
        internal ICompletionBroker CompletionBroker { get; set; }
        [Import]
        internal SVsServiceProvider ServiceProvider { get; set; }
        //[Import]
        //internal IContentTypeRegistryService ContentTypeRegistryService { get; set; }
        public void VsTextViewCreated(IVsTextView textViewAdapter) {
            this.Init();

            ////IVsSolution lSolution = this.ServiceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
            ////IVsSolution gSolution = Package.GetGlobalService(typeof(SVsSolution)) as IVsSolution;

            ITextView textView = this.AdapterService.GetWpfTextView(textViewAdapter);
            if (textView == null) {
                return;
            }

            Func<TestCompletionCommandHandler> createCommandHandler = delegate () { return new TestCompletionCommandHandler(textViewAdapter, textView, this); };
            textView.Properties.GetOrCreateSingletonProperty(createCommandHandler);
        }

        private object initLock = new object();
        private bool isInitted = false;
        private void Init() {
            if (this.isInitted) {
                return;
            }

            lock (initLock) {
                if (this.isInitted) {
                    return;
                }

                ProjectNavigator.GetAllTypeScriptFiles(this.ServiceProvider);

                this.isInitted = true;
            }
        }
    }

    [Export(typeof(ICompletionSourceProvider))]
    [ContentType("htmlx")]
    [Name("token completion")]
    internal class TestCompletionSourceProvider : ICompletionSourceProvider {
        [Import]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }
        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer) {
            return new TestCompletionSource(this, textBuffer);
        }
    }

    internal class TestCompletionSource : ICompletionSource {
        private readonly TestCompletionSourceProvider sourceProvider;
        private ITextBuffer textBuffer;
        private List<Completion> completions;
        public TestCompletionSource(TestCompletionSourceProvider sourceProvider, ITextBuffer textBuffer) {
            this.sourceProvider = sourceProvider;
            this.textBuffer = textBuffer;
        }

        void ICompletionSource.AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets) {
            var strList = new List<string> {
                "email-template-selector",
                "edit-page"
            };

            for (var i = 0; i < strList.Count; i++) {
                strList[i] = "<tt-" + strList[i];
            }

            this.completions = new List<Completion>();
            foreach (string str in strList) {
                this.completions.Add(new Completion(str, str, str, null, null));
            }

            var commonCompletionSet = completionSets.FirstOrDefault();

            completionSets.Clear();

            completionSets.Add(new CompletionSet(
                "Tokens",    //the non-localized title of the tab
                "Tokens",    //the display title of the tab
                this.FindTokenSpanAtPosition(session.GetTriggerPoint(this.textBuffer), session),
                this.completions,
                null));

            if (commonCompletionSet != null) {
                completionSets.Add(commonCompletionSet);
            }
        }

        private ITrackingSpan FindTokenSpanAtPosition(ITrackingPoint point, ICompletionSession session) {
            SnapshotPoint currentPoint = (session.TextView.Caret.Position.BufferPosition) - 1;
            ITextStructureNavigator navigator = this.sourceProvider.NavigatorService.GetTextStructureNavigator(this.textBuffer);
            TextExtent extent = navigator.GetExtentOfWord(currentPoint);
            return currentPoint.Snapshot.CreateTrackingSpan(extent.Span, SpanTrackingMode.EdgeInclusive);
        }

        private bool m_isDisposed;
        public void Dispose() {
            if (!this.m_isDisposed) {
                GC.SuppressFinalize(this);
                this.m_isDisposed = true;
            }
        }
    }
}
