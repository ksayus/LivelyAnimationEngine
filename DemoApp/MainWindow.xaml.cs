using System;
using System.Windows;
using System.Windows.Media;
using LAE;

namespace DemoApp
{
    public partial class MainWindow : Window
    {
        private readonly SolidColorBrush _brush;

        public MainWindow()
        {
            InitializeComponent();
            _brush = (SolidColorBrush)rect.Fill;
        }

        private void BtnMove_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder().MoveBy(translateTransform, 150, 0, 600).Play();
        }

        private void BtnMoveTo_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder().Move(translateTransform, 200, 100, 700).Play();
        }

        private void BtnScale_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder().Scale(scaleTransform, 2.0, 400).Play();
        }

        private void BtnRotate_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder().RotateBy(rotateTransform, 180, 700).Play();
        }

        private void BtnColor_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder().Color(_brush, Colors.Red, 500).Play();
        }

        private void BtnDelay_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder().Delay(800).MoveBy(translateTransform, 120, 0, 500).Play();
        }

        private void BtnWait_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder().Wait(1000).OnComplete(() => MessageBox.Show("Wait finished")).Play();
        }

        private void BtnCallback_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder().Callback(() => MessageBox.Show("Callback executed")).Play();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            translateTransform.X = 0;
            translateTransform.Y = 0;
            scaleTransform.ScaleX = 1;
            scaleTransform.ScaleY = 1;
            rotateTransform.Angle = 0;
            _brush.Color = Colors.Blue;
        }

        private void BtnSeq_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder()
              .Scale(scaleTransform, 1.5, 300)
              .Then()
              .MoveBy(translateTransform, -100, 50, 500)
              .Then()
              .RotateBy(rotateTransform, 90, 400)
              .OnComplete(() => MessageBox.Show("Sequence complete"))
              .Play();
        }

        private void BtnPar_Click(object sender, RoutedEventArgs e)
        {
            LA.Builder()
              .Scale(scaleTransform, 1.2, 400)
              .MoveBy(translateTransform, 80, 20, 400)
              .RotateBy(rotateTransform, 45, 400)
              .Color(_brush, Colors.Green, 400)
              .Play();
        }

        private void BtnFreeze_Click(object sender, RoutedEventArgs e)
        {
            LAEngine.Freeze();
        }

        private void BtnUnfreeze_Click(object sender, RoutedEventArgs e)
        {
            LAEngine.Unfreeze();
        }

        private void SliderSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            LAEngine.Speed = sliderSpeed.Value;
        }
    }
}
