using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Document = Microsoft.CodeAnalysis.Document;
using Task = System.Threading.Tasks.Task;

namespace HandlerFinder
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class FindHandlerCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("222993a5-b14f-4206-9ba3-00cf383c7d99");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="FindHandlerCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private FindHandlerCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(this.Execute, menuCommandID);
            menuItem.BeforeQueryStatus += MenuItem_BeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Handler for the BeforeQueryStatus event.
        /// Checks whether the command button should be visible.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand command)
            {
                command.Visible = false;
                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    (var node, var document) = await GetCurrentTokenAndDocumentAsync();
                    command.Visible = IsSyntaxNodeSupported(node);
                });
            }
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static FindHandlerCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in FindHandlerCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new FindHandlerCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                (var node, var document) = await GetCurrentTokenAndDocumentAsync();

                if (IsSyntaxNodeSupported(node))
                {
                    SemanticModel semanticModel = await document.GetSemanticModelAsync();

                    var symbolInfo = semanticModel.GetSymbolInfo(node);
                    var typeInfo = semanticModel.GetTypeInfo(node);

                    var symbol = semanticModel.GetDeclaredSymbol(node);
                    
                    var containingNamespace = symbol.ContainingNamespace;

                    System.Windows.Forms.Clipboard.SetText(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
                }

            });

            string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.MenuItemCallback()", this.GetType().FullName);
            string title = "FindHandlerCommand";

            // Show a message box to prove we were here
            VsShellUtilities.ShowMessageBox(
                this.package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        /// <summary>
        /// Checks if the syntax node type is supported
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private bool IsSyntaxNodeSupported(SyntaxNode node)
        {
            return node != null
                && ((node is MemberDeclarationSyntax));
        }

        /// <summary>
        /// Retrieves 
        /// </summary>
        /// <returns></returns>
        private async Task<(SyntaxNode, Document)> GetCurrentTokenAndDocumentAsync()
        {
            IWpfTextView textView = await GetTextViewAsync();
            SnapshotPoint caretPosition = textView.Caret.Position.BufferPosition;
            Document document = caretPosition.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
            var syntaxRoot = await document.GetSyntaxRootAsync();
            return (syntaxRoot.FindToken(caretPosition).Parent, document);
        }

        /// <summary>
        /// Gets the current editor view
        /// </summary>
        /// <returns></returns>
        private async Task<IWpfTextView> GetTextViewAsync()
        {
            IVsTextManager textManager =
                (IVsTextManager)await ServiceProvider.GetServiceAsync(typeof(SVsTextManager));
            Assumes.Present(textManager);
            textManager.GetActiveView(1, null, out IVsTextView textView);

            var editorAdaptersFactoryService = await GetEditorAdaptersFactoryServiceAsync();

            return editorAdaptersFactoryService.GetWpfTextView(textView);
        }

        /// <summary>
        /// Gets the EditorAdaptersFactoryService
        /// </summary>
        /// <returns></returns>
        private async Task<IVsEditorAdaptersFactoryService> GetEditorAdaptersFactoryServiceAsync()
        {
            IComponentModel componentModel =
                (IComponentModel)await ServiceProvider.GetServiceAsync(typeof(SComponentModel));
            Assumes.Present(componentModel);
            return componentModel.GetService<IVsEditorAdaptersFactoryService>();
        }
    }
}
