using System;
using System.Windows;

namespace TextPlus.ContactDataRetriever.Wpf
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] _) => new Application().Run(new MainWindow());
    }
}
