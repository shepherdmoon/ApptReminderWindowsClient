using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for SchedulesTab.xaml
    /// </summary>
    public partial class SchedulesTab : UserControl
    {
        private readonly string TimeZoneType = "TimeZone";
        public SchedulesTab()
        {
            InitializeComponent();
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

        public Helper Utils
        {
            get => (Helper)GetValue(UtilsProperty);
            set => SetValue(UtilsProperty, value);
        }
        public static readonly DependencyProperty UtilsProperty = DependencyProperty.Register("Utils", typeof(Helper), typeof(SchedulesTab), new UIPropertyMetadata(null));

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

        private readonly string DST = "DST";
        private readonly string REG = "Regular";
        private readonly HashSet<string> scheduleMonths = new HashSet<string>();
        private async void Schedules_Visibility_Listener(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            List<UIElement> elements = Helper.GetAllElements((Grid)sender, new Type[] { typeof(ApiCallButton), typeof(ScheduleInput), typeof(ComboBox) });
            elements.ForEach(element => element.IsEnabled = false);

            // get the saved timezone, default to the computer's selected timezone
            var timezoneResponse = Utils.RunQuery(new SQLiteCommand($"SELECT value FROM {Helper.SettingsTable} WHERE type='{TimeZoneType}'"));
            if (timezoneResponse is List<Dictionary<string, object>> rows && rows.Count == 1) SelectedTimezone.SelectedItem = TimeZoneInfo.FindSystemTimeZoneById((string)rows[0]["value"]);
            else SelectedTimezone.SelectedItem = TimeZoneInfo.Local;

            // get the existing schedule
            scheduleMonths.Clear();
            dynamic response = await Utils.CallApiEndpoint(HttpMethod.Get, "/org/reminders/schedule");
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
            void Process(int[][] values, List<dynamic> pre, List<dynamic> cur, List<dynamic> next)
            {
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
            SQLiteCommand insert = new SQLiteCommand($"REPLACE INTO {Helper.SettingsTable} (type, value) VALUES(@timezoneType, @timezoneValue)");
            insert.Parameters.AddWithValue("@timezoneType", TimeZoneType);
            insert.Parameters.AddWithValue("@timezoneValue", ((TimeZoneInfo)SelectedTimezone.SelectedItem).Id);
            var sqlResponse = Utils.RunQuery(insert);
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
                    apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Put, "/org/reminders/schedule", schedule));
                });
                foreach (string month in scheduleMonths)
                {
                    apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Delete, "/org/reminders/schedule", new { month }));
                }
                button.SetApiCalls(apiCalls);
                button.Callback = (_1) =>
                {
                    scheduleMonths.Clear();
                    schedules.ForEach(schedule => scheduleMonths.Add(schedule.month));
                };
            }
        }
    }
}
