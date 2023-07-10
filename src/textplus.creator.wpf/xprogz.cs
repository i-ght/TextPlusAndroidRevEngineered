using System;
using System.Windows;

namespace TextPlus.Creator.Wpf
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] _)
        {
            var app = new Application();
            var mWin = new MainWindow();
            return app.Run(mWin);
        }
    }
}
