using System;
using System.Windows;

namespace TextPlus.ContactSearcher.Wpf
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var app = new Application();
            var mWin = new MainWindow();
            return app.Run(mWin);
        }
    }
}
