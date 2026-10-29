using System.Threading;
using System.Windows;
using DashKey.Core;

namespace DashKey;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private Controller? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：Windows 上比文件锁简单可靠
        _instanceMutex = new Mutex(true, @"Local\DashKey.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Log.Info("已有实例在运行，本次启动退出");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("未处理异常", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("AppDomain 未处理异常", args.ExceptionObject as Exception);

        try
        {
            _controller = new Controller();
            _controller.Start();
        }
        catch (Exception ex)
        {
            Log.Error("启动失败", ex);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();

        if (_instanceMutex is not null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch { /* 未持有则忽略 */ }
            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
