using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace textplus.chatagent.wpf
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] _argv)
        {
//#if DEBUG
//            var main = new MainWindow();
//            var app = new App();
//            app.Run(main);
//#else
            var main = new MainWindow();
            var g = new glueauth(main, "txnca", Assembly.GetExecutingAssembly().GetName().Version!.ToString(), Array.Empty<string>());
            var app = new App();
            app.Run(g);
//#endif
        }
    }
}
