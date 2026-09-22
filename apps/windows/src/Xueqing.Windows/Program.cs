using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Xueqing.Windows;

internal static class Program
{
    private const string MainInstanceKey = "xueqing-main";
    private static readonly object ActivationGate = new();
    private static readonly Queue<AppActivationArguments> PendingActivations = new();

    private static App? _app;
    private static DispatcherQueue? _dispatcherQueue;
    private static AppInstance? _registeredInstance;

    [STAThread]
    public static async Task Main(string[] args)
    {
        _ = args;
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var current = AppInstance.GetCurrent();
        var activation = current.GetActivatedEventArgs();

        var instance = AppInstance.FindOrRegisterForKey(MainInstanceKey);

        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(activation);
            return;
        }

        _registeredInstance = instance;
        instance.Activated += OnActivated;

        Application.Start(_ =>
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcherQueue));

            var app = new App();
            AppActivationArguments[] pending;

            lock (ActivationGate)
            {
                _dispatcherQueue = dispatcherQueue;
                _app = app;
                pending = PendingActivations.ToArray();
                PendingActivations.Clear();
            }

            // A custom entry point owns the initial rich activation, but Window
            // creation must happen after Application.Start has returned control
            // to the WinUI dispatcher. Queue the initial activation first, then
            // any redirect that arrived while the XAML Application was starting.
            if (!dispatcherQueue.TryEnqueue(() =>
            {
                app.HandleInitialActivation(activation);

                foreach (var redirected in pending)
                {
                    app.HandleRedirectedActivation(redirected);
                }
            }))
            {
                throw new InvalidOperationException(
                    "WinUI dispatcher rejected initial application activation.");
            }
        });
    }

    private static void OnActivated(
        object? sender,
        AppActivationArguments activation)
    {
        _ = sender;

        DispatcherQueue? dispatcherQueue;
        App? app;

        lock (ActivationGate)
        {
            dispatcherQueue = _dispatcherQueue;
            app = _app;

            if (dispatcherQueue is null || app is null)
            {
                PendingActivations.Enqueue(activation);
                return;
            }
        }

        _ = dispatcherQueue.TryEnqueue(
            () => app.HandleRedirectedActivation(activation));
    }
}
