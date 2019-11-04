using System.Windows;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Helper Utils { get; } = new Helper();

        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
