using System;
using System.Windows.Media;
using LAE;

namespace TestRunner
{
    internal static class Program
    {
        [STAThread]
        static int Main()
        {
            try
            {
                return RunAll() ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unhandled exception: " + ex);
                return 2;
            }
        }

        private static bool RunAll()
        {
            bool ok = true;
            ok &= TestMoveBy();
            ok &= TestScale();
            ok &= TestColor();
            Console.WriteLine(ok ? "ALL TESTS PASSED" : "SOME TESTS FAILED");
            return ok;
        }

        private static bool TestMoveBy()
        {
            var t = new TranslateTransform();
            var builder = LA.Builder();
            builder.MoveBy(t, 100, 50, 200);
            var group = builder.BuildGroup();

            group.OnRegistered();

            double elapsed = 0;
            while (!group.IsCompleted && elapsed < 2000)
            {
                group.Update(20);
                elapsed += 20;
            }

            bool pass = group.IsCompleted && Math.Abs(t.X - 100) < 0.5 && Math.Abs(t.Y - 50) < 0.5;
            Console.WriteLine($"TestMoveBy: {(pass ? "PASS" : "FAIL")} -> X={t.X}, Y={t.Y}");
            return pass;
        }

        private static bool TestScale()
        {
            var s = new ScaleTransform(1,1);
            var builder = LA.Builder();
            builder.Scale(s, 2.5, 150);
            var group = builder.BuildGroup();

            group.OnRegistered();

            double elapsed = 0;
            while (!group.IsCompleted && elapsed < 2000)
            {
                group.Update(15);
                elapsed += 15;
            }

            bool pass = group.IsCompleted && Math.Abs(s.ScaleX - 2.5) < 0.05 && Math.Abs(s.ScaleY - 2.5) < 0.05;
            Console.WriteLine($"TestScale: {(pass ? "PASS" : "FAIL")} -> ScaleX={s.ScaleX}, ScaleY={s.ScaleY}");
            return pass;
        }

        private static bool TestColor()
        {
            var brush = new SolidColorBrush(Colors.Blue);
            var builder = LA.Builder();
            builder.Color(brush, Colors.Red, 120);
            var group = builder.BuildGroup();

            group.OnRegistered();

            double elapsed = 0;
            while (!group.IsCompleted && elapsed < 2000)
            {
                group.Update(30);
                elapsed += 30;
            }

            bool pass = group.IsCompleted && brush.Color == Colors.Red;
            Console.WriteLine($"TestColor: {(pass ? "PASS" : "FAIL")} -> Color={brush.Color}");
            return pass;
        }
    }
}
