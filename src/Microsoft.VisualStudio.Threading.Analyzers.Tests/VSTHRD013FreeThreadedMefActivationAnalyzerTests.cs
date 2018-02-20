namespace Microsoft.VisualStudio.Threading.Analyzers.Tests
{
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Xunit;
    using Xunit.Abstractions;

    public class VSTHRD013FreeThreadedMefActivationAnalyzerTests : DiagnosticVerifier
    {
        private DiagnosticResult expect = new DiagnosticResult
        {
            Id = VSTHRD013FreeThreadedMefActivationAnalyzer.Id,
            SkipVerifyMessage = true,
            Severity = DiagnosticSeverity.Warning,
        };

        public VSTHRD013FreeThreadedMefActivationAnalyzerTests(ITestOutputHelper logger)
            : base(logger)
        {
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer() => new VSTHRD013FreeThreadedMefActivationAnalyzer();

        [Fact]
        public void NoExplicitConstructor_ProducesNoDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;

[Export]
class G {
}
";
            this.VerifyCSharpDiagnostic(test);
        }

        [Fact]
        public void DefaultConstructor_ProducesNoDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;

[Export]
class G {
    public G() {
    }
}
";
            this.VerifyCSharpDiagnostic(test);
        }

        [Fact]
        public void MainThreadAssertingMethod_ProducesDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;

[Export]
class G {
    public G() {
        ThreadHelper.ThrowIfNotOnUIThread();
    }
}
";
            this.expect.Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 9, 9, 44) };
            this.VerifyCSharpDiagnostic(test, this.expect);
        }

        [Fact]
        public void MainThreadRequiringType_ProducesDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

[Export]
class G {
    public G() {
        IVsShell shell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
    }
}
";
            this.expect.Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 9, 9, 44) };
            this.VerifyCSharpDiagnostic(test, this.expect);
        }

        [Fact]
        public void MainThreadRequiringType_ExportedMethod_ProducesDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

class G {
    public G() {
        IVsShell shell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
    }

    [Export]
    public void ExportedMethod() { }
}
";
            this.expect.Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 9, 9, 44) };
            this.VerifyCSharpDiagnostic(test, this.expect);
        }

        [Fact]
        public void MainThreadRequiringType_InheritedExport_ProducesDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

[InheritedExport]
interface IFoo { }

class G : IFoo {
    public G() {
        IVsShell shell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
    }
}
";
            this.expect.Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 9, 9, 44) };
            this.VerifyCSharpDiagnostic(test, this.expect);
        }

        [Fact]
        public void MainThreadRequiringType_NoExport_ProducesNoDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

interface IFoo { }

class G : IFoo {
    public G() {
        IVsShell shell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
    }
}
";
            this.VerifyCSharpDiagnostic(test);
        }

        [Fact]
        public void MainThreadRequiringType_ExplicitImportingConstructor_ProducesDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

[Export]
class G {
    public G() {
    }

    [ImportingConstructor]
    public G(IServiceProvider sp) {
        IVsShell shell = sp.GetService(typeof(SVsShell)) as IVsShell;
    }
}
";
            this.expect.Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 9, 9, 44) };
            this.VerifyCSharpDiagnostic(test, this.expect);
        }

        [Fact]
        public void MainThreadRequiringType_OnImportsSatisfiedV1_ProducesDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

[Export]
class G : IPartImportsSatisfiedNotification {
    private readonly IServiceProvider sp;

    [ImportingConstructor]
    public G(IServiceProvider sp) {
        this.sp = sp;
    }

    public void OnImportsSatisfied() {
        IVsShell shell = this.sp.GetService(typeof(SVsShell)) as IVsShell;
    }
}
";
            this.expect.Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 9, 9, 44) };
            this.VerifyCSharpDiagnostic(test, this.expect);
        }

        [Fact]
        public void MainThreadRequiringType_OnImportsSatisfiedV1_ExplicitInterfaceImplementation_ProducesDiagnostic()
        {
            var test = @"
using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

[Export]
class G : IPartImportsSatisfiedNotification {
    private readonly IServiceProvider sp;

    [ImportingConstructor]
    public G(IServiceProvider sp) {
        this.sp = sp;
    }

    void IPartImportsSatisfiedNotification.OnImportsSatisfied() {
        IVsShell shell = this.sp.GetService(typeof(SVsShell)) as IVsShell;
    }
}
";
            this.expect.Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 9, 9, 44) };
            this.VerifyCSharpDiagnostic(test, this.expect);
        }

        [Fact]
        public void MainThreadRequiringType_OnImportsSatisfiedV2_ProducesDiagnostic()
        {
            var test = @"
using System;
using System.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

[Export]
class G {
    private readonly IServiceProvider sp;

    [ImportingConstructor]
    public G(IServiceProvider sp) {
        this.sp = sp;
    }

    [OnImportsSatisfied]
    public void LaterOn() {
        IVsShell shell = this.sp.GetService(typeof(SVsShell)) as IVsShell;
    }
}
";
            this.expect.Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 9, 9, 44) };
            this.VerifyCSharpDiagnostic(test, this.expect);
        }
    }
}
