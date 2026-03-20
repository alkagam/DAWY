using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DawEngine.UI
{
    public partial class AddTrackModal : Window
    {
        public string TrackType { get; private set; } = "Audio";

        public AddTrackModal()
        {
            InitializeComponent();
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string tag)
            {
                TrackType    = tag;
                DialogResult = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
