﻿namespace Microsoft.VisualStudio.Threading.Analyzers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Text;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Threading;

    /// <summary>
    /// Report warnings when detect the usage on Visual Studio services (i.e. IVsSolution) without verified
    /// that the current thread is main thread, or switched to main thread prior invocation explicitly.
    /// </summary>
    /// <remarks>
    /// [Background] Most of Visual Studio services especially the legacy services which are implemented in native code
    /// are living in STA. Invoking such STA services from background thread would do COM marshaling. The calling background
    /// thread will block and wait until the invocation is processed by the STA service on the main thread. It is not only about
    /// inefficiency. Such COM marshaling might lead to dead lock if the method occupying the main thread is also waiting for
    /// that calling background task and the main thread does not allow COM marshaling to reenter the main thread. To avoid potential
    /// dead lock and the expensive COM marshaling, this analyzer would ask the caller of Visual Studio services to verify the
    /// current thread is main thread, or switch to main thread prior invocation explicitly.
    ///
    /// i.e.
    ///     IVsSolution sln = GetIVsSolution();
    ///     sln.SetProperty(); /* This analyzer will report warning on this invocation. */
    ///
    /// i.e.
    ///     ThreadHelper.ThrowIfNotOnUIThread();
    ///     IVsSolution sln = GetIVsSolution();
    ///     sln.SetProperty(); /* Good */
    ///
    /// i.e.
    ///     await joinableTaskFactory.SwitchToMainThreadAsync();
    ///     IVsSolution sln = GetIVsSolution();
    ///     sln.SetProperty(); /* Good */
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class VSTHRD010VsServiceUsageAnalyzer : DiagnosticAnalyzer
    {
        public const string Id = "VSTHRD010";

        internal static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
            id: Id,
            title: Strings.VSTHRD010_Title,
            messageFormat: Strings.VSTHRD010_MessageFormat,
            helpLinkUri: Utils.GetHelpLink(Id),
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private enum ThreadingContext
        {
            /// <summary>
            /// The context is not known, either because it was never asserted or switched to,
            /// or because a branch in the method exists which changed the context conditionally.
            /// </summary>
            Unknown,

            /// <summary>
            /// The context is definitely on the main thread.
            /// </summary>
            MainThread,

            /// <summary>
            /// The context is definitely on a non-UI thread.
            /// </summary>
            NotMainThread,
        }

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(Descriptor);
            }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);

            context.RegisterCompilationStartAction(ctxt =>
            {
                var mainThreadAssertingMethods = CommonInterest.ReadAdditionalFiles(ctxt, CommonInterest.FileNamePatternForMethodsThatAssertMainThread);
                var mainThreadSwitchingMethods = CommonInterest.ReadAdditionalFiles(ctxt, CommonInterest.FileNamePatternForMethodsThatSwitchToMainThread);
                var typesRequiringMainThread = CommonInterest.ReadAdditionalFiles(ctxt, CommonInterest.FileNamePatternForTypesRequiringMainThread);

                ctxt.RegisterCodeBlockStartAction<SyntaxKind>(ctxt2 =>
                {
                    var methodAnalyzer = new MethodAnalyzer
                    {
                        MainThreadAssertingMethods = mainThreadAssertingMethods,
                        MainThreadSwitchingMethods = mainThreadSwitchingMethods,
                        TypesRequiringMainThread = typesRequiringMainThread,
                    };
                    ctxt2.RegisterSyntaxNodeAction(Utils.DebuggableWrapper(methodAnalyzer.AnalyzeInvocation), SyntaxKind.InvocationExpression);
                    ctxt2.RegisterSyntaxNodeAction(Utils.DebuggableWrapper(methodAnalyzer.AnalyzeMemberAccess), SyntaxKind.SimpleMemberAccessExpression);
                    ctxt2.RegisterSyntaxNodeAction(Utils.DebuggableWrapper(methodAnalyzer.AnalyzeCast), SyntaxKind.CastExpression);
                    ctxt2.RegisterSyntaxNodeAction(Utils.DebuggableWrapper(methodAnalyzer.AnalyzeAs), SyntaxKind.AsExpression);
                    ctxt2.RegisterSyntaxNodeAction(Utils.DebuggableWrapper(methodAnalyzer.AnalyzeAs), SyntaxKind.IsExpression);
                });
            });
        }

        private class MethodAnalyzer
        {
            private ImmutableDictionary<SyntaxNode, ThreadingContext> methodDeclarationNodes = ImmutableDictionary<SyntaxNode, ThreadingContext>.Empty;

            internal HashSet<string> MainThreadAssertingMethods { get; set; }

            internal HashSet<string> MainThreadSwitchingMethods { get; set; }

            internal HashSet<string> TypesRequiringMainThread { get; set; }

            internal void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
            {
                var invocationSyntax = (InvocationExpressionSyntax)context.Node;
                var invokeMethod = context.SemanticModel.GetSymbolInfo(context.Node).Symbol as IMethodSymbol;
                if (invokeMethod != null)
                {
                    var methodDeclaration = context.Node.FirstAncestorOrSelf<SyntaxNode>(n => CommonInterest.MethodSyntaxKinds.Contains(n.Kind()));
                    if (methodDeclaration != null)
                    {
                        if (this.MainThreadAssertingMethods.Contains(invokeMethod.Name) || this.MainThreadSwitchingMethods.Contains(invokeMethod.Name))
                        {
                            this.methodDeclarationNodes = this.methodDeclarationNodes.SetItem(methodDeclaration, ThreadingContext.MainThread);
                            return;
                        }
                    }

                    // The diagnostic (if any) should underline the method name only.
                    var focusedNode = invocationSyntax.Expression;
                    focusedNode = (focusedNode as MemberAccessExpressionSyntax)?.Name ?? focusedNode;
                    if (!this.AnalyzeTypeWithinContext(invokeMethod.ContainingType, invokeMethod, context, focusedNode))
                    {
                        foreach (var iface in invokeMethod.FindInterfacesImplemented())
                        {
                            if (this.AnalyzeTypeWithinContext(iface, invokeMethod, context, focusedNode))
                            {
                                // Just report the first diagnostic.
                                break;
                            }
                        }
                    }
                }
            }

            internal void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
            {
                var memberAccessSyntax = (MemberAccessExpressionSyntax)context.Node;
                var property = context.SemanticModel.GetSymbolInfo(context.Node).Symbol as IPropertySymbol;
                if (property != null)
                {
                    this.AnalyzeTypeWithinContext(property.ContainingType, property, context, memberAccessSyntax.Name);
                }
            }

            internal void AnalyzeCast(SyntaxNodeAnalysisContext context)
            {
                var castSyntax = (CastExpressionSyntax)context.Node;
                var type = context.SemanticModel.GetSymbolInfo(castSyntax.Type).Symbol as ITypeSymbol;
                if (type != null)
                {
                    this.AnalyzeTypeWithinContext(type, null, context);
                }
            }

            internal void AnalyzeAs(SyntaxNodeAnalysisContext context)
            {
                var asSyntax = (BinaryExpressionSyntax)context.Node;
                var type = context.SemanticModel.GetSymbolInfo(asSyntax.Right).Symbol as ITypeSymbol;
                if (type != null)
                {
                    this.AnalyzeTypeWithinContext(type, null, context);
                }
            }

            private bool AnalyzeTypeWithinContext(ITypeSymbol type, ISymbol symbol, SyntaxNodeAnalysisContext context, SyntaxNode focusDiagnosticOn = null)
            {
                if (type == null)
                {
                    throw new ArgumentNullException(nameof(type));
                }

                bool requiresUIThread = type.TypeKind == TypeKind.Interface
                    && type.ContainingAssembly != null
                    && this.TypesRequiringMainThread.Any(type.ContainingAssembly.Name.StartsWith);
                requiresUIThread |= symbol?.Name == "GetService" && type.Name == "Package" && type.BelongsToNamespace(Namespaces.MicrosoftVisualStudioShell);
                requiresUIThread |= symbol != null && !symbol.IsStatic && type.Name == "ServiceProvider" && type.BelongsToNamespace(Namespaces.MicrosoftVisualStudioShell);

                if (requiresUIThread)
                {
                    var threadingContext = ThreadingContext.Unknown;
                    var methodDeclaration = context.Node.FirstAncestorOrSelf<SyntaxNode>(n => CommonInterest.MethodSyntaxKinds.Contains(n.Kind()));
                    if (methodDeclaration != null)
                    {
                        threadingContext = this.methodDeclarationNodes.GetValueOrDefault(methodDeclaration);
                    }

                    if (threadingContext != ThreadingContext.MainThread)
                    {
                        Location location = (focusDiagnosticOn ?? context.Node).GetLocation();
                        context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, type.Name));
                        return true;
                    }
                }

                return false;
            }
        }
    }
}