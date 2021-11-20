using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using Document = Microsoft.CodeAnalysis.Document;
using Project = Microsoft.CodeAnalysis.Project;
using Solution = Microsoft.CodeAnalysis.Solution;
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
        public static readonly Guid s_commandSet = new Guid("222993a5-b14f-4206-9ba3-00cf383c7d99");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage _package;

        /// <summary>
        /// Initializes a new instance of the <see cref="FindHandlerCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private FindHandlerCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(s_commandSet, CommandId);
            var menuItem = new OleMenuCommand(Execute, menuCommandID);
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
                    (SyntaxNode node, Document document) = await GetCurrentTokenAndDocumentAsync();
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
        private IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return _package;
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
            string requestedCommandOrRequest = string.Empty;
            (string fileName, int lineToGoTo) = (string.Empty, 0);

            ThreadHelper.ThrowIfNotOnUIThread();

            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                (SyntaxNode node, Document document) = await GetCurrentTokenAndDocumentAsync();

                if (IsSyntaxNodeSupported(node))
                {
                    requestedCommandOrRequest = GetIdentifierNameByNode(node);
                }
            });

            if (string.IsNullOrWhiteSpace(requestedCommandOrRequest))
            {
                return;
            }

            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (requestedCommandOrRequest.ToLowerInvariant().EndsWith("request"))
                {
                    (fileName, lineToGoTo) =
                        await FindRequestHandlerAsync(requestedCommandOrRequest);
                }
                else if (requestedCommandOrRequest.ToLowerInvariant().EndsWith("command"))
                {
                    (fileName, lineToGoTo) =
                        await FindCommandHandlerAsync(requestedCommandOrRequest);
                }
            });

            if (!string.IsNullOrEmpty(fileName))
            {
                var dte = Package.GetGlobalService(typeof(_DTE)) as DTE2;
                dte.ExecuteCommand("File.OpenFile", fileName);

                if (lineToGoTo > 0)
                {
                    ((TextSelection)dte.ActiveDocument.Selection).GotoLine(lineToGoTo + 1);
                }
            }
        }

        private string GetIdentifierNameByNode(SyntaxNode node)
        {
            string name = string.Empty;

            switch (node)
            {
                case RecordDeclarationSyntax recordDeclarationSyntax:
                    name = recordDeclarationSyntax.Identifier.Text;
                    break;
                case IdentifierNameSyntax identifierNameSyntax:
                    name = identifierNameSyntax.Identifier.Text;
                    break;
                case ClassDeclarationSyntax classDeclarationSyntax:
                    name = classDeclarationSyntax.Identifier.Text;
                    break;
            }

            if (name != "var")
            {
                return name;
            }

            return string.Empty;
        }

        private async Task<Tuple<string, int>> FindCommandHandlerAsync(string requestedCommandOrRequest)
        {
            string fileNameToOpen = string.Empty;
            int lineToGoTo = 0;

            var componentModel = (IComponentModel)await ServiceProvider.GetServiceAsync(typeof(SComponentModel));

            if (componentModel == null)
            {
                return new Tuple<string, int>(string.Empty, 0);
            }

            VisualStudioWorkspace workspace = componentModel.GetService<VisualStudioWorkspace>();

            Solution solution = workspace.CurrentSolution;

            Project domainProject =
                solution.Projects.SingleOrDefault(y => y.Name.ToLowerInvariant().EndsWith("domain"));

            IEnumerable<GenericNameSyntax> types =
                domainProject
                .Documents
                .Where(x => x.FilePath.ToLowerInvariant().Contains("commandhandlers"))
                .Select(doc =>
                    new
                    {
                        Model = doc.GetSemanticModelAsync().Result,
                        Declarations = doc.GetSyntaxRootAsync().Result
                            .DescendantNodes().OfType<GenericNameSyntax>()
                    }).Where(x => x.Declarations.Any())
                    .SelectMany(x => x.Declarations)
                    .Where(x => x.Identifier.Text == "IRequestHandler");

            foreach (GenericNameSyntax type in types)
            {
                foreach (TypeSyntax typeArgument in type.TypeArgumentList.Arguments)
                {
                    var typeArgumentIdentifierSyntax = (IdentifierNameSyntax)typeArgument;
                    if (typeArgumentIdentifierSyntax.Identifier.Text == requestedCommandOrRequest)
                    {
                        fileNameToOpen = type.SyntaxTree.FilePath;

                        Document document = domainProject.Documents.Single(x => x.FilePath == fileNameToOpen);

                        SyntaxTree syntaxTree = await document.GetSyntaxTreeAsync();
                        SyntaxNode semantics = await document.GetSyntaxRootAsync();

                        IEnumerable<MethodDeclarationSyntax> declaredMethods = semantics.DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>();

                        foreach (MethodDeclarationSyntax method in declaredMethods)
                        {
                            if (((IdentifierNameSyntax)(method.ParameterList.Parameters.First()).Type).Identifier.Text == requestedCommandOrRequest)
                            {
                                lineToGoTo = syntaxTree.GetLineSpan(method.Span).StartLinePosition.Line;
                                break;
                            }
                        }

                        break;
                    }
                }

                if (!string.IsNullOrEmpty(fileNameToOpen))
                {
                    break;
                }
            }

            return new Tuple<string, int>(fileNameToOpen, lineToGoTo);
        }

        private async Task<Tuple<string, int>> FindRequestHandlerAsync(string requestedCommandOrRequest)
        {
            string fileNameToOpen = string.Empty;
            int lineToGoTo = 0;

            var componentModel = (IComponentModel)await ServiceProvider.GetServiceAsync(typeof(SComponentModel));

            if (componentModel == null)
            {
                return new Tuple<string, int>(string.Empty, 0);
            }

            VisualStudioWorkspace workspace = componentModel.GetService<VisualStudioWorkspace>();

            Solution solution = workspace.CurrentSolution;

            Project applicationProject =
                solution.Projects.SingleOrDefault(y => y.Name.ToLowerInvariant().EndsWith("application"));

            IEnumerable<GenericNameSyntax> types =
                applicationProject
                .Documents
                .Where(x => x.FilePath.ToLowerInvariant().Contains("requesthandlers"))
                .Select(doc =>
                    new
                    {
                        Model = doc.GetSemanticModelAsync().Result,
                        Declarations = doc.GetSyntaxRootAsync().Result
                            .DescendantNodes().OfType<GenericNameSyntax>()
                    }).Where(x => x.Declarations.Any())
                    .SelectMany(x => x.Declarations)
                    .Where(x => x.Identifier.Text == "IRequestHandler");

            foreach (GenericNameSyntax type in types)
            {
                foreach (TypeSyntax typeArgument in type.TypeArgumentList.Arguments)
                {
                    if (typeArgument is GenericNameSyntax)
                    {
                        continue;
                    }

                    var typeArgumentIdentifierSyntax = (IdentifierNameSyntax)typeArgument;
                    if (typeArgumentIdentifierSyntax.Identifier.Text == requestedCommandOrRequest)
                    {
                        fileNameToOpen = type.SyntaxTree.FilePath;

                        Document document = applicationProject.Documents.Single(x => x.FilePath == fileNameToOpen);

                        SyntaxTree syntaxTree = await document.GetSyntaxTreeAsync();
                        SyntaxNode semantics = await document.GetSyntaxRootAsync();

                        IEnumerable<MethodDeclarationSyntax> declaredMethods =
                            semantics.DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>();

                        foreach (MethodDeclarationSyntax method in declaredMethods)
                        {
                            if (((IdentifierNameSyntax)(method.ParameterList.Parameters.First()).Type).Identifier.Text == requestedCommandOrRequest)
                            {
                                lineToGoTo = syntaxTree.GetLineSpan(method.Span).StartLinePosition.Line;
                                break;
                            }
                        }

                        break;
                    }
                }

                if (!string.IsNullOrEmpty(fileNameToOpen))
                {
                    break;
                }
            }

            return new Tuple<string, int>(fileNameToOpen, lineToGoTo);
        }

        /// <summary>
        /// Checks if the syntax node type is supported
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private bool IsSyntaxNodeSupported(SyntaxNode node)
        {
            bool isSupported = node != null
                && ((node is IdentifierNameSyntax) ||
                    (node is RecordDeclarationSyntax) ||
                    (node is ClassDeclarationSyntax));

            isSupported = isSupported && GetIdentifierNameByNode(node) != string.Empty;

            return isSupported;
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
            SyntaxNode syntaxRoot = await document.GetSyntaxRootAsync();
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

            IVsEditorAdaptersFactoryService editorAdaptersFactoryService = await GetEditorAdaptersFactoryServiceAsync();

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
