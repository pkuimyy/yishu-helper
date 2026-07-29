using YiShuHelper.TrayApp;

ApplicationConfiguration.Initialize();
using var mutex = new Mutex(true, @"Local\YiShuHelper.Tray", out var firstInstance);
if (!firstInstance)
{
    MessageBox.Show("翼枢分流助手托盘已经在运行。", "翼枢分流助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

Application.Run(new TrayApplicationContext());
