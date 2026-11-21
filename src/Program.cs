using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace control_center;

static class Program
{
    private const string MutexName = @"Local\AI_Translator_ControlCenter_SingleInstance_Mutex";
    private const int HWND_BROADCAST = 0xffff;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public const int SW_RESTORE = 9;
    public static readonly uint WM_SHOW_AI_TRANSLATOR = RegisterWindowMessage("AI_TRANSLATOR_ACTIVATE_INSTANCE_MSG");

    public static void Log(string msg)
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logPath = Path.Combine(baseDir, "app.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    [STAThread]
    static void Main(string[] args)
    {
        Log($"=== Main started with args: [{string.Join(", ", args)}] ===");

        bool isOnlyInstance;
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(true, MutexName, out isOnlyInstance);
        }
        catch (AbandonedMutexException)
        {
            isOnlyInstance = true;
        }

        if (!isOnlyInstance)
        {
            Log("Another instance is already running. Signaling existing instance and exiting.");
            try
            {
                var current = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id != current.Id)
                    {
                        uint targetPid = (uint)p.Id;
                        EnumWindows((hWnd, lParam) =>
                        {
                            GetWindowThreadProcessId(hWnd, out uint windowPid);
                            if (windowPid == targetPid)
                            {
                                PostMessage(hWnd, WM_SHOW_AI_TRANSLATOR, IntPtr.Zero, IntPtr.Zero);
                            }
                            return true;
                        }, IntPtr.Zero);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"EnumWindows exception: {ex.Message}");
            }

            PostMessage((IntPtr)HWND_BROADCAST, WM_SHOW_AI_TRANSLATOR, IntPtr.Zero, IntPtr.Zero);
            mutex?.Dispose();
            return;
        }

        using (mutex)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log($"UnhandledException: {e.ExceptionObject}");
            };

            Application.ThreadException += (s, e) =>
            {
                Log($"ThreadException: {e.Exception}");
            };

            Application.ApplicationExit += (s, e) =>
            {
                Log("Application.ApplicationExit fired!");
            };

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            try
            {
                ApplicationConfiguration.Initialize();
                Log("ApplicationConfiguration.Initialize done");
                var form = new Form1();
                Log("Form1 instantiated");
                Application.Run(form);
                Log("Application.Run returned");
            }
            catch (Exception ex)
            {
                Log($"Exception in Main: {ex}");
            }
            Log("=== Main exiting ===");
        }
    }    
}
