using System.Globalization;
using System.Windows;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for AddAppointmentDialog.xaml
    /// </summary>
    public partial class AddAppointmentDialog : Window
    {
        public AddAppointmentDialog()
        {
            InitializeComponent();
            Date.CultureInfo = CultureInfo.CurrentCulture;
        }

        public string DateStringFormat { get; } = Helper.DateStringFormat;

        private void Grid_IsVisibleChanged(object _, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            Id.Focus();
        }

        private void Save_Click(object _, RoutedEventArgs _1)
        {
            if (Id.Text == "" || Date.Text == null || Date.Text == "") return;
            DialogResult = true;
        }
    }
}
