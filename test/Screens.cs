using System;
using System.Windows.Forms;
class Screens {
  [STAThread] static void Main() {
    foreach (Screen s in Screen.AllScreens)
      Console.WriteLine(s.DeviceName + " bounds=" + s.Bounds + " work=" + s.WorkingArea + " primary=" + s.Primary);
  }
}