using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WeflyUpgradeTool
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var args = e.Args?.ToArray() ?? Array.Empty<string>();
            bool smoke = args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase));
            if (!smoke)
            {
                return;
            }

            // 模拟模式，自动跑一遍升级流程并退出
            try
            {
                Environment.SetEnvironmentVariable("WEFLY_MOCK", "1");
                var win = new MainWindow();
                // 不显示窗口，避免 CI 无交互桌面导致问题
                await Task.Delay(200);
                await win.RunMockUpgradeSmokeTestAsync();
                Logging.Write("SMOKE_OK");
                Current.Shutdown(0);
            }
            catch (Exception ex)
            {
                Logging.Write("SMOKE_FAIL: " + ex.Message);
                Current.Shutdown(1);
            }
        }
    }
}







