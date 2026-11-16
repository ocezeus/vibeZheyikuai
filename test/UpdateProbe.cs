using System;
using WorkCountdown;

// 发布后实测：UpdateService.Check() 能不能从 GitHub Release 拿到最新版（「检查更新」用的就是它）
class UpdateProbe
{
    [STAThread]
    static void Main()
    {
        Console.WriteLine("当前版本 AppInfo.Version = " + AppInfo.Version);
        try
        {
            var r = UpdateService.Check();
            if (r == null) { Console.WriteLine("Check() 返回 null（网络 / API 失败）"); return; }
            Console.WriteLine("最新版本 = " + r.version + "   tag = " + r.tag);
            Console.WriteLine("release = " + (r.name ?? "-"));
            Console.WriteLine("asset = " + (r.assetName ?? "-") + "   size = " + r.assetSize);
            Console.WriteLine("assetUrl = " + (r.assetUrl ?? "-"));
            Console.WriteLine("是否比当前新 = " + (UpdateService.IsNewer(r.version, AppInfo.Version) ? "是" : "否（已经是最新）"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("检查更新异常: " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}