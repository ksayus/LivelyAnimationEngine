using System;
using System.Windows;

namespace DemoApp
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            var app = new Application();
            var wnd = new MainWindow();
            app.Run(wnd);
        }
    }
}
