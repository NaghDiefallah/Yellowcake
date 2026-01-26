using Avalonia.Controls;
using Yellowcake.Services;

namespace Yellowcake
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            NotificationService.Instance.Initialize(this);
        }
    }
}