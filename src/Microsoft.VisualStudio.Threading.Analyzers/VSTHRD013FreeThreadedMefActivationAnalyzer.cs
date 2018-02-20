namespace Microsoft.VisualStudio.Threading.Analyzers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// Flag usage of objects that must only be invoked while on the main thread (e.g. STA COM objects)
    /// without having first verified that the current thread is main thread either by throwing if on
    /// the wrong thread or asynchronously switching to the main thread.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class VSTHRD013FreeThreadedMefActivationAnalyzer : DiagnosticAnalyzer
    {
        public const string Id = "VSTHRD013";

        internal static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
            id: Id,
            title: Strings.VSTHRD013_Title,
            messageFormat: Strings.VSTHRD013_MessageFormat,
            helpLinkUri: Utils.GetHelpLink(Id),
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> ReusableSupportedDiagnostics = ImmutableArray.Create(Descriptor);

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ReusableSupportedDiagnostics;

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);

            context.RegisterCompilationStartAction(ctxt =>
            {
                var exportV1Attribute = ctxt.Compilation.GetTypeByMetadataName(Types.MEFv1.ExportAttribute.FullTypeName);
                var exportV2Attribute = ctxt.Compilation.GetTypeByMetadataName(Types.MEFv2.ExportAttribute.FullTypeName);
                var importingConstructorV1 = ctxt.Compilation.GetTypeByMetadataName(Types.MEFv1.ImportingConstructorAttribute.FullTypeName);
                var importingConstructorV2 = ctxt.Compilation.GetTypeByMetadataName(Types.MEFv2.ImportingConstructorAttribute.FullTypeName);
                var onImportsSatisfiedAttribute = ctxt.Compilation.GetTypeByMetadataName(Types.MEFv2.OnImportsSatisfiedAttribute.FullTypeName);
                var importsSatisfiedNotificationInterface = ctxt.Compilation.GetTypeByMetadataName(Types.MEFv1.IPartImportsSatisfiedNotification.FullTypeName);

                // Only analyze further if MEFv1 or MEFv2 is referenced.
                if (exportV1Attribute != null || exportV2Attribute != null)
                {
                    ctxt.RegisterSyntaxNodeAction(Utils.DebuggableWrapper(c => this.AnalyzeClass(c, exportV1Attribute, exportV2Attribute, importingConstructorV1, importingConstructorV2, onImportsSatisfiedAttribute, importsSatisfiedNotificationInterface)), SyntaxKind.ClassDeclaration);
                }
            });
        }

        private static INamedTypeSymbol FindExportAttribute(ISymbol symbol, INamedTypeSymbol exportV1Attribute, INamedTypeSymbol exportV2Attribute)
        {
            foreach (var attribute in symbol?.GetAttributes() ?? ImmutableArray<AttributeData>.Empty)
            {
                if (attribute.AttributeClass?.Equals(exportV1Attribute) ?? false)
                {
                    return exportV1Attribute;
                }

                if (attribute.AttributeClass?.Equals(exportV2Attribute) ?? false)
                {
                    return exportV2Attribute;
                }
            }

            return null;
        }

        private static INamedTypeSymbol FindExportAttributeOnTypeOrMembers(INamedTypeSymbol typeSymbol, INamedTypeSymbol exportV1Attribute, INamedTypeSymbol exportV2Attribute)
        {
            var result = FindExportAttribute(typeSymbol, exportV1Attribute, exportV2Attribute);
            if (result != null)
            {
                return result;
            }

            foreach (ISymbol member in typeSymbol.GetMembers())
            {
                result = FindExportAttribute(member, exportV1Attribute, exportV2Attribute);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static IMethodSymbol FindImportingConstructor(INamedTypeSymbol classSymbol, INamedTypeSymbol importingConstructorAttributeSymbol)
        {
            // Prefer constructors that have [ImportingConstructor] applied.
            foreach (IMethodSymbol ctor in classSymbol.InstanceConstructors)
            {
                foreach (var attribute in ctor.GetAttributes())
                {
                    if (attribute.AttributeClass.Equals(importingConstructorAttributeSymbol))
                    {
                        return ctor;
                    }
                }
            }

            // Otherwise find the default constructor.
            foreach (IMethodSymbol ctor in classSymbol.InstanceConstructors)
            {
                if (ctor.Parameters.Length == 0)
                {
                    return ctor;
                }
            }

            // No ImportingConstructor found. Probably not an activatable MEF part.
            return null;
        }

        private static IMethodSymbol FindOnImportsSatisfiedMethodV2(INamedTypeSymbol classSymbol, INamedTypeSymbol onImportsSatisfiedAttribute)
        {
            return null;
        }

        private static IMethodSymbol FindOnImportsSatisfiedMethodV1(INamedTypeSymbol classSymbol, INamedTypeSymbol partsSatisfiedNotificationInterface)
        {
            return null;
        }

        private void AnalyzeClass(SyntaxNodeAnalysisContext context, INamedTypeSymbol exportV1Attribute, INamedTypeSymbol exportV2Attribute, INamedTypeSymbol importingConstructorV1, INamedTypeSymbol importingConstructorV2, INamedTypeSymbol onImportsSatisfiedAttribute, INamedTypeSymbol importsSatisfiedNotificationInterface)
        {
            var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax, context.CancellationToken);
            if (classSymbol == null)
            {
                return;
            }

            INamedTypeSymbol foundExportAttribute = FindExportAttributeOnTypeOrMembers(classSymbol, exportV1Attribute, exportV2Attribute);
            if (foundExportAttribute == null)
            {
                return;
            }

            bool isMEFv1 = foundExportAttribute == exportV1Attribute;
            var importingConstructorAttributeSymbol = isMEFv1 ? importingConstructorV1 : importingConstructorV2;
            IMethodSymbol importingConstructor = FindImportingConstructor(classSymbol, importingConstructorAttributeSymbol);
            if (importingConstructor != null)
            {
                // TODO:
            }

            IMethodSymbol onImportsSatisfiedMethod = isMEFv1
                ? FindOnImportsSatisfiedMethodV1(classSymbol, importsSatisfiedNotificationInterface)
                : FindOnImportsSatisfiedMethodV2(classSymbol, onImportsSatisfiedAttribute);
            if (onImportsSatisfiedMethod != null)
            {
                // TODO
            }

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, context.Node.GetLocation()));
        }
    }
}
