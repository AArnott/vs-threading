namespace Microsoft.VisualStudio.Threading.Analyzers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using CodeAnalysis.CSharp;
    using CodeAnalysis.CSharp.Syntax;

    internal static class Namespaces
    {
        internal static readonly IReadOnlyList<string> SystemThreadingTasks = new[]
        {
            nameof(System),
            nameof(System.Threading),
            nameof(System.Threading.Tasks),
        };

        internal static readonly IReadOnlyList<string> SystemRuntimeCompilerServices = new[]
        {
            nameof(System),
            nameof(System.Runtime),
            nameof(System.Runtime.CompilerServices),
        };

        internal static readonly IReadOnlyList<string> MicrosoftVisualStudioThreading = new[]
        {
            "Microsoft",
            "VisualStudio",
            "Threading",
        };

        internal static QualifiedNameSyntax MakeTypeSyntax(IReadOnlyList<string> namespaces, SimpleNameSyntax typeName)
        {
            if (namespaces == null)
            {
                throw new ArgumentNullException(nameof(namespaces));
            }

            if (typeName == null)
            {
                throw new ArgumentNullException(nameof(typeName));
            }

            QualifiedNameSyntax result = null;
            for (int i = 0; i < namespaces.Count; i++)
            {
                var left = (NameSyntax)result ?? SyntaxFactory.IdentifierName(namespaces[i]);
                SimpleNameSyntax right = i + 1 < namespaces.Count
                    ? SyntaxFactory.IdentifierName(namespaces[i + 1])
                    : typeName;
                result = SyntaxFactory.QualifiedName(left, right);
            }

            return result;
        }
    }
}
