using System;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;
class VisualCheck {
 static BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Set(object o,string name,object value){o.GetType().GetField(name,F).SetValue(o,value);}
 static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,F).Invoke(o,args);}
 [STAThread] static void Main(){
        // 探针只读配置，绝不回写用户自己的设置
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
  string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"visual");Directory.CreateDirectory(dir);
  int cases=0;
  foreach(float dpi in new float[]{96,144,192})
  foreach(string effect in new string[]{"none","glow","particles","aurora"})
  foreach(bool collapsed in new bool[]{false,true}){
   Settings s=new Settings();s.lark.enabled=false;s.calendarEnabled=false;s.ui.edge="free";s.ui.autoCollapseSeconds=0;s.ui.reminderEffect=effect;s.ui.theme="midnight";
   using(var form=new WidgetForm(new SettingsStore(),s)){
    Set(form,"scale",dpi/96f);Set(form,"collapsed",collapsed);
    form.MinimumSize=Size.Empty;form.Size=new Size((int)((collapsed?224:280)*dpi/96),collapsed?(int)(68*dpi/96):(int)form.GetType().GetProperty("ExpandedH",F).GetValue(form,null));
    Call(form,"Recompute");
    int width=form.Width,height=form.Height;
    foreach(bool meeting in new bool[]{false,true}){
     Set(form,"remindEvent",new ReminderEvent { title="产品需求评审 · 下一阶段排期", detail="明确负责人、验收时间及联调计划\n换行不影响布局", place="12F · 南侧会议室 A", start=Reminders.FormatStart(DateTime.Now.AddMinutes(5)), end=Reminders.FormatStart(DateTime.Now.AddMinutes(35)), source=meeting?"lark":"local" });
     Set(form,"reminding",true);
     int[] moments={0,200,420,600,800,1200,4200,7100,7400,7760,7850,7950};
     foreach(int moment in moments){
      Set(form,"remindAt",DateTime.Now.AddMilliseconds(-moment));
      using(var bmp=new Bitmap(width,height)){
       bmp.SetResolution(dpi,dpi);
       using(var g=Graphics.FromImage(bmp)) Call(form,"OnPaint",new PaintEventArgs(g,new Rectangle(0,0,width,height)));
       if(dpi==144 && !collapsed && (effect=="glow" || effect=="particles" || effect=="aurora")) bmp.Save(Path.Combine(dir,effect+"-"+(meeting?"meeting":"event")+"-"+moment+".png"));
      }cases++;
     }
    }
    Set(form,"reminding",false);
    using(var bmp=new Bitmap(width,height)) {bmp.SetResolution(dpi,dpi);using(var g=Graphics.FromImage(bmp)) Call(form,"OnPaint",new PaintEventArgs(g,new Rectangle(0,0,width,height)));if(dpi==144 && effect=="glow") bmp.Save(Path.Combine(dir,collapsed?"compact.png":"expanded.png"));}
    s.ui.reducedMotion=true;Set(form,"reminding",true);Set(form,"remindAt",DateTime.Now.AddSeconds(-2));
    using(var bmp=new Bitmap(width,height)) {bmp.SetResolution(dpi,dpi);using(var g=Graphics.FromImage(bmp)) Call(form,"OnPaint",new PaintEventArgs(g,new Rectangle(0,0,width,height)));}cases++;
    Call(form,"OnFormClosing",new FormClosingEventArgs(CloseReason.None,false));
   }
  }
  var demo=new Settings();demo.lark.enabled=false;demo.calendarEnabled=false;demo.ui.edge="free";demo.ui.reminderEffect="particles";
  using(var form=new WidgetForm(new SettingsStore(),demo)){
   Set(form,"scale",1.5f);Set(form,"collapsed",false);form.MinimumSize=Size.Empty;form.Size=new Size(420,(int)form.GetType().GetProperty("ExpandedH",F).GetValue(form,null));Call(form,"Recompute");
   Set(form,"remindEvent",new ReminderEvent { title="产品设计评审", detail="确认交互方案与本周排期", place="12F · 会议室 A", start=Reminders.FormatStart(DateTime.Now.AddMinutes(5)), end=Reminders.FormatStart(DateTime.Now.AddMinutes(35)), source="lark" });
   for(int moment=0;moment<9000;moment+=50){Set(form,"reminding",moment<7960);Set(form,"remindAt",DateTime.Now.AddMilliseconds(-moment));using(var bmp=new Bitmap(420,273)){bmp.SetResolution(144,144);using(var g=Graphics.FromImage(bmp))Call(form,"OnPaint",new PaintEventArgs(g,new Rectangle(0,0,420,273)));bmp.Save(Path.Combine(dir,"demo-"+moment.ToString("D5")+".png"));}}
   Call(form,"OnFormClosing",new FormClosingEventArgs(CloseReason.None,false));
  }
  var settings=new Settings();settings.lark.enabled=false;settings.calendarEnabled=false;
  using(var dialog=new SettingsForm(new SettingsStore(),settings)){
   dialog.CreateControl();
   var tabs=(TabControl)typeof(SettingsForm).GetField("tabs",F).GetValue(dialog);
   dialog.Show();Application.DoEvents();
   string[] names={"policy","fixed","schedule","appearance"};
   for(int ti=0;ti<4;ti++){
     tabs.SelectedIndex=ti;Application.DoEvents();
     using(var bmp=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(dir,"tab-"+names[ti]+".png"));}
   }
   dialog.Close();
  }
  Console.WriteLine("PASS: "+cases+" rendering cases at 100/150/200%; layered alpha, text metrics, quiet mode and transition boundaries.");
 }
}
