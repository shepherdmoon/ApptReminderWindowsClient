using Flurl;
using Flurl.Http;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AsyncRetryPolicy apiPolicy;
        private readonly string SettingsTable = "Settings";
        private readonly string TemplateNameTable = "TemplateNames";
        private readonly string ApiUrlType = "BaseUrl";
        private readonly string ApiKeyType = "ApiKey";
        private readonly string TimeZoneType = "TimeZone";
        // use | as the default because that is not a valid name so it will never conflict with a user selected name
        private readonly string DefaultTemplateName = "|";
        private readonly string AddNew = "Add New";

        public MainWindow()
        {
            InitializeComponent();

            // retry all throttling and server error api responses up to 4 times
            var random = new Random();
            bool shouldRetryCall(FlurlHttpException ex)
            {
                int status = (int)ex.Call.Response.StatusCode;
                if (status == 429) return true;
                if (status >= 500 && status < 600) return true;
                return false;
            };
            apiPolicy = Policy
                .Handle<FlurlHttpException>(shouldRetryCall)
                .WaitAndRetryAsync(4, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)).Add(TimeSpan.FromMilliseconds(random.Next(1, 999))));

            // initialize the database
            RunQuery(new SQLiteCommand($"CREATE TABLE IF NOT EXISTS {SettingsTable} (type TEXT NOT NULL PRIMARY KEY, value TEXT)"));
            RunQuery(new SQLiteCommand($"CREATE TABLE IF NOT EXISTS {TemplateNameTable} (name TEXT NOT NULL PRIMARY KEY, value TEXT)"));
            RunQuery(new SQLiteCommand($"INSERT OR IGNORE INTO {TemplateNameTable} (name, value) VALUES ('{DefaultTemplateName}', 'Default')"));

            // initialize any static sources
            SelectedTimezone.ItemsSource = TimeZoneInfo.GetSystemTimeZones().Where(timezone =>
            {
                // don't allow users to select timezones that have a base utc or dst offset which is not an integer number of hours or have a fixed start/end date
                if (timezone.BaseUtcOffset.TotalHours != (int)timezone.BaseUtcOffset.TotalHours) return false;
                var current = GetCurrentRule(timezone);
                if (current == null) return true;
                if (current.DaylightTransitionStart.IsFixedDateRule) return false;
                if (current.DaylightTransitionEnd.IsFixedDateRule) return false;
                if (current.DaylightDelta.TotalHours != (int)current.DaylightDelta.TotalHours) return false;
                return true;
            });
        }

        private dynamic RunQuery(SQLiteCommand command)
        {
            try
            {
                List<Dictionary<string, object>> results = new List<Dictionary<string, object>>();
                using (SQLiteConnection conn = new SQLiteConnection(@"Data Source=local.db;"))
                {
                    conn.Open();
                    command.Connection = conn;
                    SQLiteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        Dictionary<string, object> row = new Dictionary<string, object>();
                        for(int index = 0; index < reader.FieldCount; ++index)
                        {
                            row[reader.GetName(index)] = reader.GetValue(index);
                        }
                        results.Add(row);
                    }
                    reader.Close();
                }
                return results;
            }
            catch (Exception e)
            {
                return e.Message;
            }
            finally
            {
                command.Dispose();
            }
        }

        private Task<dynamic> AsyncRunQuery(Func<SQLiteCommand> generateCommand)
        {
            SQLiteCommand command = generateCommand();
            return Task.Run(() => RunQuery(command));
        }

        private string GetBaseUrl()
        {
            var result = RunQuery(new SQLiteCommand($"SELECT value FROM {SettingsTable} WHERE type='{ApiUrlType}'"));
            if (result is List<Dictionary<string, object>> rows && rows.Count == 1) return (string)rows[0]["value"];
            return null;
        }

        private string GetApiKey()
        {
            var result = RunQuery(new SQLiteCommand($"SELECT value FROM {SettingsTable} WHERE type='{ApiKeyType}'"));
            if (result is List<Dictionary<string, object>> rows && rows.Count == 1) return (string)rows[0]["value"];
            return null;
        }

        private async Task<dynamic> CallApiEndpoint(HttpMethod method, string endpoint, object parameters = null, string baseUrl = null, string apiKey = null)
        {
            if (baseUrl == null) baseUrl = GetBaseUrl();
            if (apiKey == null) apiKey = GetApiKey();
            if (baseUrl == null || apiKey == null) return "Missing url and/or api key";
            try
            {
                return await apiPolicy.ExecuteAsync(
                    delegate()
                    {
                        var url = baseUrl.WithHeader("x-api-key", apiKey).AppendPathSegment(endpoint);
                        if (method == HttpMethod.Get) return url.GetJsonAsync();
                        return url.SendJsonAsync(method, parameters).ReceiveJson();
                    }
                );
            }
            catch (FlurlParsingException ex) when (ex.Call.Response.IsSuccessStatusCode)
            {
                // this is to handle responses that are an array
                return await ex.GetResponseJsonAsync<dynamic[]>();
            }
            catch (FlurlHttpException ex)
            {
                return ex.Call.Exception.Message;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        private TimeZoneInfo.AdjustmentRule GetCurrentRule(TimeZoneInfo timezone)
        {
            if (timezone == null || !timezone.SupportsDaylightSavingTime) return null;
            var rules = timezone.GetAdjustmentRules();
            for (int i = rules.Length - 1; i >= 0; --i)
            {
                if (rules[i].DateStart > DateTime.Now) continue;
                return rules[i];
            }
            return null;
        }

        private List<UIElement> GetAllElements(Panel container, Type[] types)
        {
            List<UIElement> elements = new List<UIElement>();
            foreach (UIElement control in container.Children)
            {
                if (control.GetType().IsSubclassOf(typeof(Panel))) elements.AddRange(GetAllElements((Panel)control, types));
                if (types.Contains(control.GetType())) elements.Add(control);
            }
            return elements;
        }

        /* START TEMPLATES TAB FUNCTIONS */
        private readonly Dictionary<string, List<dynamic>> templates = new Dictionary<string, List<dynamic>>();
        private void UpdateTemplateNameSelectorItems()
        {
            List<KeyValuePair<string, string>> templateNames = new List<KeyValuePair<string, string>>();
            foreach (string name in templates.Keys)
            {
                string value = name;
                var result = RunQuery(new SQLiteCommand($"SELECT value FROM {TemplateNameTable} WHERE name='{name}'"));
                if (result is List<Dictionary<string, object>> rows && rows.Count == 1) value = (string)rows[0]["value"];
                templateNames.Add(new KeyValuePair<string, string>(name, value));
            }
            if (templateNames.Count == 0)
            {
                string value = RunQuery(new SQLiteCommand($"SELECT value FROM {TemplateNameTable} WHERE name='{DefaultTemplateName}'"))[0]["value"];
                templateNames.Add(new KeyValuePair<string, string>(DefaultTemplateName, value));
            }
            templateNames.Sort((p1, p2) =>
            {
                if (p1.Key == DefaultTemplateName) return -1;
                if (p2.Key == DefaultTemplateName) return 1;
                return p1.Value.CompareTo(p2.Value);
            });
            templateNames.Add(new KeyValuePair<string, string>(null, AddNew));
            TemplateNameSelector.ItemsSource = templateNames;
        }

        private void UpdateTemplateLanguageSelectorItems()
        {
            HashSet<string> languages = new HashSet<string>();
            foreach (dynamic data in templates[(string)TemplateNameSelector.SelectedValue])
            {
                if (data.language != null) languages.Add((string)data.language);
            }
            List<KeyValuePair<string, string>> templateLanguages = new List<KeyValuePair<string, string>>();
            foreach (string language in languages)
            {
                templateLanguages.Add(new KeyValuePair<string, string>(language, language));
            }
            templateLanguages.Sort((p1, p2) => p1.Value.CompareTo(p2.Value));
            templateLanguages.Insert(0, new KeyValuePair<string, string>(DefaultTemplateName, ""));
            templateLanguages.Add(new KeyValuePair<string, string>(null, AddNew));
            TemplateLanguageSelector.ItemsSource = templateLanguages;
        }

        private async void Templates_Visibility_Listener(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            List<UIElement> elements = GetAllElements((Grid)sender, new Type[] { typeof(ApiCallButton), typeof(ComboBox) });
            elements.ForEach(element => element.IsEnabled = false);

            // get the existing templates
            templates.Clear();
            dynamic response = await CallApiEndpoint(HttpMethod.Get, "/template");
            if (response is string)
            {
                TemplateResponseMessage.Text = response;
            }
            else
            {
                // sort all the templates by name
                foreach (dynamic template in (dynamic[])response)
                {
                    string name = template.name ?? DefaultTemplateName;
                    if (!templates.ContainsKey(name)) templates.Add(name, new List<dynamic>());
                    templates[name].Add(template);
                }

                // update the template combo box
                string curTemplateName = (string)TemplateNameSelector.SelectedValue;
                UpdateTemplateNameSelectorItems();
                if (curTemplateName == null || !templates.ContainsKey(curTemplateName)) TemplateNameSelector.SelectedIndex = 0;
                else TemplateNameSelector.SelectedValue = curTemplateName;
            }
            elements.ForEach(element => element.IsEnabled = true);
        }

        private void TemplateNameSelector_SelectionChanged(object _, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            string name = (string)TemplateNameSelector.SelectedValue;
            if (name == null)
            {
                AddItemDialog dialog = new AddItemDialog
                {
                    Owner = this,
                    Validation = value =>
                    {
                        if (value == null || value == "") return false;
                        if (value.Length >= 256) return false;
                        if (value.IndexOf("|") != -1) return false;
                        if (templates.ContainsKey(value)) return false;
                        return true;
                    }
                };
                dialog.ShowDialog();
                if (dialog.Value == null)
                {
                    TemplateNameSelector.SelectedItem = e.RemovedItems[0];
                }
                else
                {
                    List<dynamic> values = new List<dynamic>
                    {
                        new { type = "Email", title = "", message = "", subtype = (string)null, language = (string)null },
                        new { type = "Text", message = "", subtype = (string)null, language = (string)null }
                    };
                    templates.Add(dialog.Value, values);
                    UpdateTemplateNameSelectorItems();
                    TemplateNameSelector.SelectedValue = dialog.Value;
                }
                return;
            }
            TextMessage.Value = "";
            EmailTitle.Value = "";
            EmailBody.Value = "";
            foreach (dynamic data in templates[name])
            {
                if (data.language != null) continue;
                if (data.type == "Text")
                {
                    TextMessage.Value = data.message ?? "";
                }
                if (data.type == "Email")
                {
                    EmailTitle.Value = data.title ?? "";
                    EmailBody.Value = data.message ?? "";
                }
            }
            string curTemplateLang = (string)TemplateLanguageSelector.SelectedValue;
            UpdateTemplateLanguageSelectorItems();
            if (curTemplateLang == null || templates[name].FindIndex(data => data.language == curTemplateLang) == -1) TemplateLanguageSelector.SelectedIndex = 0;
            else TemplateLanguageSelector.SelectedValue = curTemplateLang;
        }

        private void TemplateLanguageSelector_SelectionChanged(object _, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            string language = (string)TemplateLanguageSelector.SelectedValue;
            if (language == null)
            {
                AddItemDialog dialog = new AddItemDialog
                {
                    Owner = this,
                    Validation = value =>
                    {
                        if (value == null || value == "") return false;
                        if (value.Length >= 128) return false;
                        if (value.IndexOf("|") != -1) return false;
                        if (templates[(string)TemplateNameSelector.SelectedValue].FindIndex(data => data.language == value) != -1) return false;
                        return true;
                    }
                };
                dialog.ShowDialog();
                if (dialog.Value == null)
                {
                    TemplateLanguageSelector.SelectedItem = e.RemovedItems[0];
                }
                else
                {
                    List<dynamic> values = templates[(string)TemplateNameSelector.SelectedValue];
                    values.AddRange(new dynamic[] {
                        new { type = "Email", title = "", message = "", subtype = (string)null, language = dialog.Value },
                        new { type = "Text", message = "", subtype = (string)null, language = dialog.Value }
                    });
                    UpdateTemplateLanguageSelectorItems();
                    TemplateLanguageSelector.SelectedValue = dialog.Value;
                }
                return;
            }
            TextMessageLang.IsEnabled = language != DefaultTemplateName;
            TextMessageLang.Value = "";
            EmailTitleLang.IsEnabled = language != DefaultTemplateName;
            EmailTitleLang.Value = "";
            EmailBodyLang.IsEnabled = language != DefaultTemplateName;
            EmailBodyLang.Value = "";
            foreach (dynamic data in templates[(string)TemplateNameSelector.SelectedValue])
            {
                if (data.language != language) continue;
                if (data.type == "Text")
                {
                    TextMessageLang.Value = data.message ?? "";
                }
                if (data.type == "Email")
                {
                    EmailTitleLang.Value = data.title ?? "";
                    EmailBodyLang.Value = data.message ?? "";
                }
            }
        }

        private string FormatEmailBody(string text)
        {
            if (text == null) text = "";
            Regex re = new Regex(@"^\s*<html>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            if (!re.IsMatch(text)) text = $"<html>\n\t<head></head>\n\t<body>\n\t\t{text}\n\t</body>\n</html>";
            return text;
        }

        private dynamic GenerateParams(string type, string name = null, string language = null, string subtype = null, string title = null, string message = null)
        {
            dynamic value = new ExpandoObject();
            value.type = type;
            if (name != null && name != "") value.name = name;
            if (language != null && language != "") value.language = language;
            if (subtype != null && subtype != "") value.subtype = subtype;
            if (title != null && title != "") value.title = title;
            if (message != null && message != "") value.message = message;
            return value;
        }

        private void SaveTemplates_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = TemplateResponseMessage;
            List<Task<dynamic>> apiCalls = new List<Task<dynamic>>();
            string name = (string)TemplateNameSelector.SelectedValue;
            if (name == DefaultTemplateName) name = null;
            if (TextMessage.Value == "") apiCalls.Add(CallApiEndpoint(HttpMethod.Delete, "/template", GenerateParams("Text", name)));
            else apiCalls.Add(CallApiEndpoint(HttpMethod.Put, "/template", GenerateParams("Text", name, message: TextMessage.Value)));
            if (EmailTitle.Value == "" && EmailBody.Value == "") apiCalls.Add(CallApiEndpoint(HttpMethod.Delete, "/template", GenerateParams("Email", name)));
            else apiCalls.Add(CallApiEndpoint(HttpMethod.Put, "/template", GenerateParams("Email", name, title: EmailTitle.Value, message: FormatEmailBody(EmailBody.Value))));
            if (TemplateLanguageSelector.SelectedIndex != 0)
            {
                string language = (string)TemplateLanguageSelector.SelectedValue;
                if (TextMessageLang.Value == "") apiCalls.Add(CallApiEndpoint(HttpMethod.Delete, "/template", GenerateParams("Text", name, language)));
                else apiCalls.Add(CallApiEndpoint(HttpMethod.Put, "/template", GenerateParams("Text", name, language, message: TextMessageLang.Value)));
                if (EmailTitleLang.Value == "" && EmailBodyLang.Value == "") apiCalls.Add(CallApiEndpoint(HttpMethod.Delete, "/template", GenerateParams("Email", name, language)));
                else apiCalls.Add(CallApiEndpoint(HttpMethod.Put, "/template", GenerateParams("Email", name, language, title: EmailTitleLang.Value, message: FormatEmailBody(EmailBodyLang.Value))));
            }
            button.SetApiCalls(apiCalls);
        }
        /* END TEMPLATES TAB FUNCTIONS */

        /* START SCHEDULES TAB FUNCTIONS */
        private readonly string DST = "DST";
        private readonly string REG = "Regular";
        private readonly HashSet<string> scheduleMonths = new HashSet<string>();
        private async void Schedules_Visibility_Listener(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            List<UIElement> elements = GetAllElements((Grid)sender, new Type[] { typeof(ApiCallButton), typeof(ScheduleInput), typeof(ComboBox) });
            elements.ForEach(element => element.IsEnabled = false);

            // get the saved timezone, default to the computer's selected timezone
            var timezoneResponse = RunQuery(new SQLiteCommand($"SELECT value FROM {SettingsTable} WHERE type='{TimeZoneType}'"));
            if (timezoneResponse is List<Dictionary<string, object>> rows && rows.Count == 1) SelectedTimezone.SelectedItem = TimeZoneInfo.FindSystemTimeZoneById((string)rows[0]["value"]);
            else SelectedTimezone.SelectedItem = TimeZoneInfo.Local;

            // get the existing schedule
            scheduleMonths.Clear();
            dynamic response = await CallApiEndpoint(HttpMethod.Get, "/org/reminders/schedule");
            if (response is string)
            {
                TimezoneResponseMessage.Text = response;
            }
            else
            {
                dynamic[] schedules = response;
                dynamic schedule = schedules.Length == 0 ? null : schedules[0].dates[0].schedule;
                TimeZoneInfo timezone = (TimeZoneInfo)SelectedTimezone.SelectedItem;
                bool isDst = timezone.IsDaylightSavingTime(DateTime.Now);
                for (int i = 0; i < schedules.Length; ++i)
                {
                    scheduleMonths.Add((string)schedules[i].month);
                    if (schedules[i].description != (isDst ? DST : REG)) continue;
                    schedule = schedules[i].dates[0].schedule;
                }
                
                // extract the schedule for each day
                int[][] ExtractTimes(dynamic times)
                {
                    if (times is null) return new int[][] { null };
                    int[][] extracted = new int[times.Count][];
                    for (int i = 0; i < times.Count; ++i)
                    {
                        extracted[i] = new int[2] { times[i].start, times[i].end };
                    }
                    return extracted;
                }
                int[][] mon = ExtractTimes(schedule?.Mo);
                int[][] tue = ExtractTimes(schedule?.Tu);
                int[][] wed = ExtractTimes(schedule?.We);
                int[][] thu = ExtractTimes(schedule?.Th);
                int[][] fri = ExtractTimes(schedule?.Fr);
                int[][] sat = ExtractTimes(schedule?.Sa);
                int[][] sun = ExtractTimes(schedule?.Su);

                // convert the schedule from utc to the selected timezone
                int offset = (int)timezone.BaseUtcOffset.TotalHours;
                if (isDst) offset += (int)GetCurrentRule(timezone).DaylightDelta.TotalHours;
                dynamic localTimes = GenerateSchedule(mon, tue, wed, thu, fri, sat, sun, -1 * offset);
                MondaySchedule.Value = ExtractTimes(((IDictionary<string, object>)localTimes).ContainsKey("Mo") ? localTimes.Mo : null)[0];
                TuesdaySchedule.Value = ExtractTimes(((IDictionary<string, object>)localTimes).ContainsKey("Tu") ? localTimes.Tu : null)[0];
                WednesdaySchedule.Value = ExtractTimes(((IDictionary<string, object>)localTimes).ContainsKey("We") ? localTimes.We : null)[0];
                ThursdaySchedule.Value = ExtractTimes(((IDictionary<string, object>)localTimes).ContainsKey("Th") ? localTimes.Th : null)[0];
                FridaySchedule.Value = ExtractTimes(((IDictionary<string, object>)localTimes).ContainsKey("Fr") ? localTimes.Fr : null)[0];
                SaturdaySchedule.Value = ExtractTimes(((IDictionary<string, object>)localTimes).ContainsKey("Sa") ? localTimes.Sa : null)[0];
                SundaySchedule.Value = ExtractTimes(((IDictionary<string, object>)localTimes).ContainsKey("Su") ? localTimes.Su : null)[0];
            }
            elements.ForEach(element => element.IsEnabled = true);
        }

        private void SelectedTimezone_SelectionChanged(object _, SelectionChangedEventArgs _1)
        {
            string text = "Supports Daylight Saving Time: ";
            TimeZoneInfo timezone = (TimeZoneInfo)SelectedTimezone.SelectedItem;
            TimeZoneInfo.AdjustmentRule current = GetCurrentRule(timezone);
            if (current != null)
            {
                text += "Yes\n";
                DateTimeFormatInfo dateInfo = CultureInfo.CurrentCulture.DateTimeFormat;
                text += $"{(TimeSpan.Zero < current.DaylightDelta ? "+" : "")}{current.DaylightDelta} from ";
                string GenerateTransitionDescription(TimeZoneInfo.TransitionTime transition)
                {
                    string description = $"{transition.TimeOfDay.ToString("h:mm tt")} on ";
                    if (transition.Week == 1) description += "the 1st ";
                    if (transition.Week == 2) description += "the 2nd ";
                    if (transition.Week == 3) description += "the 3rd ";
                    if (transition.Week == 4) description += "the 4th ";
                    if (transition.Week == 5) description += "the last ";
                    description += $"{transition.DayOfWeek} of {dateInfo.GetMonthName(transition.Month)}";
                    return description;
                }
                text += GenerateTransitionDescription(current.DaylightTransitionStart) + " to " + GenerateTransitionDescription(current.DaylightTransitionEnd);
            }
            else text += "No";
            TimezoneHasDST.Text = text;
        }

        private dynamic GenerateSchedule(int[][] mon, int[][] tue, int[][] wed, int[][] thu, int[][] fri, int[][] sat, int[][] sun, int utcHourOffset)
        {
            dynamic schedule = new ExpandoObject();
            schedule.Mo = new List<dynamic>();
            schedule.Tu = new List<dynamic>();
            schedule.We = new List<dynamic>();
            schedule.Th = new List<dynamic>();
            schedule.Fr = new List<dynamic>();
            schedule.Sa = new List<dynamic>();
            schedule.Su = new List<dynamic>();
            void Process(int[][] values, List<dynamic> pre, List<dynamic> cur, List<dynamic> next) {
                for (int i = 0; i < values.Length; ++i)
                {
                    int[] value = values[i];
                    if (value == null) continue;
                    int start = value[0] - utcHourOffset;
                    int end = value[1] - utcHourOffset;
                    if (start < 0)
                    {
                        var preStart = 24 + start;
                        start = 0;
                        var preEnd = end < 0 ? 24 + end : 24;
                        if (end < 0) end = 0;
                        if (pre.Count > 0 && pre[pre.Count - 1].end >= preStart)
                        {
                            pre[pre.Count - 1].end = preEnd;
                        }
                        else
                        {
                            dynamic time = new ExpandoObject();
                            time.start = preStart;
                            time.end = preEnd;
                            pre.Add(time);
                        }
                    }
                    if (end > 24)
                    {
                        var nextStart = start > 24 ? start - 24 : 0;
                        if (start > 24) start = 24;
                        var nextEnd = end - 24;
                        end = 24;
                        if (next.Count > 0 && next[0].start <= nextEnd)
                        {
                            next[0].start = nextStart;
                        }
                        else
                        {
                            dynamic time = new ExpandoObject();
                            time.start = nextStart;
                            time.end = nextEnd;
                            next.Insert(0, time);
                        }
                    }
                    if (start < end)
                    {
                        int index = 0;
                        while (cur.Count > index && cur[index].start >= start)
                        {
                            ++index;
                        }
                        if (cur.Count > index && cur[index].end >= start)
                        {
                            cur[index].end = end;
                        }
                        else
                        {
                            dynamic time = new ExpandoObject();
                            time.start = start;
                            time.end = end;
                            cur.Insert(index, time);
                        }
                    }
                }
            }

            // apply the offset to generate the utc schedule
            Process(mon, schedule.Su, schedule.Mo, schedule.Tu);
            Process(tue, schedule.Mo, schedule.Tu, schedule.We);
            Process(wed, schedule.Tu, schedule.We, schedule.Th);
            Process(thu, schedule.We, schedule.Th, schedule.Fr);
            Process(fri, schedule.Th, schedule.Fr, schedule.Sa);
            Process(sat, schedule.Fr, schedule.Sa, schedule.Su);
            Process(sun, schedule.Sa, schedule.Su, schedule.Mo);

            // remove empty days
            if (schedule.Mo.Count == 0) ((IDictionary<string, object>)schedule).Remove("Mo");
            if (schedule.Tu.Count == 0) ((IDictionary<string, object>)schedule).Remove("Tu");
            if (schedule.We.Count == 0) ((IDictionary<string, object>)schedule).Remove("We");
            if (schedule.Th.Count == 0) ((IDictionary<string, object>)schedule).Remove("Th");
            if (schedule.Fr.Count == 0) ((IDictionary<string, object>)schedule).Remove("Fr");
            if (schedule.Sa.Count == 0) ((IDictionary<string, object>)schedule).Remove("Sa");
            if (schedule.Su.Count == 0) ((IDictionary<string, object>)schedule).Remove("Su");
            return schedule;
        }

        private void SaveTimezone_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = TimezoneResponseMessage;
            SQLiteCommand insert = new SQLiteCommand($"REPLACE INTO {SettingsTable} (type, value) VALUES(@timezoneType, @timezoneValue)");
            insert.Parameters.AddWithValue("@timezoneType", TimeZoneType);
            insert.Parameters.AddWithValue("@timezoneValue", ((TimeZoneInfo)SelectedTimezone.SelectedItem).Id);
            var sqlResponse = RunQuery(insert);
            if (sqlResponse is string)
            {
                button.SetApiCalls(Task.Run(() => sqlResponse));
            }
            else
            {
                List<dynamic> schedules = new List<dynamic>();
                TimeZoneInfo timezone = (TimeZoneInfo)SelectedTimezone.SelectedItem;
                TimeZoneInfo.AdjustmentRule current = GetCurrentRule(timezone);
                if (current == null)
                {
                    schedules.Add(new
                    {
                        description = REG,
                        month = "01",
                        dates = new object[]
                        {
                            new
                            {
                                date = "1",
                                schedule = GenerateSchedule(
                                    new int[][] { MondaySchedule.Value },
                                    new int[][] { TuesdaySchedule.Value },
                                    new int[][] { WednesdaySchedule.Value },
                                    new int[][] { ThursdaySchedule.Value },
                                    new int[][] { FridaySchedule.Value },
                                    new int[][] { SaturdaySchedule.Value },
                                    new int[][] { SundaySchedule.Value },
                                    (int)timezone.BaseUtcOffset.TotalHours
                                )
                            }
                        }
                    });
                }
                else
                {
                    dynamic ExtractDataFromTransition(TimeZoneInfo.TransitionTime transitionTime, int offset)
                    {
                        dynamic data = new ExpandoObject();

                        // calculate time
                        data.minute = transitionTime.TimeOfDay.Minute; // TODO update when we support timezones that have minute offsets
                        data.hour = transitionTime.TimeOfDay.Hour - offset;

                        // calculate date
                        int day = (int)transitionTime.DayOfWeek;
                        string dayString = null;
                        if (day == 0) dayString = "Su";
                        if (day == 1) dayString = "Mo";
                        if (day == 2) dayString = "Tu";
                        if (day == 3) dayString = "We";
                        if (day == 4) dayString = "Th";
                        if (day == 5) dayString = "Fr";
                        if (day == 6) dayString = "Sa";
                        data.date = $"{dayString}{(transitionTime.Week == 5 ? -1 : transitionTime.Week)}";

                        // calculate months
                        data.month = $"{(transitionTime.Month < 10 ? "0" : "")}{transitionTime.Month}";
                        data.overlap = null;
                        if (data.hour < 0 && transitionTime.Week == 1)
                        {
                            int month = transitionTime.Month == 1 ? 12 : transitionTime.Month - 1;
                            data.overlap = new
                            {
                                month = $"{(month < 10 ? "0" : "")}{month}",
                                date = $"{dayString}6"
                            };
                        }
                        if (data.hour > 23 && transitionTime.Week == 5)
                        {
                            int month = transitionTime.Month == 12 ? 1 : transitionTime.Month + 1;
                            data.overlap = new
                            {
                                month = $"{(month < 10 ? "0" : "")}{month}",
                                date = $"{dayString}0"
                            };
                        }
                        return data;
                    }
                    dynamic dst = ExtractDataFromTransition(current.DaylightTransitionStart, (int)timezone.BaseUtcOffset.TotalHours);
                    schedules.Add(new
                    {
                        description = DST,
                        dst.month,
                        dates = new object[]
                        {
                            new
                            {
                                dst.date,
                                dst.hour,
                                dst.minute,
                                schedule = GenerateSchedule(
                                    new int[][] { MondaySchedule.Value },
                                    new int[][] { TuesdaySchedule.Value },
                                    new int[][] { WednesdaySchedule.Value },
                                    new int[][] { ThursdaySchedule.Value },
                                    new int[][] { FridaySchedule.Value },
                                    new int[][] { SaturdaySchedule.Value },
                                    new int[][] { SundaySchedule.Value },
                                    (int)timezone.BaseUtcOffset.TotalHours + (int)current.DaylightDelta.TotalHours
                                )
                            }
                        }
                    });
                    if (dst.overlap != null)
                    {
                        schedules.Add(new
                        {
                            description = $"{DST} - overlap",
                            dst.overlap.month,
                            dates = new object[]
                            {
                                new
                                {
                                    dst.overlap.date,
                                    dst.hour,
                                    dst.minute,
                                    schedule = GenerateSchedule(
                                        new int[][] { MondaySchedule.Value },
                                        new int[][] { TuesdaySchedule.Value },
                                        new int[][] { WednesdaySchedule.Value },
                                        new int[][] { ThursdaySchedule.Value },
                                        new int[][] { FridaySchedule.Value },
                                        new int[][] { SaturdaySchedule.Value },
                                        new int[][] { SundaySchedule.Value },
                                        (int)timezone.BaseUtcOffset.TotalHours + (int)current.DaylightDelta.TotalHours
                                    )
                                }
                            }
                        });
                    }
                    dynamic reg = ExtractDataFromTransition(current.DaylightTransitionEnd, (int)timezone.BaseUtcOffset.TotalHours + (int)current.DaylightDelta.TotalHours);
                    schedules.Add(new
                    {
                        description = REG,
                        reg.month,
                        dates = new object[]
                        {
                            new
                            {
                                reg.date,
                                reg.hour,
                                reg.minute,
                                schedule = GenerateSchedule(
                                    new int[][] { MondaySchedule.Value },
                                    new int[][] { TuesdaySchedule.Value },
                                    new int[][] { WednesdaySchedule.Value },
                                    new int[][] { ThursdaySchedule.Value },
                                    new int[][] { FridaySchedule.Value },
                                    new int[][] { SaturdaySchedule.Value },
                                    new int[][] { SundaySchedule.Value },
                                    (int)timezone.BaseUtcOffset.TotalHours
                                )
                            }
                        }
                    });
                    if (reg.overlap != null)
                    {
                        schedules.Add(new
                        {
                            description = $"{REG} - overlap",
                            reg.overlap.month,
                            dates = new object[]
                            {
                                new
                                {
                                    reg.overlap.date,
                                    reg.hour,
                                    reg.minute,
                                    schedule = GenerateSchedule(
                                        new int[][] { MondaySchedule.Value },
                                        new int[][] { TuesdaySchedule.Value },
                                        new int[][] { WednesdaySchedule.Value },
                                        new int[][] { ThursdaySchedule.Value },
                                        new int[][] { FridaySchedule.Value },
                                        new int[][] { SaturdaySchedule.Value },
                                        new int[][] { SundaySchedule.Value },
                                        (int)timezone.BaseUtcOffset.TotalHours
                                    )
                                }
                            }
                        });
                    }
                }

                // make put calls for all the months we are adding and delete calls for the remaining months
                List<Task<dynamic>> apiCalls = new List<Task<dynamic>>();
                schedules.ForEach(schedule =>
                {
                    scheduleMonths.Remove(schedule.month);
                    apiCalls.Add(CallApiEndpoint(HttpMethod.Put, "/org/reminders/schedule", schedule));
                });
                foreach (string month in scheduleMonths)
                {
                    apiCalls.Add(CallApiEndpoint(HttpMethod.Delete, "/org/reminders/schedule", new { month }));
                }
                button.SetApiCalls(apiCalls);
                button.Callback = (_1) =>
                {
                    scheduleMonths.Clear();
                    schedules.ForEach(schedule => scheduleMonths.Add(schedule.month));
                };
            }
        }
        /* END SCHEDULES TAB FUNCTIONS */

        /* START SETTINGS TAB FUNCTIONS */
        private async void Settings_Visibility_Listener(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            List<UIElement> elements = GetAllElements((Grid)sender, new Type[] { typeof(ApiCallButton), typeof(TextInput) });
            elements.ForEach(element => element.IsEnabled = false);

            // get the saved values for url and key
            var url = GetBaseUrl();
            if (url != null) ApiUrl.Value = url;
            var key = GetApiKey();
            if (key != null) ApiKey.Value = key;

            // query for the sms redirect value
            if (url != null && key != null)
            {
                SmsRedirectResponseMessage.Text = "";
                var response = await CallApiEndpoint(HttpMethod.Get, "/org/smsCallRedirect", null, url, key);
                if (response is string)
                {
                    SmsRedirectResponseMessage.Text = response;
                }
                else
                {
                    List<object> smsRedirects = response.numbers;
                    if (smsRedirects.Count > 0) SMSRedirect1.Value = (string)smsRedirects[0];
                    if (smsRedirects.Count > 1) SMSRedirect2.Value = (string)smsRedirects[1];
                    if (smsRedirects.Count > 2) SMSRedirect3.Value = (string)smsRedirects[2];
                    if (smsRedirects.Count > 3) SMSRedirect4.Value = (string)smsRedirects[3];
                    if (smsRedirects.Count > 4) SMSRedirect5.Value = (string)smsRedirects[4];
                }
            }
            elements.ForEach(element => element.IsEnabled = true);
        }

        private void HealthCheck_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = HealthcheckResponseMessage;
            button.SetApiCalls(CallApiEndpoint(HttpMethod.Get, "/healthcheck", null, ApiUrl.Value, ApiKey.Value));
        }

        private void SaveApiSettings_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = HealthcheckResponseMessage;
            button.SetApiCalls(AsyncRunQuery(() =>
            {
                SQLiteCommand insert = new SQLiteCommand($"REPLACE INTO {SettingsTable} (type, value) VALUES(@urlType, @urlValue),(@keyType, @keyValue)");
                insert.Parameters.AddWithValue("@urlType", ApiUrlType);
                insert.Parameters.AddWithValue("@urlValue", ApiUrl.Value);
                insert.Parameters.AddWithValue("@keyType", ApiKeyType);
                insert.Parameters.AddWithValue("@keyValue", ApiKey.Value);
                return insert;
            }));
        }

        private void SmsRedirect_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = SmsRedirectResponseMessage;
            List<string> numbers = new List<string>();
            if (SMSRedirect1.Value != "") numbers.Add(SMSRedirect1.Value);
            if (SMSRedirect2.Value != "") numbers.Add(SMSRedirect2.Value);
            if (SMSRedirect3.Value != "") numbers.Add(SMSRedirect3.Value);
            if (SMSRedirect4.Value != "") numbers.Add(SMSRedirect4.Value);
            if (SMSRedirect5.Value != "") numbers.Add(SMSRedirect5.Value);
            dynamic parameters = new ExpandoObject();
            if (numbers.Count != 0) parameters.numbers = numbers;
            button.SetApiCalls(CallApiEndpoint(HttpMethod.Put, "/org/smsCallRedirect", parameters));
        }
        /* END SETTINGS TAB FUNCTIONS */
    }
}
