using System;
using System.Windows.Forms;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.Compatible
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
            {
                LogCrash(e.Exception);
                MessageBox.Show("UI错误: " + e.Exception.GetType().Name + "\n" + e.Exception.Message + "\n" + e.Exception.StackTrace, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                if (ex != null) LogCrash(ex);
                MessageBox.Show("致命错误: " + (ex != null ? ex.GetType().Name + "\n" + ex.Message + "\n" + ex.StackTrace : e.ExceptionObject.ToString()), "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                ToolIconService.CleanExpiredCache();
            }
            catch { }

            Application.Run(new MainForm());
        }

        private static void LogCrash(Exception ex)
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "tubacompat_crash.log");
                System.IO.File.AppendAllText(logPath,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " +
                    ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace + "\n\n");
            }
            catch { }
        }
    }
}
