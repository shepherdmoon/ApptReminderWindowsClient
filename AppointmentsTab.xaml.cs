using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Dynamic;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Xceed.Wpf.Toolkit;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for AppointmentsTab.xaml
    /// </summary>
    public partial class AppointmentsTab : UserControl
    {
        public string DateFormatString { get; } = Helper.DateStringFormat;
        public ObservableCollection<Appointment> Appointments { get; } = new ObservableCollection<Appointment>();
        private dynamic mapping;
        public AppointmentsTab()
        {
            InitializeComponent();
        }

        public Helper Utils
        {
            get => (Helper)GetValue(UtilsProperty);
            set => SetValue(UtilsProperty, value);
        }
        public static readonly DependencyProperty UtilsProperty = DependencyProperty.Register("Utils", typeof(Helper), typeof(AppointmentsTab), new UIPropertyMetadata(null));

        public Window Owner
        {
            get => (Window)GetValue(OwnerProperty);
            set => SetValue(OwnerProperty, value);
        }
        public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register("Owner", typeof(Window), typeof(AppointmentsTab), new UIPropertyMetadata(null));

        private void DateFormat_ValueChanged(object _, TextChangedEventArgs _1)
        {
            if (Date.Text == null || Date.Text == "")
            {
                DateFormatPreview.Content = "";
                return;
            }
            DateTime date = DateTime.ParseExact(Date.Text, DateFormatString, CultureInfo.CurrentCulture);
            try
            {
                DateFormatPreview.Content = date.ToString(DateFormat.Text, CultureInfo.CurrentCulture);
            }
            catch (Exception e)
            {
                DateFormatPreview.Content = e.Message;
            }
        }

        private void SetAppointment(string id, string date, string name = "", string format = "", string lang = "", string email = "", string text = "", dynamic map = null, List<dynamic> reminders = null)
        {
            Id.Text = id;
            Date.Text = date;
            DisplayName.Value = name;
            DateFormat.Text = format;
            Lang.Value = lang;
            Email.Value = email;
            Text.Value = text;
            mapping = map ?? new ExpandoObject();
            List<dynamic> reminderData = new List<dynamic>();
            if (reminders != null)
            {
                foreach (dynamic reminder in reminders)
                {
                    dynamic data = new ExpandoObject();
                    data.Date = DateTime.Parse(reminder.date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToString(DateFormatString, CultureInfo.CurrentCulture);
                    data.Template = ((IDictionary<string, object>)reminder).ContainsKey("template") ? reminder.template : "";
                    data.MainContact = reminder.contactTypes.Count > 0 ? reminder.contactTypes[0][0] : "";
                    data.SecondaryContact = reminder.contactTypes.Count > 0 && reminder.contactTypes[0].Count > 1 ? reminder.contactTypes[0][1] : "";
                    reminderData.Add(data);
                }
            }
            Reminders.ItemsSource = reminderData;
            SaveButton.ResetSuccess();
            SaveButton.ResetFailure();
            DeleteButton.ResetSuccess();
            DeleteButton.ResetFailure();
        }

        private bool isNew = false;
        private void NewAppointment_Click(object _, RoutedEventArgs _1)
        {
            AddAppointmentDialog dialog = new AddAppointmentDialog { Owner = Owner };
            bool? result = dialog.ShowDialog();
            if (result != true) return;
            SetAppointment(dialog.Id.Text, dialog.Date.Text);
            AppointmentsTable.SelectedIndex = -1;
            isNew = true;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = ResponseMessage;
            dynamic parameters = new ExpandoObject();
            parameters.id = Id.Text;
            DateTime utc = DateTime.ParseExact(Date.Text, DateFormatString, CultureInfo.CurrentCulture).ToUniversalTime();
            parameters.date = $"{utc.ToString("s", CultureInfo.InvariantCulture)}Z";
            if (Lang.Value != "") parameters.language = Lang.Value;
            if (DateFormat.Text != "") mapping.date = (string)DateFormatPreview.Content;
            parameters.mapping = mapping;
            parameters.contact = new ExpandoObject();
            if (Email.Value != "") parameters.contact.Email = new string[] { Email.Value };
            if (Text.Value != "") parameters.contact.Text = new string[] { Text.Value };
            parameters.generateDefaults = isNew;
            button.SetApiCalls(Utils.CallApiEndpoint(HttpMethod.Put, "/appointment", parameters));
            button.Callback = (_1) =>
            {
                isNew = false;
                var command = new SQLiteCommand($"REPLACE INTO {Helper.AppointmentTable} (id, name, date, date_format) VALUES(@id, @name, @date, @date_format)");
                command.Parameters.AddWithValue("@id", Id.Text);
                command.Parameters.AddWithValue("@name", DisplayName.Value);
                command.Parameters.AddWithValue("@date", new DateTimeOffset(utc).ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("@date_format", DateFormat.Text);
                Utils.RunQuery(command);
                DeleteButton.ResetSuccess();
                DeleteButton.ResetFailure();
            };
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = ResponseMessage;
            DateTime utc = DateTime.ParseExact(Date.Text, DateFormatString, CultureInfo.CurrentCulture).ToUniversalTime();
            dynamic parameters = new
            {
                id = Id.Text,
                date = $"{utc.ToString("s", CultureInfo.InvariantCulture)}Z"
            };
            button.SetApiCalls(Utils.CallApiEndpoint(HttpMethod.Delete, "/appointment", parameters));
            button.Callback = (_1) =>
            {
                var command = new SQLiteCommand($"DELETE FROM {Helper.AppointmentTable} WHERE id = @id AND date = @date");
                command.Parameters.AddWithValue("@id", Id.Text);
                command.Parameters.AddWithValue("@date", new DateTimeOffset(utc).ToUnixTimeMilliseconds());
                Utils.RunQuery(command);
                if (AppointmentsTable.SelectedIndex != -1) Appointments.RemoveAt(AppointmentsTable.SelectedIndex);
                SetAppointment("", "");
            };
        }

        private void SearchButton_Click(object _, RoutedEventArgs _1)
        {
            Appointments.Clear();
            long date = 0;
            if (SearchDate.Text != null && SearchDate.Text != "")
            {
                DateTime utc = DateTime.ParseExact(SearchDate.Text, DateFormatString, CultureInfo.CurrentCulture).ToUniversalTime();
                date = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
            }
            string idString = "";
            if (SearchId.Text != "")
            {
                idString = $" AND (id LIKE @id OR name LIKE @id)";
            }
            var command = new SQLiteCommand($"SELECT * from {Helper.AppointmentTable} WHERE date >= {date}{idString} ORDER BY date LIMIT 100");
            if (SearchId.Text != "")
            {
                command.Parameters.AddWithValue("@id", $"{SearchId.Text}%");
            }
            var result = Utils.RunQuery(command);
            if (!(result is List<Dictionary<string, object>>)) return;
            foreach (var data in (List<Dictionary<string, object>>)result)
            {
                Appointment appointment = new Appointment((string)data["id"], (string)data["name"], (long)data["date"], (string)data["date_format"]);
                Appointments.Add(appointment);
            }
        }

        private async void AppointmentsTable_SelectionChanged(object _, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            isNew = false;
            Appointment appointment = (Appointment)e.AddedItems[0];
            DateTime utc = DateTime.ParseExact(appointment.Date, DateFormatString, CultureInfo.CurrentCulture).ToUniversalTime();
            var queryParams = new Dictionary<string, object>() { { "id", appointment.Id }, { "date", $"{utc.ToString("s", CultureInfo.InvariantCulture)}Z" } };
            dynamic response = await Utils.CallApiEndpoint(HttpMethod.Get, "/appointment", null, queryParams);
            if (response is string)
            {
                ResponseMessage.Text = response;
            }
            else
            {
                if (!((IDictionary<string, object>)response).ContainsKey("id"))
                {
                    SetAppointment(appointment.Id, appointment.Date, appointment.Name, appointment.GetDateFormat());
                    ResponseMessage.Text = "Appointment was not found on the server.";
                    return;
                }
                string lang = ((IDictionary<string, object>)response).ContainsKey("language") ? response.language : "";
                string email = ((IDictionary<string, object>)response.contact).ContainsKey("Email") ? response.contact.Email[0] : "";
                string text = ((IDictionary<string, object>)response.contact).ContainsKey("Text") ? response.contact.Text[0] : "";
                SetAppointment(appointment.Id, appointment.Date, appointment.Name, appointment.GetDateFormat(), lang, email, text, response.mapping, (List<dynamic>)response.reminders);
            }
        }
    }
}
