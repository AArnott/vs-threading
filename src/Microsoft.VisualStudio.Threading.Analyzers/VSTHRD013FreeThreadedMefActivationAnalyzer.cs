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

            context.RegisterCompilationStartAction(compilationStartContext =>
            {
                var data = new CompilationData(compilationStartContext);

                // Only analyze further if MEFv1 or MEFv2 is referenced.
                if (data.ExportV1Attribute != null || data.ExportV2Attribute != null)
                {
                    compilationStartContext.RegisterSyntaxNodeAction(Utils.DebuggableWrapper(c => this.AnalyzeClass(c, data)), SyntaxKind.ClassDeclaration);
                }
            });
        }

        private static INamedTypeSymbol FindExportAttribute(ISymbol symbol, CompilationData compilationData)
        {
            foreach (var attribute in symbol?.GetAttributes() ?? ImmutableArray<AttributeData>.Empty)
            {
                if (attribute.AttributeClass?.Equals(compilationData.ExportV1Attribute) ?? false)
                {
                    return compilationData.ExportV1Attribute;
                }

                if (attribute.AttributeClass?.Equals(compilationData.ExportV2Attribute) ?? false)
                {
                    return compilationData.ExportV2Attribute;
                }
            }

            return null;
        }

        private static INamedTypeSymbol FindExportAttributeOnTypeOrMembers(INamedTypeSymbol typeSymbol, CompilationData compilationData)
        {
            var result = FindExportAttribute(typeSymbol, compilationData);
            if (result != null)
            {
                return result;
            }

            foreach (ISymbol member in typeSymbol.GetMembers())
            {
                result = FindExportAttribute(member, compilationData);
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

        private void AnalyzeClass(SyntaxNodeAnalysisContext context, CompilationData compilationData)
        {
            var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax, context.CancellationToken);
            if (classSymbol == null)
            {
                return;
            }

            INamedTypeSymbol foundExportAttribute = FindExportAttributeOnTypeOrMembers(classSymbol, compilationData);
            if (foundExportAttribute == null)
            {
                return;
            }

            bool isMEFv1 = foundExportAttribute == compilationData.ExportV1Attribute;
            var importingConstructorAttributeSymbol = isMEFv1 ? compilationData.ImportingConstructorV1 : compilationData.ImportingConstructorV2;
            IMethodSymbol importingConstructor = FindImportingConstructor(classSymbol, importingConstructorAttributeSymbol);
            this.ReportThreadAffinity(context, importingConstructor, compilationData);

            IMethodSymbol onImportsSatisfiedMethod = isMEFv1
                ? FindOnImportsSatisfiedMethodV1(classSymbol, compilationData.ImportsSatisfiedNotificationInterface)
                : FindOnImportsSatisfiedMethodV2(classSymbol, compilationData.OnImportsSatisfiedAttribute);
            this.ReportThreadAffinity(context, onImportsSatisfiedMethod, compilationData);
        }

        private void ReportThreadAffinity(SyntaxNodeAnalysisContext context, IMethodSymbol methodSymbol, CompilationData compilationData)
        {
            var methodSyntax = methodSymbol?.DeclaringSyntaxReferences.FirstOrDefault();
            if (methodSyntax == null)
            {
                return;
            }

            

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, context.Node.GetLocation()));
        }

        private class CompilationData
        {
#pragma warning disable RS1012 // Start action has no registered actions.
            internal CompilationData(CompilationStartAnalysisContext context)
#pragma warning restore RS1012 // Start action has no registered actions.
            {
                this.ExportV1Attribute = context.Compilation.GetTypeByMetadataName(Types.MEFv1.ExportAttribute.FullTypeName);
                this.ExportV2Attribute = context.Compilation.GetTypeByMetadataName(Types.MEFv2.ExportAttribute.FullTypeName);
                this.ImportingConstructorV1 = context.Compilation.GetTypeByMetadataName(Types.MEFv1.ImportingConstructorAttribute.FullTypeName);
                this.ImportingConstructorV2 = context.Compilation.GetTypeByMetadataName(Types.MEFv2.ImportingConstructorAttribute.FullTypeName);
                this.OnImportsSatisfiedAttribute = context.Compilation.GetTypeByMetadataName(Types.MEFv2.OnImportsSatisfiedAttribute.FullTypeName);
                this.ImportsSatisfiedNotificationInterface = context.Compilation.GetTypeByMetadataName(Types.MEFv1.IPartImportsSatisfiedNotification.FullTypeName);
                this.MainThreadAssertingMethods = CommonInterest.ReadMethods(context, CommonInterest.FileNamePatternForMethodsThatAssertMainThread).ToImmutableArray();
                this.TypesRequiringMainThread = CommonInterest.ReadTypes(context, CommonInterest.FileNamePatternForTypesRequiringMainThread).ToImmutableArray();
            }

            public INamedTypeSymbol ExportV1Attribute { get; }

            public INamedTypeSymbol ExportV2Attribute { get; }

            public INamedTypeSymbol ImportingConstructorV1 { get; }

            public INamedTypeSymbol ImportingConstructorV2 { get; }

            public INamedTypeSymbol OnImportsSatisfiedAttribute { get; }

            public INamedTypeSymbol ImportsSatisfiedNotificationInterface { get; }

            public ImmutableArray<CommonInterest.QualifiedMember> MainThreadAssertingMethods { get; }

            public ImmutableArray<CommonInterest.QualifiedType> TypesRequiringMainThread { get; }
        }
    }
}
