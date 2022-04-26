using System;
using System.IO;
using System.Threading.Tasks;
using c0ded0c.Core;
using c0ded0c.Core.Hashing;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace c0ded0c.MsBuild
{
    public class RoslynAssemblyProcessingMiddleware : IAssemblyProcessingMiddleware
    {
        private readonly IPathCalculatorProvider pathCalculatorProvider;
        private readonly IHashCalculatorProvider hashCalculatorProvider;
        private readonly IIdentificationBuilder keyBuilder;
        private readonly ILogger<IProjectProcessingEngine> logger;

        public RoslynAssemblyProcessingMiddleware(
            IPathCalculatorProvider pathCalculatorProvider,
            IHashCalculatorProvider hashCalculatorProvider,
            IIdentificationBuilder keyBuilder,
            ILogger<IProjectProcessingEngine> logger)
        {
            this.pathCalculatorProvider = pathCalculatorProvider ?? throw new ArgumentNullException(nameof(pathCalculatorProvider));
            this.hashCalculatorProvider = hashCalculatorProvider ?? throw new ArgumentNullException(nameof(hashCalculatorProvider));
            this.keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
            this.logger = logger;
        }

        public async Task<IAssemblyInfo> ProcessAsync(IAssemblyInfo assembly, AssemblyProcessingAsyncDelegate next)
        {
            return await next(await ProcessAsync(assembly));
        }

        private async Task<IAssemblyInfo> ProcessAsync(IAssemblyInfo assembly)
        {
            AssemblyInfo familiar = AssemblyInfo.From(assembly);

            if (familiar == null)
            {
                return assembly;
            }

            Project? project = familiar.GetMutableTag<Project>();

            if (project == null || string.IsNullOrEmpty(project.FilePath))
            {
                return assembly;
            }

            var assemblyBuilder = familiar.ToBuilder();
            INamespaceManager namespaceManager = new NamespaceManager(pathCalculatorProvider, keyBuilder);

            foreach (Document document in project.Documents)
            {
                var documentBuilder = document.BuildInfo(familiar.Key, pathCalculatorProvider, keyBuilder).ToBuilder();
                documentBuilder.Checksum = CalculateDocumentChecksum(document);

                RoslynDocumentInfo? roslynDocumentInfo = await BuildRoslynDocument(document);
                documentBuilder.MutableTag = roslynDocumentInfo;

                if (roslynDocumentInfo != null)
                {
                    var walker = new TypesAndMembersExtractorWalker(roslynDocumentInfo, documentBuilder, assemblyBuilder, pathCalculatorProvider, keyBuilder, namespaceManager);
                    walker.Visit(roslynDocumentInfo.RootSyntaxNode);
                }

                assemblyBuilder.Documents[documentBuilder.Key.FullName] = documentBuilder.ToImmutable();
            }

            assemblyBuilder.Namespaces.AddRange(namespaceManager.GetNamespaceMap());
            return assemblyBuilder.ToImmutable();
        }

        private string CalculateDocumentChecksum(Document document)
        {
            IHashCalculator calculator = hashCalculatorProvider.GetCalculator();
            byte[] hashData;

            using (Stream file = File.OpenRead(document.FilePath))
            {
                hashData = calculator.ComputeHash(file);
            }

            return calculator.ToStringRepresentation(hashData);
        }

        private async Task<RoslynDocumentInfo?> BuildRoslynDocument(Document document)
        {
            Task<SyntaxNode?> syntaxTask = document.GetSyntaxRootAsync();
            Task<SemanticModel?> semanticTask = document.GetSemanticModelAsync();

            await Task.WhenAll(syntaxTask, semanticTask);

            if (syntaxTask.Result == null)
            {
                logger.LogError("No syntax tree for document: {document}", document.Name);
                return null;
            }

            if (semanticTask.Result == null)
            {
                logger.LogError("No semantic model for document: {document}", document.Name);
                return null;
            }

            return new RoslynDocumentInfo(
                document,
                syntaxTask.Result,
                semanticTask.Result);
        }
    }
}
