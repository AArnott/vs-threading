# VSTHRD013 Free-threaded MEF activation

Constructing MEF parts should avoid any requirements to run on a specific thread.
MEF parts are activated lazily on whatever thread their importer may be on at the time,
and deadlocks can occur when a MEF part that requires the main thread to activate
is actually activated on a different thread.

MEF part importing constructors should be simple and free-threaded, leaving any
thread-affinitized initialization requirements to their API for the importer to invoke
outside of the MEF activation sequence.

MEF parts may also implement `IPartImportsSatisfiedNotification` or apply
[OnImportsSatisfiedAttribute] to one of its methods to be invoked when all its imports
have been satisfied. This invocation is also part of the MEF activation sequence and
should similarly be free-threaded.

## Examples of patterns that are flagged by this analyzer

This example demonstrates a violating importing constructor:

```csharp
[Export(typeof(IFoo))]
public class Foo : IFoo
{
    private int cookie;

    [ImportingConstructor]
    public Foo(SVsServiceProvider serviceProvider)
    {
        var solution = serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
        Assumes.Present(solution);
        ErrorHandler.ThrowOnFailure(solution.AdviseSolutionEvents(this, out this.cookie));
    }
}
```

This example demonstrates a violating imports satisfied method:

```csharp
[Export(typeof(IFoo))]
public class Foo : IFoo, IPartImportsSatisfiedNotification
{
    private readonly SVsServiceProvider serviceProvider;
    private readonly JoinableTaskContext joinableTaskContext;
    private int cookie;

    [ImportingConstructor]
    public Foo(SVsServiceProvider serviceProvider, JoinableTaskContext joinableTaskContext)
    {
        this.serviceProvider = serviceProvider;
        this.joinableTaskContext = joinableTaskContext;
    }

    void IPartImportsSatisfiedNotification.OnImportsSatisfied()
    {
        var solution = serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
        Assumes.Present(solution);
        ErrorHandler.ThrowOnFailure(solution.AdviseSolutionEvents(this, out this.cookie));
    }
}
```

## Solution

Move thread-affinitized code execution to a method that the client can call rather than MEF activation,
as demonstrated below:

```csharp
[Export(typeof(IFoo))]
public class Foo : IFoo
{
    private readonly SVsServiceProvider serviceProvider;
    private readonly JoinableTaskContext joinableTaskContext;
    private int cookie;

    [ImportingConstructor]
    public Foo(SVsServiceProvider serviceProvider, JoinableTaskContext joinableTaskContext)
    {
        this.serviceProvider = serviceProvider;
        this.joinableTaskContext = joinableTaskContext;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await this.joinableTaskContext.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (this.cookie == 0)
        {
            var solution = serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
            Assumes.Present(solution);
            ErrorHandler.ThrowOnFailure(solution.AdviseSolutionEvents(this, out this.cookie));
        }
    }
}
```
