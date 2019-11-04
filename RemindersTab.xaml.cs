using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for RemindersTab.xaml
    /// </summary>
    public partial class RemindersTab : UserControl
    {
        public RemindersTab()
        {
            InitializeComponent();
            MinHoursSelector.ItemsSource = Enumerable.Range(0, 24);
            MinHoursSelector.SelectedIndex = 0;
        }

        public Helper Utils
        {
            get => (Helper)GetValue(UtilsProperty);
            set => SetValue(UtilsProperty, value);
        }
        public static readonly DependencyProperty UtilsProperty = DependencyProperty.Register("Utils", typeof(Helper), typeof(RemindersTab), new UIPropertyMetadata(null));

        public ObservableCollection<Reminder> Reminders { get; } = new ObservableCollection<Reminder>();
        private readonly List<KeyValuePair<string, string>> templateNames = new List<KeyValuePair<string, string>>();
        private async void Reminders_Visibility_Listener(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            List<UIElement> elements = Helper.GetAllElements((Grid)sender, new Type[] { typeof(ApiCallButton), typeof(Button), typeof(ComboBox) });
            elements.ForEach(element => element.IsEnabled = false);

            // get the existing reminders
            dynamic response = await Utils.CallApiEndpoint(HttpMethod.Get, "/org/reminders/default", null, new Dictionary<string, object>() { { "type", "null" } });
            if (response is string)
            {
                ReminderResponseMessage.Text = response;
            }
            else
            {
                dynamic templateResponse = await Utils.CallApiEndpoint(HttpMethod.Get, "/template", null, new Dictionary<string, object>() { { "metaOnly", "true" } });
                if (templateResponse is string)
                {
                    ReminderResponseMessage.Text = templateResponse;
                }
                else
                {
                    // generate the template names
                    templateNames.Clear();
                    Dictionary<string, HashSet<string>> names = new Dictionary<string, HashSet<string>>();
                    foreach (dynamic template in templateResponse)
                    {
                        string name = template.name ?? Helper.DefaultTemplateName;
                        if (names.ContainsKey(name)) names[name].Add((string)template.type);
                        else names[name] = new HashSet<string> { (string)template.type };
                    }
                    foreach (string name in names.Keys)
                    {
                        var displayName = name;
                        var result = Utils.RunQuery(new SQLiteCommand($"SELECT value FROM {Helper.TemplateNameTable} WHERE name='{name}'"));
                        if (result is List<Dictionary<string, object>> rows && rows.Count == 1) displayName = (string)rows[0]["value"];
                        List<string> types = names[name].ToList();
                        types.Sort();
                        templateNames.Add(new KeyValuePair<string, string>(name, $"{displayName} ({string.Join(",", types)})"));
                    }
                    templateNames.Sort((p1, p2) =>
                    {
                        if (p1.Key == Helper.DefaultTemplateName) return -1;
                        if (p2.Key == Helper.DefaultTemplateName) return 1;
                        return p1.Value.CompareTo(p2.Value);
                    });

                    // generate the reminder objects
                    Reminders.Clear();
                    MinHoursSelector.SelectedValue = (int)response.minHoursBeforeApt;
                    foreach (dynamic data in response.reminders)
                    {
                        Reminders.Add(new Reminder(data, templateNames));
                    }
                    AddReminderButton.IsEnabled = Reminders.Count < 9;
                }
            }
            elements.ForEach(element => element.IsEnabled = true);
        }

        private void AddReminder_Click(object _, RoutedEventArgs _1)
        {
            if (Reminders.Count >= 9)
            {
                AddReminderButton.IsEnabled = false;
                return;
            }
            Reminders.Add(new Reminder(templateNames));
            RemindersContainer.ScrollToEnd();
        }

        private void RemoveReminder_Click(object sender, RoutedEventArgs _1)
        {
            Button button = (Button)sender;
            string id = (string)button.Tag;
            Reminders.Remove(Reminders.Where(reminder => reminder.Id == id).First());
            if (Reminders.Count < 9)
            {
                AddReminderButton.IsEnabled = true;
            }
        }

        private void SaveReminders_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = ReminderResponseMessage;
            var parameters = new
            {
                minHoursBeforeApt = MinHoursSelector.SelectedValue,
                reminders = Reminders.Select(reminder => reminder.Convert())
            };
            button.SetApiCalls(Utils.CallApiEndpoint(HttpMethod.Put, "/org/reminders/default", parameters));
        }
    }
}
